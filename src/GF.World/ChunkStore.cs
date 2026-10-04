using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GF.Core;

namespace GF.World;

/// <summary>Almacén de chunks. TryLoad puede llamarse desde hilos de fondo; Save se llama desde el hilo principal.</summary>
public interface IChunkStore<TCell> where TCell : unmanaged
{
    /// <summary>Rellena chunk.Cells y devuelve true si hay datos válidos guardados para chunk.Coord.</summary>
    bool TryLoad(Chunk<TCell> chunk);
    void Save(Chunk<TCell> chunk);
}

/// <summary>
/// Un archivo comprimido (Deflate) por chunk: c.X.Y.Z.bin. Cabecera con forma y tamaño de celda
/// para rechazar datos incompatibles. Escritura atómica (archivo temporal + Move).
/// Sirve para cualquier TCell unmanaged (voxel o roguelike).
/// </summary>
public sealed class FileChunkStore<TCell> : IChunkStore<TCell> where TCell : unmanaged
{
    private const int Magic = 0x314B4843;   // "CHK1"
    private const int HeaderSize = 20;
    private readonly string _dir;
    private readonly Func<ChunkCoord, ChunkCoord>? _canonical;

    /// <param name="canonicalize">Opcional: normaliza la coordenada usada como nombre de archivo
    /// (mundos que se envuelven en X: el chunk -1 y el N-1 son el mismo lugar).</param>
    public FileChunkStore(string directory, Func<ChunkCoord, ChunkCoord>? canonicalize = null)
    {
        _dir = directory;
        _canonical = canonicalize;
        Directory.CreateDirectory(directory);
    }

    private string PathOf(ChunkCoord c)
    {
        if (_canonical != null) c = _canonical(c);
        return Path.Combine(_dir, $"c.{c.X}.{c.Y}.{c.Z}.bin");
    }

    private static void WriteHeader(Span<byte> h, ChunkShape s)
    {
        BinaryPrimitives.WriteInt32LittleEndian(h, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(h[4..], s.SizeX);
        BinaryPrimitives.WriteInt32LittleEndian(h[8..], s.SizeY);
        BinaryPrimitives.WriteInt32LittleEndian(h[12..], s.SizeZ);
        BinaryPrimitives.WriteInt32LittleEndian(h[16..], Unsafe.SizeOf<TCell>());
    }

    public bool TryLoad(Chunk<TCell> chunk)
    {
        var path = PathOf(chunk.Coord);
        if (!File.Exists(path)) return false;

        try
        {
            using var fs = File.OpenRead(path);
            using var ds = new DeflateStream(fs, CompressionMode.Decompress);

            Span<byte> actual = stackalloc byte[HeaderSize];
            Span<byte> expected = stackalloc byte[HeaderSize];
            ds.ReadExactly(actual);
            WriteHeader(expected, chunk.Shape);
            if (!actual.SequenceEqual(expected)) return false;

            ds.ReadExactly(MemoryMarshal.AsBytes(chunk.Cells.AsSpan()));
            return true;
        }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException or IOException)
        {
            Array.Clear(chunk.Cells);   // no dejar datos a medias antes de regenerar
            return false;
        }
    }

    public void Save(Chunk<TCell> chunk)
    {
        var path = PathOf(chunk.Coord);
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
        using (var ds = new DeflateStream(fs, CompressionLevel.Fastest))
        {
            Span<byte> header = stackalloc byte[HeaderSize];
            WriteHeader(header, chunk.Shape);
            ds.Write(header);
            ds.Write(MemoryMarshal.AsBytes(chunk.Cells.AsSpan()));
        }
        File.Move(tmp, path, overwrite: true);
    }
}
