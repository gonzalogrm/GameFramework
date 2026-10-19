using GF.Core;
using GF.World.Items;
using Xunit;

namespace GF.World.Tests;

public class ItemPrototypeTests
{
    private const string Json = """
        { "items": [
            { "name": "item",   "abstract": true, "properties": { "category": "misc", "max_stack": 64, "weight": 1.0 } },
            { "name": "weapon", "abstract": true, "parents": ["item"], "properties": { "category": "weapon", "slot": "hand", "attack": 3.0, "max_stack": 1 } },
            { "name": "knife",  "parents": ["item", "weapon"], "title": "Cuchillo", "properties": { "attack": 2.5 } },
            { "name": "sharp",  "abstract": true, "properties": { "category": "sharp", "attack": 4.0 } },
            { "name": "heavy",  "abstract": true, "properties": { "category": "heavy", "max_stack": 2 } },
            { "name": "blade",  "parents": ["sharp", "heavy"] },
            { "name": "blade2", "parents": ["heavy", "sharp"] },
            { "name": "shiv",   "parents": ["item", "weapon"], "properties": { "category": "tool" } },
            { "name": "bone",   "parents": ["item"] },
            { "name": "apple",  "parents": ["item"], "properties": { "use_heal": 3.0, "max_stack": 16 } },
            { "name": "rock",   "abstract": true, "parents": ["item"] }
        ] }
        """;

    private static (ItemCatalog Catalog, List<string> Messages) Load()
    {
        var messages = new List<string>();
        return (ItemDefinitionLoader.Load(Json, messages), messages);
    }

    private static ItemDef Get(ItemCatalog c, string name) => c.Registry.Get(c.Registry.IdOf(name));

    [Fact]
    public void WithMultipleInheritance_TheLastParentWins_AndTheOwnPropertiesWinOverAll()
    {
        var (catalog, _) = Load();

        var knife = Get(catalog, "knife");     // [item, weapon]: lo común lo pone weapon
        Assert.Equal("weapon", knife.Category);
        Assert.Equal("hand", knife.Slot);
        Assert.Equal(2.5f, knife.Attack);      // la propia, sobre weapon (3.0)
        Assert.Equal(1, knife.MaxStack);       // weapon (1) sobre item (64)
        Assert.Equal("knife > weapon > item", knife.Prototype.ChainName);

        // Dos padres sin relación: el orden decide. blade = [sharp, heavy] -> gana heavy; blade2 = [heavy, sharp] -> gana sharp.
        var blade = Get(catalog, "blade");
        Assert.Equal("heavy", blade.Category);
        Assert.Equal(4f, blade.Attack);        // heavy no define attack: viene de sharp
        Assert.Equal(2, blade.MaxStack);
        Assert.Equal("blade > heavy > sharp", blade.Prototype.ChainName);
        var blade2 = Get(catalog, "blade2");
        Assert.Equal("sharp", blade2.Category);
        Assert.Equal(2, blade2.MaxStack);      // solo heavy la define
        Assert.Equal("blade2 > sharp > heavy", blade2.Prototype.ChainName);

        Assert.Equal("tool", Get(catalog, "shiv").Category);   // lo propio gana a todo
    }

    [Fact]
    public void ItemsAreAlsoTheirParents_Polymorphism()
    {
        var (catalog, _) = Load();
        var knife = Get(catalog, "knife");
        Assert.True(knife.Is(catalog.GetPrototype("weapon")));
        Assert.True(knife.Is(catalog.GetPrototype("item")));
        Assert.False(Get(catalog, "bone").Is(catalog.GetPrototype("weapon")));
        Assert.Equal(1f, knife.Get("weight")!.Value.AsFloat);   // una propiedad cualquiera, heredada de item
        Assert.Null(knife.Get("nope"));
    }

    [Fact]
    public void ByDefaultAStackIs64_UnlessIndicated_OrEquippable()
    {
        var (catalog, _) = Load();
        Assert.Equal(64, Get(catalog, "bone").MaxStack);
        Assert.Equal(16, Get(catalog, "apple").MaxStack);
        Assert.Equal(1, Get(catalog, "knife").MaxStack);

        var bare = ItemDefinitionLoader.Load("""{ "items": [ { "name": "x" }, { "name": "y", "slot": "hand" } ] }""", new List<string>());
        Assert.Equal(64, bare.Registry.Get(bare.Registry.IdOf("x")).MaxStack);   // ni un padre lo define
        Assert.Equal(1, bare.Registry.Get(bare.Registry.IdOf("y")).MaxStack);    // equipable: 1
    }

