using GF.Core;

namespace GF.World;

/// <summary>Mundo en memoria basado en chunks. Uso exclusivo desde el hilo principal.</summary>
public sealed class World<TCell> : IWorld<TCell> where TCell : unmanaged
{
    private readonly Dictionary<ChunkCoord, Chunk<TCell>> _chunks = new();

    public ChunkShape Shape { get; }
    public int LoadedChunkCount => _chunks.Count;
    public IEnumerable<Chunk<TCell>> Chunks => _chunks.Values;

    public event Action<ChunkCoord>? ChunkLoaded;
    public event Action<ChunkCoord>? ChunkUnloaded;
    public event Action<ChunkCoord>? ChunkChanged;
    /// <summary>Una celda cambió (SetCell): celda, valor anterior y valor nuevo. Permite saber QUÉ cambió, no solo en qué chunk.</summary>
    public event Action<CellCoord, TCell, TCell>? CellChanged;

    public World(ChunkShape shape) => Shape = shape;

    // Caché del último chunk consultado: la física, los raycasts y el mallado piden celdas contiguas, casi siempre del mismo chunk.
    private ChunkCoord _lastCoord;
    private Chunk<TCell>? _lastChunk;

    public Chunk<TCell>? GetChunk(ChunkCoord c)
    {
        if (_lastChunk != null && c == _lastCoord) return _lastChunk;
        if (!_chunks.TryGetValue(c, out var chunk)) return null;
        _lastCoord = c; _lastChunk = chunk;
        return chunk;
    }

    public TCell GetCell(CellCoord c)
    {
        var cc = Shape.ToChunk(c, out int lx, out int ly, out int lz);
        var chunk = GetChunk(cc);
        return chunk != null ? chunk[lx, ly, lz] : default;
    }

    public bool SetCell(CellCoord c, TCell value)
    {
        var cc = Shape.ToChunk(c, out int lx, out int ly, out int lz);
        var chunk = GetChunk(cc);
        if (chunk == null) return false;

        int index = Shape.Index(lx, ly, lz);
        var previous = chunk.Cells[index];
        chunk[lx, ly, lz] = value;
        chunk.IsModified = true;
        // Un bloque distinto es una instancia nueva: pierde los cambios de la anterior.
        if (chunk.CellProperties != null && !EqualityComparer<TCell>.Default.Equals(previous, value))
        {
            chunk.CellProperties.Remove(index);
            if (chunk.CellProperties.Count == 0) chunk.CellProperties = null;
        }
        ChunkChanged?.Invoke(cc);

        if (lx == 0) ChunkChanged?.Invoke(cc with { X = cc.X - 1 });
        if (lx == Shape.SizeX - 1) ChunkChanged?.Invoke(cc with { X = cc.X + 1 });
        if (ly == 0) ChunkChanged?.Invoke(cc with { Y = cc.Y - 1 });
        if (ly == Shape.SizeY - 1) ChunkChanged?.Invoke(cc with { Y = cc.Y + 1 });
        if (lz == 0) ChunkChanged?.Invoke(cc with { Z = cc.Z - 1 });
        if (lz == Shape.SizeZ - 1) ChunkChanged?.Invoke(cc with { Z = cc.Z + 1 });
        CellChanged?.Invoke(c, previous, value);
        return true;
    }

    /// <summary>Cambios de instancia de una celda (null si no tiene: comparte los valores del prototipo de su bloque).</summary>
    public PropertyOverrides? GetCellOverrides(CellCoord c)
    {
        var cc = Shape.ToChunk(c, out int lx, out int ly, out int lz);
        if (!_chunks.TryGetValue(cc, out var chunk) || chunk.CellProperties == null) return null;
        return chunk.CellProperties.TryGetValue(Shape.Index(lx, ly, lz), out var overrides) ? overrides : null;
    }

    /// <summary>Guarda (o, con null/vacío, borra) los cambios de instancia de una celda y marca el chunk como modificado.</summary>
    public bool SetCellOverrides(CellCoord c, PropertyOverrides? overrides)
    {
        var cc = Shape.ToChunk(c, out int lx, out int ly, out int lz);
        var chunk = GetChunk(cc);
        if (chunk == null) return false;
        int index = Shape.Index(lx, ly, lz);

        if (overrides == null || overrides.Count == 0)
        {
            if (chunk.CellProperties != null && chunk.CellProperties.Remove(index))
            {
                if (chunk.CellProperties.Count == 0) chunk.CellProperties = null;
                chunk.IsModified = true;
            }
            return true;
        }
        (chunk.CellProperties ??= new Dictionary<int, PropertyOverrides>())[index] = overrides;
        chunk.IsModified = true;
        return true;
    }

    /// <summary>Total de celdas del mundo cargado que guardan algún cambio de instancia.</summary>
    public int CellOverrideCount
    {
        get
        {
            int n = 0;
            foreach (var chunk in _chunks.Values) n += chunk.CellProperties?.Count ?? 0;
            return n;
        }
    }

    public void AddChunk(Chunk<TCell> chunk)
    {
        _chunks[chunk.Coord] = chunk;
        _lastChunk = null;
        ChunkLoaded?.Invoke(chunk.Coord);
    }

    public bool RemoveChunk(ChunkCoord c)
    {
        if (!_chunks.Remove(c)) return false;
        _lastChunk = null;
        ChunkUnloaded?.Invoke(c);
        return true;
    }
}
