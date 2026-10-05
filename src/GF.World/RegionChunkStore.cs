using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using GF.Core;

namespace GF.World;

/// <summary>
/// Almacén de chunks por REGIONES: muchos chunks (por defecto hasta 16x16x16) en un único archivo r.X.Y.Z.rgn, en vez de
/// un archivo por chunk. Cada archivo es un registro de solo-añadir:
///     cabecera (forma del chunk, tamaño de celda, dimensiones de la región)
///     { 0xC7, slot:int32, longitud:int32, datos comprimidos (Deflate) } ...
/// - ÍNDICE: al abrir una región se recorren las cabeceras de los registros y se construye en memoria slot -> (offset, longitud);
///   la última versión de cada slot gana. Leer un chunk es un Seek + Read, sin tocar el resto del archivo.
/// - ESCRITURA: añade al final (sin gestionar huecos). Si el proceso muere a medias, la cola truncada o corrupta se descarta al
///   reabrir y los chunks anteriores siguen intactos.
/// - COMPACTACIÓN: al cerrar una región, si las versiones obsoletas pesan más que las vigentes (y pasan de 64 KB) se reescribe el archivo.
/// - CACHÉ: como máximo maxOpenRegions regiones abiertas (con su índice); se expulsa la menos usada recientemente.
/// - COMPATIBILIDAD: si no hay datos en regiones, consulta 'fallback' (p. ej. el FileChunkStore anterior, un archivo por chunk).
/// Hilos: TryLoad puede llamarse desde hilos de fondo y Save desde el principal; el acceso a los archivos se serializa con un cerrojo,
/// pero la compresión/descompresión se hace fuera de él.
/// </summary>
public sealed class RegionChunkStore<TCell> : IChunkStore<TCell>, IDisposable where TCell : unmanaged
{
    private readonly record struct RegionKey(int X, int Y, int Z);

    /// <summary>Marca que, tras las celdas, el payload trae los cambios de instancia de los bloques. Los payloads antiguos no la tienen.</summary>
    private const int PropertiesMarker = 0xA5;

    private readonly string _dir;
    private readonly int _rx, _ry, _rz, _maxOpen;
    private readonly Func<ChunkCoord, ChunkCoord>? _canonical;
    private readonly IChunkStore<TCell>? _fallback;
    private readonly object _gate = new();
    private readonly Dictionary<RegionKey, RegionFile> _regions = new();
    private long _tick;
    private bool _disposed;

    public RegionChunkStore(string directory, int regionChunksX = 16, int regionChunksY = 16, int regionChunksZ = 16,
        int maxOpenRegions = 16, Func<ChunkCoord, ChunkCoord>? canonicalize = null, IChunkStore<TCell>? fallback = null)
    {
        _dir = directory;
        _rx = Math.Max(1, regionChunksX);
        _ry = Math.Max(1, regionChunksY);
        _rz = Math.Max(1, regionChunksZ);
        _maxOpen = Math.Max(1, maxOpenRegions);
        _canonical = canonicalize;
        _fallback = fallback;
        Directory.CreateDirectory(directory);
    }

    // Estadísticas (para depuración).
    public int OpenRegions => _regions.Count;
    public long CacheHits { get; private set; }
    public long CacheMisses { get; private set; }
    public long ChunksWritten { get; private set; }

    public bool TryLoad(Chunk<TCell> chunk)
    {
        var (key, slot) = Locate(chunk.Coord);
        byte[]? payload = null;
        lock (_gate)
        {
            if (_disposed) return false;   // un trabajo de fondo rezagado tras cerrar la partida
            var region = GetRegion(key, chunk.Shape, create: false);
            if (region != null && region.TryRead(slot, out var data)) payload = data;
        }
        if (payload != null) return Inflate(payload, chunk);
        return _fallback != null && _fallback.TryLoad(chunk);
    }

    public void Save(Chunk<TCell> chunk)
    {
        var (key, slot) = Locate(chunk.Coord);
        byte[] payload = Deflate(chunk);   // fuera del cerrojo
        lock (_gate)
        {
            if (_disposed) return;
            GetRegion(key, chunk.Shape, create: true)!.Append(slot, payload);
            ChunksWritten++;
        }
    }