    [Fact]
    public void ConsumableEffects_AreInheritedProperties()
    {
        var (catalog, _) = Load();
        var apple = Get(catalog, "apple");
        Assert.True(apple.IsUsable);
        Assert.Equal(new ItemEffect("heal", 3f), apple.Use![0]);
        Assert.False(Get(catalog, "knife").IsUsable);
    }

    [Fact]
    public void AbstractPrototypes_AreNotItems_AndHaveNoRuntimeVariants()
    {
        var (catalog, messages) = Load();
        Assert.False(catalog.Registry.TryGetId("weapon", out _));
        Assert.False(catalog.Registry.TryGetId("rock", out _));
        Assert.True(catalog.IsAbstract("weapon"));
        Assert.False(catalog.IsAbstract("knife"));
        Assert.Empty(messages);
        Assert.NotNull(catalog.GetPrototype("weapon"));                                     // el prototipo existe y sirve de padre
        Assert.Throws<InvalidOperationException>(() => catalog.Fork().CreateRuntime("rock", "x"));   // pero no se puede crear una variante de él
    }

    [Fact]
    public void AnyConcreteItem_CanHaveRuntimeVariants()
    {
        var (baseCatalog, _) = Load();
        var catalog = baseCatalog.Fork();
        int baseCount = catalog.Registry.Count;

        var special = catalog.CreateRuntime("knife", "knife_of_morgrim", "Cuchillo de Morgrim",
            new Dictionary<string, PropValue> { ["attack"] = PropValue.Of(6f), ["leader_mark"] = PropValue.Of(true) });

        Assert.True(special.IsRuntime);
        Assert.Equal(baseCount, catalog.Registry.IdOf("knife_of_morgrim"));          // ID nuevo, detrás de los del JSON
        Assert.Equal("Cuchillo de Morgrim", special.Title);
        Assert.Equal(6f, special.Attack);                                           // propiedad existente, cambiada
        Assert.True(special.Get("leader_mark")!.Value.AsBool);                      // propiedad NUEVA
        Assert.Equal("weapon", special.Category);                                   // el resto, del objeto base
        Assert.True(special.Is(catalog.GetPrototype("knife")));
        Assert.True(special.Is(catalog.GetPrototype("weapon")));
        Assert.Same(special, catalog.CreateRuntime("knife", "knife_of_morgrim"));    // crearlo otra vez devuelve el mismo
        Assert.Equal(2.5f, Get(catalog, "knife").Attack);                            // la base no cambia
        Assert.Null(Get(catalog, "knife").Get("leader_mark"));

        // Cualquier objeto concreto vale como base, también uno que no es un arma o uno de runtime.
        var bone = catalog.CreateRuntime("bone", "cursed_bone", "Hueso maldito", new Dictionary<string, PropValue> { ["curse"] = PropValue.Of(3) });
        Assert.Equal(3, bone.Get("curse")!.Value.AsInt);
        var again = catalog.CreateRuntime("knife_of_morgrim", "knife_of_morgrim_2", "Cuchillo de Morgrim II");
        Assert.Equal(6f, again.Attack);                                              // hereda de la variante, que hereda de knife
        Assert.True(again.Is(catalog.GetPrototype("knife_of_morgrim")));

        Assert.Throws<InvalidOperationException>(() => catalog.CreateRuntime("knife", "knife"));                           // nombre de un objeto del JSON
        Assert.Throws<InvalidOperationException>(() => catalog.CreateRuntime("nope", "x"));                                // base inexistente
        Assert.Throws<InvalidOperationException>(() => catalog.CreateRuntime("knife", "bad",
            overrides: new Dictionary<string, PropValue> { ["attack"] = PropValue.Of(1) }));                               // tipo distinto del heredado (int vs decimal)
    }

