using GF.Core;
using GF.World;
using GF.World.Entities;
using GF.World.Map;
using Xunit;

namespace GF.World.Tests;

public class EntityPropertyTests
{
    private static readonly Prototype DogProto =
        new Prototype("perro").Text("name", "").Text("species", "Perro").Float("hp", 12f).Float("max_hp", 12f);

    private static (EntityStore Store, Registry<EntityDef> Defs) Setup()
    {
        var defs = new Registry<EntityDef>();
        defs.Register("dog", new EntityDef("dog", 4, 150, 100, 60, 0.6f, 0.9f, DogProto));
        var store = new EntityStore(new ChunkShape(16, 16, 16), new WorldScale(4, 3, 2, 2, 16, 16)) { Definitions = defs };
        return (store, defs);
    }

    [Fact]
    public void ThousandInstances_ShareThePrototype_AndOnlyTheChangedOnesStoreAnything()
    {
        var (store, _) = Setup();
        var dogs = Enumerable.Range(0, 1000)
            .Select(i => store.Spawn(0, new Vec3d(i % 120, 1, 10 + i % 80), 4))
            .ToList();

        dogs[10].Set("hp", 5f);
        dogs[20].Set("name", "Rex");

        Assert.Equal(2, store.CountWithOverrides());
        Assert.All(dogs, d => Assert.Same(DogProto, d.Prototype));   // todas remiten al MISMO prototipo
        Assert.Equal(0, dogs[500].OverrideCount);
        Assert.Equal(12f, dogs[500].GetFloat("hp"));
        Assert.Equal(5f, dogs[10].GetFloat("hp"));
        Assert.Equal(12f, dogs[10].GetFloat("max_hp"));   // lo no cambiado se lee del prototipo
        Assert.Equal(1, dogs[10].OverrideCount);

        dogs[10].Set("hp", 12f);   // vuelve a su valor normal: deja de ocupar memoria
        Assert.Equal(0, dogs[10].OverrideCount);
        Assert.Equal(1, store.CountWithOverrides());
    }

    [Fact]
    public void Properties_ReportWhichOnesWereChanged()
    {
        var (store, _) = Setup();
        var dog = store.Spawn(0, new Vec3d(5, 1, 5), 4);
        dog.Set("hp", 7f);

        var view = dog.Properties.ToDictionary(p => p.Name);
        Assert.True(view["hp"].Overridden);
        Assert.Equal(12f, view["hp"].Default.AsFloat);
        Assert.False(view["species"].Overridden);
        Assert.Equal("Perro", view["species"].Value.AsText);
    }

    [Fact]
    public void WithoutDefinitions_EntitiesWorkButHaveNoProperties()
    {
        var store = new EntityStore(new ChunkShape(16, 16, 16), new WorldScale(4, 3, 2, 2, 16, 16));
        var e = store.Spawn(0, new Vec3d(5, 1, 5), 1);
        Assert.Null(e.Prototype);
        Assert.Empty(e.Properties);
        Assert.Throws<InvalidOperationException>(() => e.GetFloat("hp"));
    }

    [Fact]
    public void ExportImport_SavesOnlyTheChanges()
    {
        var (store, defs) = Setup();
        var plain = store.Spawn(0, new Vec3d(10, 1, 10), 4);
        var hurt = store.Spawn(0, new Vec3d(20, 1, 10), 4);
        hurt.Set("hp", 5f);
        hurt.Set("name", "Rex");

        var records = store.Export(defs);
        Assert.Null(records.Single(r => r.Id == plain.Id).Props);
        Assert.Equal(2, records.Single(r => r.Id == hurt.Id).Props!.Count);

        var (store2, _) = Setup();
        store2.Definitions = null;   // Import debe asignarlas por sí mismo
        store2.Import(records, defs, 1.0);
        var restored = store2.All.Single(e => e.Id == hurt.Id);
        Assert.Equal(5f, restored.GetFloat("hp"));
        Assert.Equal("Rex", restored.GetText("name"));
        Assert.Equal(12f, restored.GetFloat("max_hp"));
        Assert.Equal(0, store2.All.Single(e => e.Id == plain.Id).OverrideCount);
    }

    [Fact]
    public void Raycast_HitsTheNearestEntityAlongTheRay()
    {
        var (store, _) = Setup();
        var near = store.Spawn(0, new Vec3d(10, 0, 5), 4);
        store.Spawn(0, new Vec3d(14, 0, 5), 4);
        store.Spawn(0, new Vec3d(10, 0, 20), 4);   // fuera del rayo

        var hit = store.Raycast(new Vec3d(5, 0.5, 5), new Vec3d(1, 0, 0), 20, out double distance);

        Assert.Same(near, hit);
        Assert.Equal(4.7, distance, 3);   // la caja de 0,6 de ancho empieza en x = 9,7
        Assert.Null(store.Raycast(new Vec3d(5, 0.5, 5), new Vec3d(-1, 0, 0), 20, out _));   // mirando al otro lado
        Assert.Null(store.Raycast(new Vec3d(5, 0.5, 5), new Vec3d(1, 0, 0), 3, out _));     // demasiado lejos
    }
}
