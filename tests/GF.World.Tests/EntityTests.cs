using GF.Core;
using GF.World;
using GF.World.Entities;
using GF.World.Map;
using Xunit;

namespace GF.World.Tests;

public class EntityTests
{
    private static readonly WorldScale Scale = new(4, 3, 2, 2, 16, 16);   // 128 x 96 bloques, regiones de 32
    private static readonly ChunkShape Shape = new(16, 16, 16);

    private static (EntityStore Store, World<byte> World) Setup()
    {
        var store = new EntityStore(Shape, Scale);
        var world = new World<byte>(Shape);
        store.Attach(world);
        return (store, world);
    }

    private static Chunk<byte> MakeChunk(int x, int y, int z) => new(new ChunkCoord(x, y, z), Shape);

    [Fact]
    public void Entity_IsActiveOnlyWhileItsChunkColumnIsLoaded()
    {
        var (store, world) = Setup();
        int activated = 0, deactivated = 0;
        store.Activated += _ => activated++;
        store.Deactivated += _ => deactivated++;

        var e = store.Spawn(0, new Vec3d(5, 3, 5), 1);
        Assert.Equal(SimulationTier.Dormant, e.Tier);

        world.AddChunk(MakeChunk(0, 0, 0));
        Assert.Equal(SimulationTier.Active, e.Tier);
        Assert.Contains(e, store.Active);

        world.RemoveChunk(new ChunkCoord(0, 0, 0));
        Assert.Equal(SimulationTier.Dormant, e.Tier);
        Assert.Equal(1, activated);
        Assert.Equal(1, deactivated);
    }

    [Fact]
    public void Move_IntoLoadedColumn_ActivatesAndReindexes()
    {
        var (store, world) = Setup();
        world.AddChunk(MakeChunk(1, 0, 0));
        var e = store.Spawn(0, new Vec3d(5, 0, 5), 1);
        Assert.Equal(SimulationTier.Dormant, e.Tier);
        Assert.Contains(e, store.InRegion(0, 0));

        store.Move(e, new Vec3d(40, 0, 5));   // región 1, columna 2 (no cargada)
        Assert.Contains(e, store.InRegion(1, 0));
        Assert.DoesNotContain(e, store.InRegion(0, 0));

        store.Move(e, new Vec3d(20, 0, 5));   // columna 1 (cargada)
        Assert.Equal(SimulationTier.Active, e.Tier);
    }

    [Fact]
    public void Move_WrapsXAround()
    {
        var (store, _) = Setup();
        var e = store.Spawn(0, new Vec3d(5, 0, 5), 1);
        store.Move(e, new Vec3d(-3, 0, 5));
        Assert.Equal(125.0, e.Position.X, 3);
    }

    [Fact]
    public void ApproximateEntity_AdvancesTowardDestination_ThenArrives()
    {
        var (store, _) = Setup();
        bool arrived = false;
        store.Arrived += _ => arrived = true;

        var e = store.Spawn(0, new Vec3d(10, 0, 10), 2);
        store.SetDestination(e, new Vec3d(30, 0, 10));
        Assert.Equal(SimulationTier.Approximate, e.Tier);

        store.Update(5.0, new Vec3d(10, 0, 10));   // 5 s a 2 bloques/s -> 10 bloques
        Assert.Equal(20.0, e.Position.X, 3);
        Assert.False(arrived);

        store.Update(20.0, new Vec3d(10, 0, 10));
        Assert.True(arrived);
        Assert.Null(e.Destination);
        Assert.Equal(30.0, e.Position.X, 3);
        Assert.Equal(SimulationTier.Dormant, e.Tier);
    }

    [Fact]
    public void ChunkLoad_CatchesUpApproximateEntitiesBeforeActivating()
    {
        var (store, world) = Setup();
        store.NearRegionRadius = 0;
        store.MaxFarPerUpdate = 0;   // sin simulación periódica: solo la puesta al día al cargar

        var e = store.Spawn(0, new Vec3d(10, 0, 10), 0.5);
        store.SetDestination(e, new Vec3d(60, 0, 10));
        store.Update(10.0, new Vec3d(100, 0, 80));   // el foco está en otra región
        Assert.Equal(10.0, e.Position.X, 3);

        world.AddChunk(MakeChunk(0, 0, 0));
        Assert.Equal(15.0, e.Position.X, 3);   // 10 s * 0,5 bloques/s
        Assert.Equal(SimulationTier.Active, e.Tier);
    }

    [Fact]
    public void QueryRadius_WorksAcrossTheEastWestSeam()
    {
        var (store, _) = Setup();   // ancho del mundo = 128
        var west = store.Spawn(0, new Vec3d(2, 0, 40), 1);
        var east = store.Spawn(0, new Vec3d(126, 0, 40), 1);
        var far = store.Spawn(0, new Vec3d(60, 0, 40), 1);

        var found = new List<Entity>();
        store.QueryRadius(new Vec3d(0, 0, 40), 6, found);

        Assert.Contains(west, found);
        Assert.Contains(east, found);
        Assert.DoesNotContain(far, found);
        Assert.Equal(-2.0, Scale.DeltaX(0, 126), 6);
    }

    [Fact]
    public void ExportImport_RoundTrips()
    {
        var defs = new Registry<EntityDef>();
        defs.Register("walker", new EntityDef("walker", 1, 1, 2, 3));

        var (store, _) = Setup();
        var e = store.Spawn(0, new Vec3d(10, 5, 10), 2, "tag");
        store.SetDestination(e, new Vec3d(50, 5, 10));
        var records = store.Export(defs);

        var (store2, _) = Setup();
        store2.Import(records, defs, 42.0);

        Assert.Equal(1, store2.Count);
        Assert.Equal(42.0, store2.Time);
        var e2 = store2.All.Single();
        Assert.Equal(e.Position, e2.Position);
        Assert.Equal(e.Destination, e2.Destination);
        Assert.Equal("tag", e2.Tag);
        Assert.Equal(2.0, e2.Speed);
        Assert.Equal(SimulationTier.Approximate, e2.Tier);
    }
}
