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

    public World(ChunkShape shape) => Shape = shape;

    public Chunk<TCell>? GetChunk(ChunkCoord c) => _chunks.TryGetValue(c, out var chunk) ? chunk : null;

    public TCell GetCell(CellCoord c)
    {
        var cc = Shape.ToChunk(c, out int lx, out int ly, out int lz);
        return _chunks.TryGetValue(cc, out var chunk) ? chunk[lx, ly, lz] : default;
    }

    public bool SetCell(CellCoord c, TCell value)
    {
        var cc = Shape.ToChunk(c, out int lx, out int ly, out int lz);
        if (!_chunks.TryGetValue(cc, out var chunk)) return false;

        chunk[lx, ly, lz] = value;
        chunk.IsModified = true;
        ChunkChanged?.Invoke(cc);

        if (lx == 0) ChunkChanged?.Invoke(cc with { X = cc.X - 1 });
        if (lx == Shape.SizeX - 1) ChunkChanged?.Invoke(cc with { X = cc.X + 1 });
        if (ly == 0) ChunkChanged?.Invoke(cc with { Y = cc.Y - 1 });
        if (ly == Shape.SizeY - 1) ChunkChanged?.Invoke(cc with { Y = cc.Y + 1 });
        if (lz == 0) ChunkChanged?.Invoke(cc with { Z = cc.Z - 1 });
        if (lz == Shape.SizeZ - 1) ChunkChanged?.Invoke(cc with { Z = cc.Z + 1 });
        return true;
    }

    public void AddChunk(Chunk<TCell> chunk)
    {
        _chunks[chunk.Coord] = chunk;
        ChunkLoaded?.Invoke(chunk.Coord);
    }

    public bool RemoveChunk(ChunkCoord c)
    {
        if (!_chunks.Remove(c)) return false;
        ChunkUnloaded?.Invoke(c);
        return true;
    }
}
