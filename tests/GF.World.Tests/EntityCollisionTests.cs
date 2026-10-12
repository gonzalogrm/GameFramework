using GF.Core;
using GF.World;
using GF.World.Entities;
using GF.World.Map;
using Xunit;

namespace GF.World.Tests;

public class EntityCollisionTests
{
    private static readonly WorldScale Scale = new(4, 3, 2, 2, 16, 16);
    private static readonly ChunkShape Shape = new(16, 16, 16);

    private static EntityStore Setup()
    {
        var store = new EntityStore(Shape, Scale);
        var world = new World<byte>(Shape);
        store.Attach(world);
        world.AddChunk(new Chunk<byte>(new ChunkCoord(0, 0, 0), Shape));   // la columna de las entidades está cargada: Active
        return store;
    }

    [Fact]
    public void AnOverlappingBody_IsPushedOutAlongTheShortestAxis()
    {
        var store = Setup();
        var other = store.Spawn(0, new Vec3d(8, 0, 8), 1);

        var result = EntityCollision.PushOut(store, Scale, new Vec3d(8.3, 0, 8.0), 0.6, 1.8, null, new List<Entity>(), out bool onTop);

        Assert.True(result.X - other.Position.X >= 0.6 - 1e-6);   // ya no se solapan en X (0,6 = media anchura de cada uno)
        Assert.Equal(8.0, result.Z, 6);                           // en Z no se movió
        Assert.False(onTop);
    }

    [Fact]
    public void LandingOnTopOfABody_LeavesYouStandingOnIt()
    {
        var store = Setup();
        store.Spawn(0, new Vec3d(8, 0, 8), 1);   // 1,8 de alto

        var result = EntityCollision.PushOut(store, Scale, new Vec3d(8.1, 1.7, 8.0), 0.6, 1.8, null, new List<Entity>(), out bool onTop);

        Assert.True(onTop);
        Assert.True(result.Y >= 1.8);
        Assert.Equal(8.1, result.X, 6);   // sin empujón lateral
    }

    [Fact]
    public void ABodyIgnoresItself_AndBodiesThatDoNotOverlap()
    {
        var store = Setup();
        var a = store.Spawn(0, new Vec3d(8, 0, 8), 1);
        store.Spawn(0, new Vec3d(12, 0, 8), 1);

        Assert.Equal(a.Position, EntityCollision.PushOut(store, Scale, a.Position, 0.6, 1.8, a, new List<Entity>(), out _));
        Assert.Equal(new Vec3d(10, 0, 8), EntityCollision.PushOut(store, Scale, new Vec3d(10, 0, 8), 0.6, 1.8, null, new List<Entity>(), out _));
    }

    [Fact]
    public void ThePlayerAsAnObstacle_PushesAnNpcAway()
    {
        var store = Setup();
        var result = EntityCollision.PushOut(store, Scale, new Vec3d(8.2, 0, 8), 0.6, 1.8, null, new List<Entity>(), out _,
            obstacle: new Vec3d(8, 0, 8));
        Assert.True(Math.Abs(result.X - 8) >= 0.6 - 1e-6);
    }
}
