using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using GF.Core;

namespace GF.World;

/// <summary>
/// Carga/descarga chunks alrededor de un punto. Genera en el ThreadPool e incorpora los resultados al mundo en el hilo
/// que llama a Update (el principal).
/// Planificación:
///  - MaxConcurrentJobs limita los trabajos EN EJECUCIÓN (antes contaba también los ya terminados pendientes de incorporar,
///    y la cola se atascaba por el límite de incorporaciones por frame).
///  - Los resultados se incorporan con un presupuesto de TIEMPO por frame (ApplyBudgetMs), no un número fijo.
///  - El recorrido de chunks pedidos/descargados solo se repite cuando hace falta (cambia el centro, hay cupo pendiente...).
/// </summary>
public sealed class ChunkManager<TCell> : IDisposable where TCell : unmanaged
{
    private readonly World<TCell> _world;
    private readonly WorldGenerator<TCell> _generator;
    private readonly HashSet<ChunkCoord> _pending = new();           // pedidos y aún no incorporados (en ejecución o en la cola)
    private readonly ConcurrentQueue<Chunk<TCell>> _completed = new();
    private readonly ConcurrentQueue<Exception> _failures = new();
    private readonly List<ChunkCoord> _toUnload = new();
    private ChunkCoord[] _offsets = Array.Empty<ChunkCoord>();
    private (int X, int Y, int Z) _offsetsFor = (-1, -1, -1);
    private ChunkCoord _lastCenter;
    private bool _hasCenter, _scanNeeded = true, _disposed;
    private int _running;
    private long _generated, _generationTicks;

    public int RadiusX { get; set; } = 4;
    public int RadiusY { get; set; } = 2;
    public int RadiusZ { get; set; } = 4;
    /// <summary>Límites verticales en unidades de chunk (roguelike 2D: 0..0).</summary>
    public int MinChunkY { get; set; } = int.MinValue;
    public int MaxChunkY { get; set; } = int.MaxValue;
    /// <summary>Límites en Z (p. ej. los polos de un mundo rectangular).</summary>
    public int MinChunkZ { get; set; } = int.MinValue;
    public int MaxChunkZ { get; set; } = int.MaxValue;

    /// <summary>Chunks generándose a la vez (hilos de fondo dedicados a generar).</summary>
    public int MaxConcurrentJobs { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);
    /// <summary>Máximo de chunks ya generados esperando a ser incorporados; evita acumular trabajo que el hilo principal no absorbe.</summary>
    public int MaxBacklog { get; set; } = 32;
    /// <summary>Tiempo máximo por frame dedicado a incorporar chunks al mundo.</summary>
    public double ApplyBudgetMs { get; set; } = 2.0;
    /// <summary>Tope de seguridad de incorporaciones por frame (el límite real es ApplyBudgetMs).</summary>
    public int MaxChunksAppliedPerFrame { get; set; } = 64;
    /// <summary>Si se asigna: los chunks guardados se cargan en vez de generarse, y los editados se guardan al descargarse.</summary>
    public IChunkStore<TCell>? Store { get; set; }

    // Estadísticas.
    public int RunningJobs => Volatile.Read(ref _running);
    public int PendingCount => _pending.Count;
    public long GeneratedChunks => Interlocked.Read(ref _generated);
    public double AverageGenerationMs
    {
        get
        {
            long n = Interlocked.Read(ref _generated);
            return n == 0 ? 0 : Interlocked.Read(ref _generationTicks) * 1000.0 / Stopwatch.Frequency / n;
        }
    }

    public ChunkManager(World<TCell> world, WorldGenerator<TCell> generator)
    {
        _world = world;
        _generator = generator;
    }

    public void Update(CellCoord center)
    {
        if (_disposed) return;
        if (_failures.TryDequeue(out var ex)) ExceptionDispatchInfo.Capture(ex).Throw();

        var cc = _world.Shape.ToChunk(center);
        bool changed = EnsureOffsets() || !_hasCenter || cc != _lastCenter;
        _lastCenter = cc;
        _hasCenter = true;
        if (changed) _scanNeeded = true;

        ApplyCompleted(cc);
        if (changed) Unload(cc);
        if (_scanNeeded) RequestMissing(cc);
    }