    [Fact]
    public void RuntimeVariants_CanAddComponents_AndExtraParents()
    {
        var (baseCatalog, _) = Load();
        var catalog = baseCatalog.Fork();

        var thorny = catalog.CreateRuntime("knife", "thorny_knife", "Cuchillo espinoso", null, extraParents: new[] { "sharp" }, components: new[] { "Thorns", "regeneration" });
        Assert.Equal(new[] { "thorns", "regeneration" }, thorny.Components);          // se normalizan
        Assert.True(thorny.HasComponent("thorns"));
        Assert.Equal("sharp", thorny.Category);                                        // sharp (padre extra, añadido después) manda sobre knife
        Assert.Equal(4f, thorny.Attack);
        Assert.False(Get(catalog, "knife").HasComponent("thorns"));                    // la base no los tiene

        // Los componentes se heredan: una variante de la variante los conserva.
        var child = catalog.CreateRuntime("thorny_knife", "thorny_knife_2");
        Assert.True(child.HasComponent("regeneration"));
    }

    [Fact]
    public void ComponentsInTheJson_AreInheritedLikeAnyOtherProperty()
    {
        var catalog = ItemDefinitionLoader.Load("""
            { "items": [ { "name": "base", "abstract": true, "components": ["glow"] },
                         { "name": "lamp", "parents": ["base"], "components": ["fragile"] } ] }
            """, new List<string>());
        var lamp = catalog.Registry.Get(catalog.Registry.IdOf("lamp"));
        Assert.True(lamp.HasComponent("glow"));
        Assert.True(lamp.HasComponent("fragile"));
        Assert.Equal(2, lamp.Components.Count);
    }

    [Fact]
    public void RuntimeItems_AreSavedAsADelta_AndRebuiltInAnotherWorld()
    {
        var (baseCatalog, _) = Load();
        var catalog = baseCatalog.Fork();
        catalog.CreateRuntime("knife", "knife_of_morgrim", "Cuchillo de Morgrim", new Dictionary<string, PropValue> { ["attack"] = PropValue.Of(6f) },
            components: new[] { "thorns" });
        catalog.CreateRuntime("knife_of_morgrim", "heir_knife");   // una variante de una variante

        var saved = catalog.ExportRuntime();
        Assert.Equal(2, saved.Count);
        Assert.Equal("knife", saved[0].Base);
        Assert.Equal(new[] { "thorns" }, saved[0].Components);
        Assert.Single(saved[0].Overrides!);                        // solo lo que cambia
        Assert.Null(saved[1].Overrides);                           // la heredera no cambia nada: solo su nombre y su base
        Assert.False(baseCatalog.Registry.TryGetId("knife_of_morgrim", out _));   // el original no se contamina

        var reloaded = baseCatalog.Fork();
        Assert.Empty(reloaded.ImportRuntime(saved));
        var again = Get(reloaded, "knife_of_morgrim");
        Assert.Equal(6f, again.Attack);
        Assert.Equal("Cuchillo de Morgrim", again.Title);
        Assert.True(again.HasComponent("thorns"));
        Assert.Equal(6f, Get(reloaded, "heir_knife").Attack);
    }

    [Fact]
    public void WhatThePlayerCarries_CanBeExportedWithItsDependencies_AndRebuiltInAnotherWorld()
    {
        var (baseCatalog, _) = Load();
        var worldA = baseCatalog.Fork();
        worldA.CreateRuntime("knife", "knife_a", "Cuchillo A", new Dictionary<string, PropValue> { ["attack"] = PropValue.Of(7f) }, components: new[] { "thorns" });
        worldA.CreateRuntime("knife_a", "heir_a", "Heredero A");     // depende de otro objeto de runtime
        worldA.CreateRuntime("bone", "left_in_a");                    // este no se lleva

        var carried = worldA.ExportRuntimeFor(new[] { "heir_a", "knife" /* del JSON: no cuenta */ });
        Assert.Equal(new[] { "knife_a", "heir_a" }, carried.Select(r => r.Name));      // la base primero, y sin lo que no se lleva

        // En otro mundo (otro catálogo) el objeto sigue existiendo con lo mismo, y el inventario lo referencia por nombre.
        var worldB = baseCatalog.Fork();
        Assert.Empty(worldB.ImportRuntime(carried));
        var heir = Get(worldB, "heir_a");
        Assert.Equal(7f, heir.Attack);
        Assert.True(heir.HasComponent("thorns"));
        Assert.False(worldB.Registry.TryGetId("left_in_a", out _));

        // Importar dos veces (el mundo ya lo tenía) no duplica nada.
        Assert.Empty(worldB.ImportRuntime(carried));
        Assert.Equal(2, worldB.ExportRuntime().Count);
    }

