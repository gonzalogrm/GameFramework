using System.Text;
using GF.Core;
using GF.World;
using Xunit;

namespace GF.World.Tests;

public class PropertyTests
{
    private static readonly PropertyId Hp = PropertyIds.Of("hp");

    private static Prototype Creature() =>
        new Prototype("criatura").Text("name", "").Float("hp", 10f).Float("max_hp", 10f).Int("level", 1).Bool("hostile", false);

    [Fact]
    public void UnmodifiedInstance_ReadsThePrototype_AndStoresNothing()
    {
        PropertyOverrides? overrides = null;
        var proto = Creature();
        Assert.Equal(10f, PropertyOps.Get(proto, overrides, Hp).AsFloat);
        Assert.Equal("", PropertyOps.Get(proto, overrides, PropertyIds.Of("name")).AsText);
        Assert.Null(overrides);
    }

    [Fact]
    public void Set_StoresOnlyTheChange_AndReturningToTheDefaultReleasesIt()
    {
        PropertyOverrides? overrides = null;
        var proto = Creature();

        PropertyOps.Set(proto, ref overrides, Hp, PropValue.Of(4f));
        Assert.Equal(1, overrides!.Count);
        Assert.Equal(4f, PropertyOps.Get(proto, overrides, Hp).AsFloat);
        Assert.Equal(10f, PropertyOps.Get(proto, overrides, PropertyIds.Of("max_hp")).AsFloat);   // lo demás sigue en el prototipo

        PropertyOps.Set(proto, ref overrides, Hp, PropValue.Of(10f));
        Assert.Null(overrides);   // vuelve al valor por defecto: no queda nada guardado
    }

    [Fact]
    public void DerivedPrototypes_InheritAndOverrideDefaults()
    {
        var parent = Creature();
        var dog = parent.Derive("perro").Text("species", "Perro").Float("hp", 12f);

        Assert.Equal(12f, PropertyOps.Get(dog, null, Hp).AsFloat);                         // valor redefinido
        Assert.Equal(10f, PropertyOps.Get(dog, null, PropertyIds.Of("max_hp")).AsFloat);   // heredado
        Assert.Equal("Perro", PropertyOps.Get(dog, null, PropertyIds.Of("species")).AsText);
        Assert.Equal("perro > criatura", dog.ChainName);
        Assert.True(dog.Is(parent));
        Assert.False(parent.Is(dog));
        Assert.Throws<InvalidOperationException>(() => dog.Text("hp", "x"));   // no se puede cambiar el tipo de una propiedad heredada
    }

    [Fact]
    public void UnknownProperties_AndWrongKinds_AreRejected_ButNumbersAreCoerced()
    {
        PropertyOverrides? overrides = null;
        var proto = Creature();

        Assert.Throws<KeyNotFoundException>(() => PropertyOps.Get(proto, null, PropertyIds.Of("no_existe")));
        Assert.Throws<InvalidOperationException>(() => PropertyOps.Set(proto, ref overrides, Hp, PropValue.Of("mucha")));

        PropertyOps.Set(proto, ref overrides, PropertyIds.Of("level"), PropValue.Of(3.6f));   // decimal a entero
        Assert.Equal(PropKind.Int, PropertyOps.Get(proto, overrides, PropertyIds.Of("level")).Kind);
        Assert.Equal(4, PropertyOps.Get(proto, overrides, PropertyIds.Of("level")).AsInt);
    }

    [Fact]
    public void Enumerate_ListsParentsFirst_AndFlagsChangedValues()
    {
        PropertyOverrides? overrides = null;
        var dog = Creature().Derive("perro").Float("loyalty", 0.5f);
        PropertyOps.Set(dog, ref overrides, Hp, PropValue.Of(3f));

        var views = PropertyOps.Enumerate(dog, overrides).ToList();
        Assert.Equal(new[] { "name", "hp", "max_hp", "level", "hostile", "loyalty" }, views.Select(v => v.Name));
        Assert.Equal(new[] { "hp" }, views.Where(v => v.Overridden).Select(v => v.Name));
        var hp = views.Single(v => v.Name == "hp");
        Assert.Equal(3f, hp.Value.AsFloat);
        Assert.Equal(10f, hp.Default.AsFloat);
    }

    [Fact]
    public void Serializer_RoundTripsCellMaps_ByPropertyName()
    {
        var overrides = new PropertyOverrides();
        overrides.Set(Hp, PropValue.Of(4.5f));
        overrides.Set(PropertyIds.Of("name"), PropValue.Of("Rex"));
        overrides.Set(PropertyIds.Of("hostile"), PropValue.Of(true));
        var map = new Dictionary<int, PropertyOverrides> { [7] = overrides };

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) PropertySerializer.WriteCellMap(w, map);
        ms.Position = 0;
        using var r = new BinaryReader(ms);
        var back = PropertySerializer.ReadCellMap(r);

        Assert.Single(back);
        var o = back[7];
        Assert.Equal(3, o.Count);
        Assert.Equal(4.5f, o.TryGet(Hp, out var hp) ? hp.AsFloat : -1f);
        Assert.Equal("Rex", o.TryGet(PropertyIds.Of("name"), out var name) ? name.AsText : "");
        Assert.True(o.TryGet(PropertyIds.Of("hostile"), out var hostile) && hostile.AsBool);
    }
}

public sealed class PropertyPersistenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gf_props_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void RegionStore_PersistsCellProperties_AndStillReadsChunksWithoutThem()
    {
        var shape = new ChunkShape(16, 16, 16);
        var plain = new Chunk<ushort>(new ChunkCoord(0, 0, 0), shape);
        var withProps = new Chunk<ushort>(new ChunkCoord(1, 0, 0), shape);
        Array.Fill(withProps.Cells, (ushort)3);

        var overrides = new PropertyOverrides();
        overrides.Set(PropertyIds.Of("durability"), PropValue.Of(1f));
        withProps.CellProperties = new Dictionary<int, PropertyOverrides> { [shape.Index(2, 3, 4)] = overrides };

        using (var store = new RegionChunkStore<ushort>(_dir))
        {
            store.Save(plain);
            store.Save(withProps);
        }
        using (var store = new RegionChunkStore<ushort>(_dir))
        {
            var loadedPlain = new Chunk<ushort>(plain.Coord, shape);
            Assert.True(store.TryLoad(loadedPlain));
            Assert.Null(loadedPlain.CellProperties);

            var loaded = new Chunk<ushort>(withProps.Coord, shape);
            Assert.True(store.TryLoad(loaded));
            Assert.Equal(withProps.Cells, loaded.Cells);
            Assert.NotNull(loaded.CellProperties);
            Assert.True(loaded.CellProperties![shape.Index(2, 3, 4)].TryGet(PropertyIds.Of("durability"), out var v));
            Assert.Equal(1f, v.AsFloat);
        }
    }
}