    /// <summary>
    /// ¿Están ya cargados todos los vecinos de este chunk que van a cargarse? Sirve para esperar a mallar hasta que los datos
    /// de los bordes estén completos y no mallar el mismo chunk varias veces mientras el mundo se va cargando.
    /// Los vecinos fuera del radio, de los límites verticales o de los polos no cuentan (nunca llegarán).
    /// </summary>
    public bool AreNeighborsSettled(ChunkCoord c)
    {
        if (!_hasCenter) return true;
        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0 && dz == 0) continue;
            var n = new ChunkCoord(c.X + dx, c.Y + dy, c.Z + dz);
            if (_world.GetChunk(n) != null) continue;
            if (IsDesired(n)) return false;
        }
        return true;
    }

    private bool IsDesired(ChunkCoord c) =>
        c.Y >= MinChunkY && c.Y <= MaxChunkY && c.Z >= MinChunkZ && c.Z <= MaxChunkZ && InRange(c, _lastCenter, 0);

    private void ApplyCompleted(ChunkCoord cc)
    {
        long start = Stopwatch.GetTimestamp();
        int applied = 0;
        while (applied < MaxChunksAppliedPerFrame && _completed.TryDequeue(out var chunk))
        {
            _pending.Remove(chunk.Coord);
            if (InRange(chunk.Coord, cc, 0) && _world.GetChunk(chunk.Coord) == null)
            {
                _world.AddChunk(chunk);
                applied++;
            }
            else _scanNeeded = true;   // descartado (ya fuera de rango): puede que haya que pedirlo de nuevo

            if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= ApplyBudgetMs) break;
        }
    }

    private void Unload(ChunkCoord cc)
    {
        _toUnload.Clear();
        foreach (var chunk in _world.Chunks)
        {
            if (InRange(chunk.Coord, cc, 1)) continue;   // 1 chunk de histéresis para evitar parpadeo en el borde
            if (Store != null && chunk.IsModified) { Store.Save(chunk); chunk.IsModified = false; }
            _toUnload.Add(chunk.Coord);
        }
        foreach (var c in _toUnload) _world.RemoveChunk(c);
    }

    private void RequestMissing(ChunkCoord cc)
    {
        bool capped = false;
        foreach (var off in _offsets)   // ordenados del más cercano al más lejano
        {
            var coord = cc + off;
            if (coord.Y < MinChunkY || coord.Y > MaxChunkY || coord.Z < MinChunkZ || coord.Z > MaxChunkZ) continue;
            if (_pending.Contains(coord) || _world.GetChunk(coord) != null) continue;

            if (Volatile.Read(ref _running) >= MaxConcurrentJobs || _completed.Count >= MaxBacklog)
            {
                capped = true;   // seguimos pidiendo en los siguientes frames
                break;
            }
            Start(coord);
        }
        _scanNeeded = capped;
    }

    private void Start(ChunkCoord coord)
    {
        _pending.Add(coord);
        Interlocked.Increment(ref _running);

        var shape = _world.Shape;
        var gen = _generator;
        var store = Store;
        Task.Run(() =>
        {
            try
            {
                long t0 = Stopwatch.GetTimestamp();
                var chunk = new Chunk<TCell>(coord, shape);
                if (store == null || !store.TryLoad(chunk)) gen.Generate(chunk);
                Interlocked.Add(ref _generationTicks, Stopwatch.GetTimestamp() - t0);
                Interlocked.Increment(ref _generated);
                _completed.Enqueue(chunk);
            }
            catch (Exception e) { _failures.Enqueue(e); }
            finally { Interlocked.Decrement(ref _running); }
        });
    }

    private bool InRange(ChunkCoord c, ChunkCoord center, int margin) =>
        Math.Abs(c.X - center.X) <= RadiusX + margin &&
        Math.Abs(c.Y - center.Y) <= RadiusY + margin &&
        Math.Abs(c.Z - center.Z) <= RadiusZ + margin;

    /// <summary>Recalcula la lista ordenada de desplazamientos si cambió el radio. Devuelve true si cambió.</summary>
    private bool EnsureOffsets()
    {
        var key = (RadiusX, RadiusY, RadiusZ);
        if (key == _offsetsFor) return false;
        var list = new List<ChunkCoord>();
        for (int y = -RadiusY; y <= RadiusY; y++)
        for (int z = -RadiusZ; z <= RadiusZ; z++)
        for (int x = -RadiusX; x <= RadiusX; x++)
            list.Add(new ChunkCoord(x, y, z));
        list.Sort((a, b) => Dist2(a).CompareTo(Dist2(b)));
        _offsets = list.ToArray();
        _offsetsFor = key;
        return true;
    }

    private static int Dist2(ChunkCoord c) => c.X * c.X + c.Y * c.Y + c.Z * c.Z;

    /// <summary>Guarda todos los chunks cargados que hayan sido editados (p. ej. al salir o en autoguardado).</summary>
    public void SaveAll()
    {
        if (Store == null) return;
        foreach (var chunk in _world.Chunks)
            if (chunk.IsModified) { Store.Save(chunk); chunk.IsModified = false; }
    }

    public void Dispose() => _disposed = true;
}
