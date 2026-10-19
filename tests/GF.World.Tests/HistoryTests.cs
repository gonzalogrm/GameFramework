using GF.Core;
using GF.World;
using GF.World.Entities;
using GF.World.History;
using GF.World.Items;
using GF.World.Map;
using Xunit;

namespace GF.World.Tests;

public class WorldHistoryTests
{
    [Fact]
    public void TheHistory_IsDeterministic_AndBounded()
    {
        var a = WorldHistory.Generate(42);
        var b = WorldHistory.Generate(42);

        Assert.InRange(a.Epochs.Count, 2, 10);
        Assert.Equal(a.Epochs.Select(e => e.Civilization), b.Epochs.Select(e => e.Civilization));
        Assert.Equal(a.Heroes.Select(h => h.ToString()), b.Heroes.Select(h => h.ToString()));
        Assert.All(a.Epochs, e => Assert.InRange(e.Heroes.Count, 1, 3));
        for (int i = 1; i < a.Epochs.Count; i++) Assert.True(a.Epochs[i].StartYear > a.Epochs[i - 1].EndYear);   // épocas en orden, con un periodo oscuro entre ellas
        Assert.NotEqual(WorldHistory.Generate(43).Epochs.Select(e => e.Civilization), a.Epochs.Select(e => e.Civilization));
        Assert.True(WorldHistory.Generate(7, maxEpochs: 3).Epochs.Count <= 3);
        Assert.Equal(10, Enumerable.Range(0, 400).Max(s => WorldHistory.Generate((ulong)s).Epochs.Count));      // el tope de 10 se alcanza y no se supera
    }
}

public class LegendaryTests
{
    private const string ItemsJson = """
        { "items": [
            { "name": "item",   "abstract": true, "properties": { "category": "misc" } },
            { "name": "weapon", "abstract": true, "parents": ["item"], "properties": { "category": "weapon", "slot": "hand", "attack": 1.0 } },
            { "name": "club",   "parents": ["weapon"], "title": "Garrote", "properties": { "attack": 1.5 } },
            { "name": "sword",  "parents": ["weapon"], "title": "Espada",  "properties": { "attack": 4.0 } },
            { "name": "bone",   "parents": ["item"] }
        ] }
        """;

    private const string TypesJson = """
        { "types": [
            { "name": "guardian", "speed": 2.0, "inventorySize": 4 },
            { "name": "cache", "speed": 0.0, "inventorySize": 4, "components": ["container"] }
        ] }
        """;

    private static readonly WorldScale Scale = new(4, 3, 2, 2, 16, 16);   // 8 columnas en X, 6 en Z

    private static ItemCatalog NewCatalog() => ItemDefinitionLoader.Load(ItemsJson, new List<string>()).Fork();

    private static LegendaryRegistry NewRegistry(ItemCatalog catalog, ulong seed, Func<int, int, bool>? isLand = null) =>
        LegendaryRegistry.Create(WorldHistory.Generate(seed), seed, catalog, Scale, isLand ?? ((_, _) => true),
            new LegendaryOptions { ForgeChance = 1f, Components = new[] { "thorns", "regeneration" } });

    private sealed class Holder : IInventoryHolder
    {
        public Inventory Inventory { get; } = new(4);
        public Equipment Equipment { get; } = new();
    }

    [Fact]
    public void EveryHeroForgesALegendaryVariant_OfAnExistingWeapon()
    {
        var catalog = NewCatalog();
        var history = WorldHistory.Generate(5);
        var registry = NewRegistry(catalog, 5);

        Assert.Equal(history.Heroes.Count(), registry.Artifacts.Count);   // ForgeChance 1: uno por héroe
        foreach (var a in registry.Artifacts)
        {
            var def = catalog.Registry.Get(a.ItemId);
            Assert.True(def.IsRuntime);
            Assert.True(def.Get("legendary")!.Value.AsBool);
            Assert.Equal(a.Hero.ToString(), def.Get("hero")!.Value.AsText);
            Assert.Equal(a.Epoch.Index, def.Get("epoch")!.Value.AsInt);
            Assert.True(def.Is(catalog.GetPrototype("weapon")));
            Assert.Contains("de " + a.Hero.Name, def.Title);
            Assert.True(a.Attack >= 3f);                                  // más que cualquier base (1.5 + 2 como mínimo)
            Assert.InRange(a.ColumnX, 0, 7);
            Assert.InRange(a.ColumnZ, 0, 5);
            Assert.InRange(a.X, a.ColumnX * 16, a.ColumnX * 16 + 15);
            Assert.InRange(a.Z, a.ColumnZ * 16, a.ColumnZ * 16 + 15);
        }
    }