    [Fact]
    public void ManyHoldersShareOneItem_TheyOnlyStoreItsId()
    {
        var (catalog, _) = Load();
        ushort id = catalog.Registry.IdOf("knife");
        var holders = Enumerable.Range(0, 1000).Select(_ => new Equipment(SlotLayout.Default)).ToList();
        foreach (var h in holders) h.Set(0, id);
        Assert.All(holders, h => Assert.Same(catalog.Registry.Get(id), catalog.Registry.Get(h.Get(0)!.Value)));
        Assert.All(holders, h => Assert.Equal(2.5f, h.Attack(catalog.Registry)));
    }

    [Fact]
    public void InconsistentOrCircularInheritance_IsReported()
    {
        Assert.Throws<InvalidDataException>(() => ItemDefinitionLoader.Load(
            """{ "items": [ { "name": "a", "parents": ["b"] }, { "name": "b", "parents": ["a"] } ] }""", new List<string>()));
        // a hereda de b y de a, en ese orden (contradictorio: b ya hereda de a).
        Assert.Throws<InvalidDataException>(() => ItemDefinitionLoader.Load(
            """{ "items": [ { "name": "a" }, { "name": "b", "parents": ["a"] }, { "name": "c", "parents": ["b", "a"] } ] }""", new List<string>()));
        // Una propiedad con tipos distintos en dos padres.
        Assert.Throws<InvalidDataException>(() => ItemDefinitionLoader.Load(
            """{ "items": [ { "name": "a", "properties": { "p": 1 } }, { "name": "b", "properties": { "p": 1.5 } }, { "name": "c", "parents": ["a", "b"] } ] }""",
            new List<string>()));
    }
}

public class PrototypeInheritanceTests
{
    [Fact]
    public void ADiamond_PutsTheCommonAncestorBehindBothPaths()
    {
        var a = new Prototype("A").Int("x", 1).Int("y", 1);
        var b = new Prototype("B", a).Int("x", 2);
        var c = new Prototype("C", a).Int("y", 3);
        var d = new Prototype("D", new[] { b, c });

        Assert.Equal("D > C > B > A", d.ChainName);
        Assert.Equal(2, PropertyOps.Get(d, null, PropertyIds.Of("x")).AsInt);   // B (A queda detrás)
        Assert.Equal(3, PropertyOps.Get(d, null, PropertyIds.Of("y")).AsInt);   // C gana: es el último padre
        Assert.True(d.Is(a) && d.Is(b) && d.Is(c));
        Assert.Same(c, d.Parent);
    }

    [Fact]
    public void ParentOrderDecidesWhoOverrides()
    {
        var a = new Prototype("A").Text("t", "from A");
        var b = new Prototype("B").Text("t", "from B");
        Assert.Equal("from B", PropertyOps.Get(new Prototype("AB", new[] { a, b }), null, PropertyIds.Of("t")).AsText);
        Assert.Equal("from A", PropertyOps.Get(new Prototype("BA", new[] { b, a }), null, PropertyIds.Of("t")).AsText);
        Assert.Equal("own", PropertyOps.Get(new Prototype("AB2", new[] { a, b }).Text("t", "own"), null, PropertyIds.Of("t")).AsText);
    }

    [Fact]
    public void SingleInheritance_BehavesAsBefore()
    {
        var a = new Prototype("A").Int("x", 1);
        var b = a.Derive("B").Int("x", 5);
        Assert.Equal("B > A", b.ChainName);
        Assert.Same(a, b.Parent);
        Assert.Equal(5, PropertyOps.Get(b, null, PropertyIds.Of("x")).AsInt);
        Assert.Single(b.Parents);
    }

    [Fact]
    public void ContradictoryOrder_Throws()
    {
        var a = new Prototype("A");
        var b = new Prototype("B", a);
        Assert.Throws<InvalidOperationException>(() => new Prototype("X", new[] { b, a }));
    }
}
