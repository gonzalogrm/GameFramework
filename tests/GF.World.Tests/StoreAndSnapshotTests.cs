using GF.Core;
using GF.World;
using Xunit;

namespace GF.World.Tests;

public class StoreAndSnapshotTests
{
    private static readonly ChunkShape Shape = new(16, 16, 16);

    private static Chunk<ushort> Filled(ChunkCoord c, ushort v)
    {
        var chunk = new Chunk<ushort>(c, Shape);
        Array.Fill(chunk.Cells, v);
        return chunk;
    }

    [Fact]
    public void Snapshot_ReadsEdgeAndCornerNeighbors()
    {
        var world = new World<ushort>(Shape);
        world.AddChunk(Filled(new ChunkCoord(0, 0, 0), 1));
        world.AddChunk(Filled(new ChunkCoord(1, 1, 0), 3));      // vecino en arista
        world.AddChunk(Filled(new ChunkCoord(-1, -1, -1), 4));   // vecino en esquina

        var snap = ChunkSnapshot<ushort>.Capture(world, world.GetChunk(new ChunkCoord(0, 0, 0))!);
        Assert.Equal(3, snap.Get(16, 16, 5));
        Assert.Equal(4, snap.Get(-1, -1, -1));
        Assert.Equal(0, snap.Get(16, -1, 5));   // no cargado
    }

    [Fact]
    public void FileChunkStore_RoundTrips_AndRejectsMismatches()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gf_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileChunkStore<ushort>(dir);
            var chunk = new Chunk<ushort>(new ChunkCoord(2, -1, 3), Shape);
            for (int i = 0; i < chunk.Cells.Length; i++) chunk.Cells[i] = (ushort)(i * 7);
            store.Save(chunk);

            var loaded = new Chunk<ushort>(chunk.Coord, Shape);
            Assert.True(store.TryLoad(loaded));
            Assert.Equal(chunk.Cells, loaded.Cells);

            Assert.False(store.TryLoad(new Chunk<ushort>(chunk.Coord, new ChunkShape(8, 8, 8))));   // forma distinta
            Assert.False(store.TryLoad(new Chunk<ushort>(new ChunkCoord(9, 9, 9), Shape)));          // sin datos
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void SetCell_MarksChunkModified()
    {
        var world = new World<ushort>(Shape);
        world.AddChunk(Filled(new ChunkCoord(0, 0, 0), 0));
        Assert.False(world.GetChunk(new ChunkCoord(0, 0, 0))!.IsModified);
        world.SetCell(new CellCoord(1, 1, 1), 5);
        Assert.True(world.GetChunk(new ChunkCoord(0, 0, 0))!.IsModified);
    }
}
