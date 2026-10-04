using System.Diagnostics;
using GF.Core;
using GF.World;
using Xunit;

namespace GF.World.Tests;

public class ChunkManagerTests
{
    private sealed class MarkStage : IGenerationStage<byte>
    {
        public void Apply(Chunk<byte> chunk, GenerationContext ctx) => chunk.Cells[0] = 1;
    }

    private static (World<byte> World, ChunkManager<byte> Manager) Setup(int rx, int ry, int rz)
    {
        var world = new World<byte>(new ChunkShape(8, 8, 8));
        var gen = new WorldGenerator<byte>(1).AddStage(new MarkStage());
        var mgr = new ChunkManager<byte>(world, gen) { RadiusX = rx, RadiusY = ry, RadiusZ = rz, MaxConcurrentJobs = 4 };
        return (world, mgr);
    }

    private static void PumpUntil(ChunkManager<byte> mgr, CellCoord center, Func<bool> done, int timeoutMs = 10_000)
    {
        var sw = Stopwatch.StartNew();
        while (!done() && sw.ElapsedMilliseconds < timeoutMs)
        {
            mgr.Update(center);
            Thread.Sleep(1);
        }
    }

    [Fact]
    public void LoadsEveryChunkInTheRadius()
    {
        var (world, mgr) = Setup(2, 1, 2);   // 5 x 3 x 5
        PumpUntil(mgr, new CellCoord(0, 0, 0), () => world.LoadedChunkCount == 75);
        Assert.Equal(75, world.LoadedChunkCount);
        Assert.Equal(75, mgr.GeneratedChunks);
        Assert.True(mgr.AverageGenerationMs >= 0);
    }

    [Fact]
    public void MovingTheCenter_UnloadsFarChunksAndLoadsNewOnes()
    {
        var (world, mgr) = Setup(2, 0, 2);
        var origin = new CellCoord(0, 0, 0);
        PumpUntil(mgr, origin, () => world.LoadedChunkCount == 25);

        var far = new CellCoord(8 * 10, 0, 0);   // chunk (10, 0, 0)
        PumpUntil(mgr, far, () => world.GetChunk(new ChunkCoord(10, 0, 0)) != null && world.GetChunk(new ChunkCoord(0, 0, 0)) == null);
        Assert.NotNull(world.GetChunk(new ChunkCoord(10, 0, 0)));
        Assert.Null(world.GetChunk(new ChunkCoord(0, 0, 0)));
    }

    [Fact]
    public void RespectsVerticalAndPoleBounds()
    {
        var (world, mgr) = Setup(2, 3, 2);
        mgr.MinChunkY = 0; mgr.MaxChunkY = 0;      // un solo nivel (roguelike 2D)
        mgr.MinChunkZ = 0; mgr.MaxChunkZ = 100;    // el centro está en el borde: solo la mitad del cuadrado
        PumpUntil(mgr, new CellCoord(0, 0, 0), () => world.LoadedChunkCount == 15);   // x: -2..2, z: 0..2
        Assert.Equal(15, world.LoadedChunkCount);
        Assert.All(world.Chunks, c => { Assert.Equal(0, c.Coord.Y); Assert.InRange(c.Coord.Z, 0, 2); });
    }

    [Fact]
    public void AreNeighborsSettled_WaitsForDesiredNeighbors_ButIgnoresThoseOutsideTheRadius()
    {
        var (world, mgr) = Setup(2, 0, 2);
        var origin = new CellCoord(0, 0, 0);

        mgr.Update(origin);   // fija el centro y lanza la generación; aún no se ha incorporado nada
        Assert.False(mgr.AreNeighborsSettled(new ChunkCoord(0, 0, 0)));

        PumpUntil(mgr, origin, () => world.LoadedChunkCount == 25);
        Assert.True(mgr.AreNeighborsSettled(new ChunkCoord(0, 0, 0)));
        Assert.True(mgr.AreNeighborsSettled(new ChunkCoord(2, 0, 2)));    // sus vecinos de fuera del radio nunca llegarán
    }

    [Fact]
    public void BacklogLimit_DoesNotStallTheLoading()
    {
        var (world, mgr) = Setup(3, 0, 3);
        mgr.MaxBacklog = 2;
        mgr.ApplyBudgetMs = 0.0001;   // se incorpora un chunk por llamada
        PumpUntil(mgr, new CellCoord(0, 0, 0), () => world.LoadedChunkCount == 49);
        Assert.Equal(49, world.LoadedChunkCount);
    }
}