    /// <summary>Fuerza a disco todo lo pendiente (llamar tras SaveAll / en el autoguardado).</summary>
    public void Flush()
    {
        lock (_gate)
        {
            foreach (var r in _regions.Values) r.Flush();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var r in _regions.Values) r.Close();
            _regions.Clear();
        }
    }

    // ------------------------------------------------------------------ internos

    private (RegionKey Key, int Slot) Locate(ChunkCoord coord)
    {
        if (_canonical != null) coord = _canonical(coord);
        int rx = IntMath.FloorDiv(coord.X, _rx), ry = IntMath.FloorDiv(coord.Y, _ry), rz = IntMath.FloorDiv(coord.Z, _rz);
        int lx = coord.X - rx * _rx, ly = coord.Y - ry * _ry, lz = coord.Z - rz * _rz;
        return (new RegionKey(rx, ry, rz), (ly * _rz + lz) * _rx + lx);
    }

    private string PathOf(RegionKey k) => Path.Combine(_dir, $"r.{k.X}.{k.Y}.{k.Z}.rgn");

    private RegionLayout LayoutOf(ChunkShape s) =>
        new(s.SizeX, s.SizeY, s.SizeZ, Unsafe.SizeOf<TCell>(), _rx, _ry, _rz);

    /// <summary>Debe llamarse con el cerrojo tomado.</summary>
    private RegionFile? GetRegion(RegionKey key, ChunkShape shape, bool create)
    {
        if (_regions.TryGetValue(key, out var region))
        {
            CacheHits++;
            region.LastUsed = ++_tick;
            return region;
        }

        var path = PathOf(key);
        if (!create && !File.Exists(path)) return null;   // leer un chunk que no existe no crea archivos

        CacheMisses++;
        if (_regions.Count >= _maxOpen) EvictLeastRecentlyUsed();
        region = RegionFile.Open(path, LayoutOf(shape));
        region.LastUsed = ++_tick;
        _regions[key] = region;
        return region;
    }

    private void EvictLeastRecentlyUsed()
    {
        RegionKey? oldest = null;
        long min = long.MaxValue;
        foreach (var (key, region) in _regions)
            if (region.LastUsed < min) { min = region.LastUsed; oldest = key; }
        if (oldest is not { } k) return;
        _regions[k].Close();
        _regions.Remove(k);
    }

    private static byte[] Deflate(Chunk<TCell> chunk)
    {
        using var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            ds.Write(MemoryMarshal.AsBytes(chunk.Cells.AsSpan()));
            if (chunk.CellProperties is { Count: > 0 } properties)
            {
                ds.WriteByte(PropertiesMarker);
                using var writer = new BinaryWriter(ds, Encoding.UTF8, leaveOpen: true);
                PropertySerializer.WriteCellMap(writer, properties);
            }
        }
        return ms.ToArray();
    }

    private static Dictionary<int, PropertyOverrides>? ReadProperties(Stream ds)
    {
        if (ds.ReadByte() != PropertiesMarker) return null;   // payload antiguo o sin cambios de instancia
        try
        {
            using var reader = new BinaryReader(ds, Encoding.UTF8, leaveOpen: true);
            return PropertySerializer.ReadCellMap(reader);
        }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException or IOException or FormatException)
        {
            return null;   // las celdas son lo importante: si las propiedades están dañadas, se pierden solo ellas
        }
    }

    private static bool Inflate(byte[] payload, Chunk<TCell> chunk)
    {
        try
        {
            using var ms = new MemoryStream(payload, writable: false);
            using var ds = new DeflateStream(ms, CompressionMode.Decompress);
            ds.ReadExactly(MemoryMarshal.AsBytes(chunk.Cells.AsSpan()));
            chunk.CellProperties = ReadProperties(ds);
            return true;
        }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException or IOException)
        {
            Array.Clear(chunk.Cells);   // dato corrupto: que se regenere
            return false;
        }
    }
}

internal readonly record struct RegionLayout(int ShapeX, int ShapeY, int ShapeZ, int CellSize, int RegionX, int RegionY, int RegionZ);

/// <summary>Un archivo de región abierto: índice en memoria + registro de solo-añadir. No es thread-safe (lo protege el almacén).</summary>
internal sealed class RegionFile
{
    private const int Magic = 0x314E4752;   // "RGN1"
    private const int HeaderSize = 32;
    private const int RecordHeaderSize = 9;
    private const byte RecordMagic = 0xC7;
    private const long MinGarbageToCompact = 64 * 1024;

    private readonly string _path;
    private readonly RegionLayout _layout;
    private readonly int _slots;
    private readonly Dictionary<int, (long Offset, int Length)> _index = new();
    private FileStream _stream = null!;
    private long _end;
    private long _live, _garbage;
    private bool _dirty;

    public long LastUsed;

    private RegionFile(string path, RegionLayout layout)
    {
        _path = path;
        _layout = layout;
        _slots = layout.RegionX * layout.RegionY * layout.RegionZ;
    }

    public static RegionFile Open(string path, RegionLayout layout)
    {
        var file = new RegionFile(path, layout);
        file.OpenOrCreate();
        return file;
    }

    private void OpenOrCreate()
    {
        if (File.Exists(_path))
        {
            _stream = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, 16384);
            if (HeaderMatches())
            {
                Scan();
                return;
            }
            // Forma/tamaño distintos: no se reutiliza, pero tampoco se destruye.
            _stream.Dispose();
            File.Move(_path, _path + ".bad", overwrite: true);
        }

