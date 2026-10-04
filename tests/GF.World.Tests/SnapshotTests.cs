using GF.Core;
using GF.World;
using Xunit;

namespace GF.World.Tests;

public class SnapshotTests
{
    private static Chunk<ushort> Filled(ChunkShape shape, ChunkCoord c, ushort v)
    {
        var chunk = new Chunk<ushort>(c, shape);
        Array.Fill(chunk.Cells, v);
        return chunk;
    }

    [Fact]
    public void Snapshot_ReadsNeighborBorders_AndCopiesOwnCells()
    {
        var shape = new ChunkShape(16, 16, 16);
        var world = new World<ushort>(shape);
        world.AddChunk(Filled(shape, new ChunkCoord(0, 0, 0), 1));
        world.AddChunk(Filled(shape, new ChunkCoord(1, 0, 0), 2));

        var snap = ChunkSnapshot<ushort>.Capture(world, world.GetChunk(new ChunkCoord(0, 0, 0))!);

        Assert.Equal(1, snap.Get(15, 5, 5));   // dentro
        Assert.Equal(2, snap.Get(16, 5, 5));   // vecino +X
        Assert.Equal(0, snap.Get(-1, 5, 5));   // vecino -X no cargado
        Assert.Equal(0, snap.Get(5, 16, 5));   // vecino +Y no cargado

        world.SetCell(new CellCoord(3, 3, 3), 9);
        Assert.Equal(1, snap.Get(3, 3, 3));    // el snapshot no ve ediciones posteriores
    }
}
