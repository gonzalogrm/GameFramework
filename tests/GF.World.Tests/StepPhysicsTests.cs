using GF.Core;
using GF.World;
using GF.World.Voxel;
using Microsoft.Xna.Framework;
using Xunit;

namespace GF.World.Tests;

public class StepPhysicsTests
{
    private static readonly Vector3 Body = new(0.6f, 1.8f, 0.6f);
    private static readonly Vec3d Start = new(5.5, 4.001, 5.5);   // de pie sobre un suelo cuya superficie está en y = 4

    private readonly BlockRegistry _blocks = new();
    private readonly World<ushort> _world = new(new ChunkShape(16, 16, 16));
    private readonly ushort _stone;

    public StepPhysicsTests()
    {
        _stone = _blocks.Register("stone", BlockDef.Cube("stone", 0));
        var chunk = new Chunk<ushort>(new ChunkCoord(0, 0, 0), _world.Shape);
        for (int y = 0; y < 4; y++)
        for (int z = 0; z < 16; z++)
        for (int x = 0; x < 16; x++)
            chunk[x, y, z] = _stone;
        _world.AddChunk(chunk);
    }

    private void Place(int x, int y, int z) => _world.SetCell(new CellCoord(x, y, z), _stone);

    private Vec3d Walk(double dx, double stepHeight, out CollisionFlags flags) =>
        VoxelPhysics.MoveAndCollide(_world, _blocks, Start, Body, new Vec3d(dx, 0, 0), out flags, stepHeight);

    [Fact]
    public void WithoutStepHeight_AOneBlockLedgeStopsYou()
    {
        Place(6, 4, 5);
        var p = Walk(0.6, 0, out var flags);
        Assert.True((flags & CollisionFlags.X) != 0);
        Assert.Equal(Start.Y, p.Y, 6);
        Assert.True(p.X < 5.75);
    }

    [Fact]
    public void WithStepHeight_YouClimbAOneBlockLedgeAndKeepWalking()
    {
        Place(6, 4, 5);
        var p = Walk(0.6, 1.0, out var flags);
        Assert.Equal(6.1, p.X, 6);                       // avanzó todo lo que pedía
        Assert.Equal(5.001, p.Y, 6);                     // y quedó sobre el bloque
        Assert.True((flags & CollisionFlags.Ground) != 0);
        Assert.Equal(0, (int)(flags & (CollisionFlags.X | CollisionFlags.Z)));
    }

    [Fact]
    public void ATwoBlockWall_IsNotClimbed()
    {
        Place(6, 4, 5);
        Place(6, 5, 5);
        var p = Walk(0.6, 1.0, out var flags);
        Assert.True((flags & CollisionFlags.X) != 0);
        Assert.Equal(Start.Y, p.Y, 6);
    }

    [Fact]
    public void ALowCeiling_PreventsTheStep()
    {
        Place(6, 4, 5);
        Place(5, 6, 5);   // justo encima de la cabeza una vez subido el escalón
        var p = Walk(0.6, 1.0, out var flags);
        Assert.True((flags & CollisionFlags.X) != 0);
        Assert.Equal(Start.Y, p.Y, 6);
    }

    [Fact]
    public void FreeWalking_IsUnaffectedByStepHeight()
    {
        var a = Walk(0.3, 0, out var fa);
        var b = Walk(0.3, 1.0, out var fb);
        Assert.Equal(a, b);
        Assert.Equal(fa, fb);
    }
}