        _stream = new FileStream(_path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 16384);
        WriteHeader(_stream);
        _end = HeaderSize;
        _dirty = true;
    }

    private void BuildHeader(Span<byte> h)
    {
        BinaryPrimitives.WriteInt32LittleEndian(h, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(h[4..], _layout.ShapeX);
        BinaryPrimitives.WriteInt32LittleEndian(h[8..], _layout.ShapeY);
        BinaryPrimitives.WriteInt32LittleEndian(h[12..], _layout.ShapeZ);
        BinaryPrimitives.WriteInt32LittleEndian(h[16..], _layout.CellSize);
        BinaryPrimitives.WriteInt32LittleEndian(h[20..], _layout.RegionX);
        BinaryPrimitives.WriteInt32LittleEndian(h[24..], _layout.RegionY);
        BinaryPrimitives.WriteInt32LittleEndian(h[28..], _layout.RegionZ);
    }

    private void WriteHeader(Stream s)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        BuildHeader(header);
        s.Write(header);
    }

    private bool HeaderMatches()
    {
        if (_stream.Length < HeaderSize) return false;
        Span<byte> actual = stackalloc byte[HeaderSize];
        Span<byte> expected = stackalloc byte[HeaderSize];
        _stream.Position = 0;
        _stream.ReadExactly(actual);
        BuildHeader(expected);
        return actual.SequenceEqual(expected);
    }

    /// <summary>Construye el índice recorriendo las cabeceras de los registros. Descarta una cola truncada o corrupta.</summary>
    private void Scan()
    {
        long length = _stream.Length;
        long pos = HeaderSize;
        Span<byte> h = stackalloc byte[RecordHeaderSize];

        while (pos + RecordHeaderSize <= length)
        {
            _stream.Position = pos;
            _stream.ReadExactly(h);
            int slot = BinaryPrimitives.ReadInt32LittleEndian(h[1..]);
            int len = BinaryPrimitives.ReadInt32LittleEndian(h[5..]);
            if (h[0] != RecordMagic || len < 0 || (uint)slot >= (uint)_slots || pos + RecordHeaderSize + len > length) break;

            if (_index.TryGetValue(slot, out var old))
            {
                _live -= old.Length + RecordHeaderSize;
                _garbage += old.Length + RecordHeaderSize;
            }
            _index[slot] = (pos + RecordHeaderSize, len);
            _live += len + RecordHeaderSize;
            pos += RecordHeaderSize + len;
        }

        if (pos < length) _stream.SetLength(pos);
        _end = pos;
    }

    public bool TryRead(int slot, out byte[] payload)
    {
        if (!_index.TryGetValue(slot, out var entry))
        {
            payload = Array.Empty<byte>();
            return false;
        }
        payload = new byte[entry.Length];
        _stream.Position = entry.Offset;
        _stream.ReadExactly(payload);
        return true;
    }

    public void Append(int slot, byte[] payload)
    {
        Span<byte> h = stackalloc byte[RecordHeaderSize];
        h[0] = RecordMagic;
        BinaryPrimitives.WriteInt32LittleEndian(h[1..], slot);
        BinaryPrimitives.WriteInt32LittleEndian(h[5..], payload.Length);

        _stream.Position = _end;
        _stream.Write(h);
        _stream.Write(payload);

        if (_index.TryGetValue(slot, out var old))
        {
            _live -= old.Length + RecordHeaderSize;
            _garbage += old.Length + RecordHeaderSize;
        }
        _index[slot] = (_end + RecordHeaderSize, payload.Length);
        _live += payload.Length + RecordHeaderSize;
        _end += RecordHeaderSize + payload.Length;
        _dirty = true;
    }

    public void Flush()
    {
        if (!_dirty) return;
        _stream.Flush(flushToDisk: true);
        _dirty = false;
    }

    /// <summary>Vacía, compacta si compensa y cierra.</summary>
    public void Close()
    {
        Flush();
        if (_garbage > MinGarbageToCompact && _garbage > _live) Compact();
        else _stream.Dispose();
    }

    private void Compact()
    {
        var tmp = _path + ".tmp";
        using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 16384))
        {
            WriteHeader(output);
            Span<byte> h = stackalloc byte[RecordHeaderSize];
            foreach (int slot in _index.Keys.OrderBy(k => k))
            {
                var entry = _index[slot];
                var payload = new byte[entry.Length];
                _stream.Position = entry.Offset;
                _stream.ReadExactly(payload);

                h[0] = RecordMagic;
                BinaryPrimitives.WriteInt32LittleEndian(h[1..], slot);
                BinaryPrimitives.WriteInt32LittleEndian(h[5..], payload.Length);
                output.Write(h);
                output.Write(payload);
            }
            output.Flush(flushToDisk: true);
        }
        _stream.Dispose();
        File.Move(tmp, _path, overwrite: true);
    }
}
