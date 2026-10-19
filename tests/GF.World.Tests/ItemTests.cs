using GF.Core;
using GF.World;
using GF.World.Entities;
using GF.World.Items;
using GF.World.Map;
using Xunit;

namespace GF.World.Tests;

public class ItemTests
{
    private const string ItemsJson = """
        { "items": [
            { "name": "bone", "title": "Hueso", "category": "ingredient" },
            { "name": "apple", "title": "Manzana", "category": "food", "maxStack": 16, "use": [ { "effect": "heal", "amount": 3 } ] },
            { "name": "club", "title": "Garrote", "category": "weapon", "slot": "hand", "attack": 1.5 },
            { "name": "iron_sword", "title": "Espada de hierro", "slot": "hand", "attack": 4 },
            { "name": "iron_helmet", "title": "Casco", "category": "armor", "slot": "head", "defense": 2 },
            { "name": "bone", "title": "Repetido" }
        ] }
        """;

    private const string TypesJson = """
        { "types": [
            { "name": "ogre", "title": "ogro", "speed": 2.0, "inventorySize": 4, "slots": ["hand", "hand", "hand", "head"],
              "equipment": { "hand": "club", "hand2": "club", "hand3": "iron_sword" } },
            { "name": "bad", "slots": ["hand"], "equipment": { "head": "iron_helmet", "hand": "iron_helmet", "hand2": "club" } }
        ] }
        """;

    private static (Registry<ItemDef> Items, List<string> Messages) LoadItems()
    {
        var messages = new List<string>();
        return (ItemDefinitionLoader.Load(ItemsJson, messages).Registry, messages);
    }

    [Fact]
    public void TheItemLoader_ReadsCategoriesSlotsStatsAndEffects()
    {
        var (items, messages) = LoadItems();
        Assert.Equal(5, items.Count);
        Assert.Single(messages);                                  // "bone" repetido
        var apple = items.Get(items.IdOf("apple"));
        Assert.Equal("food", apple.Category);
        Assert.Equal(16, apple.MaxStack);
        Assert.True(apple.IsUsable);
        Assert.Equal(new ItemEffect("heal", 3f), apple.Use![0]);

        var sword = items.Get(items.IdOf("iron_sword"));
        Assert.True(sword.IsEquippable);
        Assert.Equal("hand", sword.Slot);
        Assert.Equal(1, sword.MaxStack);                          // un equipable apila de 1 en 1
        Assert.Equal("weapon", sword.Category);                   // sin categoría: la deduce (ataque > defensa)
        Assert.False(items.Get(items.IdOf("bone")).IsEquippable);
    }

    [Fact]
    public void ASlotLayout_NamesRepeatedSlotsWithANumber()
    {
        var layout = new SlotLayout(new[] { "hand", "hand", "hand", "head" });
        Assert.Equal(new[] { "hand", "hand2", "hand3", "head" }, Enumerable.Range(0, 4).Select(layout.Name));
        Assert.Equal(2, layout.IndexOf("HAND3"));
        Assert.Equal(0, layout.IndexOf("MainHand"));   // nombre de los guardados antiguos
        Assert.Equal(-1, layout.IndexOf("ring"));
    }

    [Fact]
    public void ACreatureWithThreeArms_EquipsThreeWeapons_AndAddsTheirDamage()
    {
        var (items, _) = LoadItems();
        var messages = new List<string>();
        var set = EntityDefinitionLoader.Load(TypesJson, items, messages);
        var def = set.Types.Get(set.Types.IdOf("ogre"));

        Assert.Equal(4, def.Slots!.Count);
        var store = new EntityStore(new ChunkShape(16, 16, 16), new WorldScale(4, 3, 2, 2, 16, 16)) { Definitions = set.Types, Items = items };
        var ogre = store.Spawn(set.Types.IdOf("ogre"), new Vec3d(10, 4, 10), 2.0);

        Assert.Equal(4, ogre.Equipment.SlotCount);
        Assert.Equal(items.IdOf("iron_sword"), ogre.Equipment.Get(2));        // hand3
        Assert.Equal(1.5f + 1.5f + 4f, ogre.Equipment.Attack(items));           // el daño de las TRES armas
        Assert.Null(store.Export(set.Types).Single().Gear);                      // sin cambios: nada que guardar

        ogre.Equipment.Set(1, null);                                             // pierde el garrote de hand2
        var gear = store.Export(set.Types).Single().Gear!;
        Assert.Equal(new[] { "hand", "hand3" }, gear.Select(g => g.Slot));       // se guardan por nombre de ranura
        Assert.Equal(5.5f, ogre.Equipment.Attack(items));
    }

    [Fact]
    public void ItemsThatDoNotFitTheirSlot_AreReportedAndSkipped()
    {
        var (items, _) = LoadItems();
        var messages = new List<string>();
        var set = EntityDefinitionLoader.Load(TypesJson, items, messages);
        var info = set.Info("bad");

        Assert.Equal(3, messages.Count);   // head no existe en ese tipo; iron_helmet no cabe en hand; hand2 no existe
        Assert.Null(info.Loadout.Gear(0));
    }

    [Fact]
    public void Equip_FillsEveryFreeSlotOfTheKind_AndThenReplacesTheFirst()
    {
        var (items, _) = LoadItems();
        var layout = new SlotLayout(new[] { "hand", "hand", "head" });
        var eq = new Equipment(layout);
        var inv = new Inventory(6);
        inv.Add(items, items.IdOf("club"), 1);
        inv.Add(items, items.IdOf("club"), 1);
        inv.Add(items, items.IdOf("iron_sword"), 1);

        Assert.True(ItemOps.Equip(inv, eq, items, 0));
        Assert.True(ItemOps.Equip(inv, eq, items, 1));
        Assert.Equal(items.IdOf("club"), eq.Get(0));
        Assert.Equal(items.IdOf("club"), eq.Get(1));
        Assert.True(ItemOps.Equip(inv, eq, items, 2));                           // manos llenas: sustituye la primera
        Assert.Equal(items.IdOf("iron_sword"), eq.Get(0));
        Assert.Equal(1, inv.CountOf(items.IdOf("club")));                        // el garrote sustituido vuelve a la mochila
        Assert.Equal(1.5f + 4f, eq.Attack(items));
    }

    [Fact]
    public void UsingAConsumable_AppliesItsEffects_AndOnlySpendsItWhenTheyHadAnEffect()
    {
        var (items, _) = LoadItems();
        var inv = new Inventory(2);
        inv.Add(items, items.IdOf("apple"), 2);
        float hp = 10f, max = 10f;

        bool Heal(ItemEffect e)
        {
            if (e.Type != "heal" || hp >= max) return false;
            hp = Math.Min(max, hp + e.Amount);
            return true;
        }

        Assert.False(ItemOps.Use(inv, 0, items, Heal));     // vida llena: no se gasta
        Assert.Equal(2, inv.CountOf(items.IdOf("apple")));

        hp = 5f;
        Assert.True(ItemOps.Use(inv, 0, items, Heal));
        Assert.Equal(8f, hp);
        Assert.Equal(1, inv.CountOf(items.IdOf("apple")));
        Assert.False(ItemOps.Use(inv, 1, items, Heal));     // ranura vacía
    }
}
