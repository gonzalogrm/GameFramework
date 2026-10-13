using GF.Core;
using GF.World;
using GF.World.Entities;
using GF.World.Items;
using GF.World.Map;
using Xunit;

namespace GF.World.Tests;

public class EntityDefinitionTests
{
    private const string Json = """
        // comentarios permitidos
        { "types": [
            { "name": "creature", "abstract": true, "title": "criatura",
              "properties": { "hp": 10.0, "level": 1, "name": "" } },
            { "name": "zombie", "parent": "creature", "title": "zombi", "speed": 3.0, "size": [0.6, 1.8], "color": [70, 150, 70],
              "inventorySize": 4,
              "properties": { "hp": 12.0 },
              "inventory": [ { "item": "bone", "count": 2 } ],
              "variants": [
                { "name": "plain", "weight": 3 },
                { "name": "guard", "weight": 1, "properties": { "hp": 20.0 }, "equipment": { "Head": "cap" },
                  "inventory": [ { "item": "bone", "count": 5 }, { "item": "ghost", "count": 1 } ] } ] }
        ] }
        """;

    private static readonly Registry<ItemDef> Items = new();
    private static readonly ushort Bone, Cap;

    static EntityDefinitionTests()
    {
        Bone = Items.Register("bone", new ItemDef("bone", "Hueso", 4));
        Cap = Items.Register("cap", new ItemDef("cap", "Gorro", 1, EquipSlot.Head, Defense: 1f));
    }

    private static (EntityTypeSet Set, EntityStore Store, List<string> Messages) Load()
    {
        var messages = new List<string>();
        var set = EntityDefinitionLoader.Load(Json, Items, messages);
        var store = new EntityStore(new ChunkShape(16, 16, 16), new WorldScale(4, 3, 2, 2, 16, 16)) { Definitions = set.Types, Items = Items };
        return (set, store, messages);
    }

    [Fact]
    public void TheLoader_BuildsInheritedPrototypes_AndVariantsAsChildPrototypes()
    {
        var (set, _, messages) = Load();
        var def = set.Types.Get(set.Types.IdOf("zombie"));

        Assert.Equal(1, set.Types.Count);   // creature es abstracto
        Assert.Equal(2, def.Variants!.Count);
        Assert.Equal("zombi/guard > zombi > criatura", def.Variants[1].Prototype.ChainName);

        var hp = PropertyIds.Of("hp");
        Assert.Equal(12f, PropertyOps.Get(def.Variants[0].Prototype, null, hp).AsFloat);   // hereda el valor del tipo
        Assert.Equal(20f, PropertyOps.Get(def.Variants[1].Prototype, null, hp).AsFloat);   // la variante lo redefine
        Assert.Equal(PropKind.Int, PropertyOps.Get(def.Variants[0].Prototype, null, PropertyIds.Of("level")).Kind);   // 1 = entero
        Assert.Equal(PropKind.Float, PropertyOps.Get(def.Variants[0].Prototype, null, hp).Kind);                         // 10.0 = decimal
        Assert.Single(messages);   // el objeto "ghost" no existe: aviso, no error
        Assert.Contains("ghost", messages[0]);
    }

    [Fact]
    public void AnInstanceWithoutChanges_SavesOnlyItsVariant()
    {
        var (set, store, _) = Load();
        var id = set.Types.IdOf("zombie");
        var e = store.Spawn(id, new Vec3d(10.123456, 4, 20), 3.0, null, variant: 1);

        _ = e.Inventory;   // consultar el inventario lo crea, pero igual que la plantilla: no cuenta como cambio
        _ = e.Equipment;
        Assert.Equal(5, e.Inventory.CountOf(Bone));
        Assert.Equal(Cap, e.Equipment.Get(EquipSlot.Head));

        var record = store.Export(set.Types).Single();
        Assert.Equal("guard", record.Variant);
        Assert.Null(record.Speed);   // = la del tipo
        Assert.Null(record.Props);
        Assert.Null(record.Items);
        Assert.Null(record.Gear);
        Assert.Equal(10.12, record.X);   // posición con 2 decimales
    }

    [Fact]
    public void OnlyWhatChanges_IsSaved_AndComingBackToTheTemplateDropsIt()
    {
        var (set, store, _) = Load();
        var e = store.Spawn(set.Types.IdOf("zombie"), new Vec3d(10, 4, 20), 3.0, null, variant: 1);

        e.Inventory.Remove(Bone, 5);   // se le quita todo: hay que guardar que está vacío
        var r1 = store.Export(set.Types).Single();
        Assert.NotNull(r1.Items);
        Assert.Empty(r1.Items!);
        Assert.Null(r1.Gear);          // el equipo no ha cambiado

        e.Inventory.Add(Items, Bone, 5);   // vuelve a ser como la plantilla
        Assert.Null(store.Export(set.Types).Single().Items);

        e.Equipment.Set(EquipSlot.Head, null);
        var r2 = store.Export(set.Types).Single();
        Assert.NotNull(r2.Gear);
        Assert.Empty(r2.Gear!);
    }

    [Fact]
    public void LoadingASave_RestoresTheVariantAndItsLoadout_WithoutThemBeingSaved()
    {
        var (set, store, _) = Load();
        store.Spawn(set.Types.IdOf("zombie"), new Vec3d(10, 4, 20), 3.0, null, variant: 1);
        var records = store.Export(set.Types);

        var (_, store2, _) = Load();
        store2.Import(records, set.Types, 0);
        var e = store2.All.Single();

        Assert.Equal(1, e.Variant);
        Assert.Equal("zombi/guard > zombi > criatura", e.Prototype!.ChainName);
        Assert.Equal(20f, e.GetFloat("hp"));
        Assert.Equal(5, e.Inventory.CountOf(Bone));
        Assert.Equal(Cap, e.Equipment.Get(EquipSlot.Head));
        Assert.Equal(3.0, e.Speed);
    }

    [Fact]
    public void AnInvalidFile_OrCircularInheritance_ThrowsWithAReason()
    {
        Assert.Throws<InvalidDataException>(() => EntityDefinitionLoader.Load("{ no es json", Items, new List<string>()));
        Assert.Throws<InvalidDataException>(() => EntityDefinitionLoader.Load(
            """{ "types": [ { "name": "a", "parent": "b" }, { "name": "b", "parent": "a" } ] }""", Items, new List<string>()));
    }
}
