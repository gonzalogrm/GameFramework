using GF.Core;
using GF.World;
using Xunit;

namespace GF.World.Tests;

public class AlgorithmTests
{
    private static World<byte> OpenWorld()
    {
        var world = new World<byte>(new ChunkShape(32, 32, 1));
        var chunk = new Chunk<byte>(new ChunkCoord(0, 0, 0), world.Shape);
        Array.Fill(chunk.Cells, (byte)1);   // 1 = suelo, 0 = muro (y todo lo no cargado)
        world.AddChunk(chunk);
        return world;
    }

    [Fact]
    public void CaveStage_IsSeamlessAcrossChunkBoundaries()
    {
        var gen = new WorldGenerator<byte>(99).AddStage(new CellularAutomataCaveStage<byte>(1, 0));
        var big = new Chunk<byte>(new ChunkCoord(0, 0, 0), new ChunkShape(64, 64, 1));
        gen.Generate(big);

        var small = new ChunkShape(32, 32, 1);
        for (int cx = 0; cx < 2; cx++)
        for (int cy = 0; cy < 2; cy++)
        {
            var c = new Chunk<byte>(new ChunkCoord(cx, cy, 0), small);
            gen.Generate(c);
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
                Assert.Equal(big[cx * 32 + x, cy * 32 + y, 0], c[x, y, 0]);
        }
    }

    /*
    [Fact]
    public void Fov_WallBlocksCellsBehindIt()
    {
        var world = OpenWorld();
        world.SetCell(new CellCoord(5, 10, 0), 0);
        var seen = new HashSet<CellCoord>();
        Fov.Compute(world, new CellCoord(2, 10, 0), 10, c => c == 0, seen.Add);

        Assert.Contains(new CellCoord(5, 10, 0), seen);       // el muro se ve
        Assert.DoesNotContain(new CellCoord(8, 10, 0), seen); // lo de detrás no
        Assert.Contains(new CellCoord(2, 15, 0), seen);       // campo abierto sí
    }
    */

    [Fact]
    public void AStar_FindsStraightPathInOpenSpace()
    {
        var path = AStar.FindPath(OpenWorld(), new CellCoord(2, 2, 0), new CellCoord(7, 2, 0), c => c != 0);
        Assert.NotNull(path);
        Assert.Equal(6, path!.Count);
        Assert.Equal(new CellCoord(7, 2, 0), path[^1]);
    }

    [Fact]
    public void AStar_GoesAroundWallsAndFailsWhenEnclosed()
    {
        var world = OpenWorld();
        for (int y = 0; y < 20; y++) world.SetCell(new CellCoord(5, y, 0), 0);
        var around = AStar.FindPath(world, new CellCoord(3, 5, 0), new CellCoord(7, 5, 0), c => c != 0);
        Assert.NotNull(around);
        Assert.All(around!, c => Assert.NotEqual(0, world.GetCell(c)));

        for (int y = 0; y < 32; y++) world.SetCell(new CellCoord(5, y, 0), 0);
        Assert.Null(AStar.FindPath(world, new CellCoord(3, 5, 0), new CellCoord(7, 5, 0), c => c != 0));
    }
}