    [Fact]
    public void AcrossWorlds_ComponentsAndFatesVary()
    {
        var all = Enumerable.Range(0, 30).SelectMany(seed => NewRegistry(NewCatalog(), (ulong)seed).Artifacts).ToList();
        Assert.Contains(all, a => a.Components.Count > 0);                 // algunos llevan un componente...
        Assert.Contains(all, a => a.Components.Count == 0);                // ...y otros no
        Assert.Contains(all, a => a.Fate == ArtifactFate.Held);            // los hay en poder de un guardián
        Assert.Contains(all, a => a.Fate == ArtifactFate.Hidden);          // y escondidos
        Assert.Equal(new[] { "regeneration", "thorns" }, all.SelectMany(a => a.Components).Distinct().OrderBy(c => c));
    }

    [Fact]
    public void TheRegistry_IsDeterministic_AndIdempotentWhenTheItemsWereAlreadyRestored()
    {
        var first = NewRegistry(NewCatalog(), 11);
        var second = NewRegistry(NewCatalog(), 11);
        Assert.Equal(first.Artifacts.Select(a => (a.ItemName, a.Title, a.Fate, a.ColumnX, a.ColumnZ, a.X, a.Z, a.Attack)),
                     second.Artifacts.Select(a => (a.ItemName, a.Title, a.Fate, a.ColumnX, a.ColumnZ, a.X, a.Z, a.Attack)));

        // Al abrir un mundo guardado, el catálogo ya trae los objetos de runtime restaurados: crear el registro no los duplica.
        var saved = NewCatalog();
        NewRegistry(saved, 11);
        var restored = NewCatalog();
        Assert.Empty(restored.ImportRuntime(saved.ExportRuntime()));
        int count = restored.Registry.Count;
        var again = NewRegistry(restored, 11);
        Assert.Equal(count, restored.Registry.Count);
        Assert.Equal(first.Artifacts.Count, again.Artifacts.Count);
    }

    [Fact]
    public void Artifacts_AreOnlyPlacedOnLand()
    {
        var registry = NewRegistry(NewCatalog(), 3, (x, z) => x < 64);   // solo la mitad oeste es tierra
        Assert.All(registry.Artifacts, a => Assert.True(a.X < 64));
    }

    [Fact]
    public void Locate_FindsWhoHoldsEachArtifact_ForDebugging()
    {
        var catalog = NewCatalog();
        var registry = NewRegistry(catalog, 9);
        var set = EntityDefinitionLoader.Load(TypesJson, catalog.Registry, new List<string>());
        var store = new EntityStore(new ChunkShape(16, 16, 16), Scale) { Definitions = set.Types, Items = catalog.Registry };
        var player = new Holder();
        bool populated = false;
        bool IsPopulated(int cx, int cz) => populated;

        var a = registry.Artifacts[0];
        var b = registry.Artifacts[1];

        // Aún no se generó su columna: pendiente, con las coordenadas donde aparecerá.
        var pending = registry.Locate(a, store, player, IsPopulated);
        Assert.Equal(ArtifactState.Pending, pending.State);
        Assert.Equal(a.X + 0.5, pending.Position!.Value.X);

        // Se generó la columna pero nadie lo tiene: perdido.
        populated = true;
        Assert.Equal(ArtifactState.Lost, registry.Locate(a, store, player, IsPopulated).State);

        // Un guardián lo lleva equipado (sin tocar su inventario).
        var guardian = store.Spawn(set.Types.IdOf("guardian"), new Vec3d(40, 5, 40), 2.0);
        guardian.Equipment.Set(0, a.ItemId);
        var held = registry.Locate(a, store, player, IsPopulated);
        Assert.Equal(ArtifactState.HeldByEntity, held.State);
        Assert.Same(guardian, held.Holder);

        // Escondido en un alijo (un contenedor).
        var cache = store.Spawn(set.Types.IdOf("cache"), new Vec3d(70, 5, 20), 0.0);
        cache.Inventory.Add(catalog.Registry, b.ItemId, 1);
        var hidden = registry.Locate(b, store, player, IsPopulated);
        Assert.Equal(ArtifactState.InCache, hidden.State);
        Assert.Equal(new Vec3d(70, 5, 20), hidden.Position);

        // El jugador lo coge: ahora es suyo.
        guardian.Equipment.Set(0, null);
        player.Inventory.Add(catalog.Registry, a.ItemId, 1);
        Assert.Equal(ArtifactState.OnPlayer, registry.Locate(a, store, player, IsPopulated).State);
    }

    [Fact]
    public void ForColumn_ReturnsTheArtifactsOfThatColumn()
    {
        var registry = NewRegistry(NewCatalog(), 21);
        foreach (var a in registry.Artifacts)
            Assert.Contains(a, registry.ForColumn(a.ColumnX, a.ColumnZ));
        Assert.Empty(registry.ForColumn(99, 99));
    }
}
