using GF.Core;
using GF.World.Cosmos;
using GF.World.Entities;
using GF.World.Items;
using GF.World.Map;

namespace GF.World.History;

/// <summary>Qué fue de un objeto legendario cuando se hizo el mundo: lo lleva un enemigo (guardián) o está escondido en unas coordenadas.</summary>
public enum ArtifactFate { Held, Hidden }

/// <summary>
/// Un objeto legendario forjado por un héroe. Es un objeto de RUNTIME (variante de un arma existente, con propiedades nuevas y quizá componentes), así que
/// existe en el catálogo del mundo con su ID. (X, Z) son las coordenadas donde se coloca cuando se genera su columna de chunks.
/// </summary>
public sealed record LegendaryArtifact(string ItemName, ushort ItemId, string Title, Hero Hero, Epoch Epoch, ArtifactFate Fate, int ColumnX, int ColumnZ,
    int X, int Z, float Attack, IReadOnlyList<string> Components);

public enum ArtifactState { Pending, HeldByEntity, InCache, OnPlayer, Lost }

/// <summary>Dónde está ahora un objeto legendario (para depurar): el estado, quién lo tiene y dónde.</summary>
public sealed record ArtifactLocation(ArtifactState State, Entity? Holder, Vec3d? Position, string Description);

/// <summary>Cómo se forjan los objetos legendarios de un mundo.</summary>
public sealed class LegendaryOptions
{
    /// <summary>Probabilidad de que cada héroe forje un arma legendaria.</summary>
    public float ForgeChance { get; set; } = 0.6f;
    /// <summary>Componentes que puede llevar un arma legendaria (el juego les asocia comportamientos). Se eligen 0 o 1 al azar, deterministas.</summary>
    public string[] Components { get; set; } = Array.Empty<string>();
    /// <summary>Categoría de los objetos del catálogo que pueden servir de base (las armas).</summary>
    public string BaseCategory { get; set; } = "weapon";
    /// <summary>Prefijo del nombre de los objetos. Debe ser ÚNICO por mundo: el jugador puede llevarse un objeto a otro mundo y allí no puede chocar con otro del mismo nombre.</summary>
    public string NamePrefix { get; set; } = "legendary";
}

/// <summary>
/// La lista de armas y objetos legendarios de un mundo, para consultar y depurar: cuáles hay, quién los forjó y DÓNDE están ahora (qué enemigo los tiene,
/// en qué alijo, en qué coordenadas aún sin generar o si el jugador ya los tiene). Sale de la historia del mundo y es determinista (no se guarda; los
/// objetos creados sí, como cualquier objeto de runtime, y las entidades que los llevan, como cualquier entidad).
/// </summary>
public sealed class LegendaryRegistry
{
    private readonly List<LegendaryArtifact> _artifacts;

    private LegendaryRegistry(List<LegendaryArtifact> artifacts) => _artifacts = artifacts;

    public IReadOnlyList<LegendaryArtifact> Artifacts => _artifacts;

    /// <summary>Los que se colocan en esa columna de chunks (cx envuelto, cz).</summary>
    public IEnumerable<LegendaryArtifact> ForColumn(int cx, int cz) => _artifacts.Where(a => a.ColumnX == cx && a.ColumnZ == cz);

    /// <summary>
    /// Forja los objetos legendarios de una historia: por cada héroe, con cierta probabilidad, una VARIANTE de un arma del catálogo (ataque mayor, propiedades
    /// nuevas como hero y epoch, a veces un componente) que se registra en el catálogo con un ID nuevo. Idempotente: si el mundo se carga con esos objetos
    /// ya recreados desde el guardado, devuelve los mismos.
    /// </summary>
    /// <param name="isLand">¿Es tierra firme esa posición (x, z)? Los objetos solo se colocan en tierra.</param>
    public static LegendaryRegistry Create(WorldHistory history, ulong seed, ItemCatalog catalog, WorldScale scale, Func<int, int, bool> isLand,
        LegendaryOptions? options = null)
    {
        options ??= new LegendaryOptions();
        var bases = new List<ItemDef>();
        for (ushort id = 0; id < catalog.Registry.Count; id++)
        {
            var def = catalog.Registry.Get(id);
            if (!def.IsRuntime && def.Category == options.BaseCategory && def.Attack > 0) bases.Add(def);
        }

        var artifacts = new List<LegendaryArtifact>();
        if (bases.Count == 0) return new LegendaryRegistry(artifacts);

        int size = scale.ChunkSizeX;
        int columnsX = Math.Max(1, scale.ChunkCountX), columnsZ = Math.Max(1, scale.HeightBlocks / Math.Max(1, scale.ChunkSizeZ));

        foreach (var epoch in history.Epochs)
        foreach (var hero in epoch.Heroes)
        {
            var rng = new SplitMixRandom(UniverseGenerator.Mix(UniverseGenerator.Mix(seed, 0x1E6E4DA2), (ulong)(epoch.Index * 16 + hero.Index)));
            if (!rng.Chance(options.ForgeChance)) continue;

            var baseItem = bases[rng.Next(bases.Count)];
            float attack = baseItem.Attack + 2f + rng.NextFloat() * 4f;
            string component = options.Components.Length > 0 && rng.Chance(0.5f) ? options.Components[rng.Next(options.Components.Length)] : "";
            string name = $"{options.NamePrefix}_{epoch.Index}_{hero.Index}";
            string title = $"{baseItem.Title} de {hero.Name} {hero.Epithet}";

            var item = catalog.CreateRuntime(baseItem.Name, name, title, new Dictionary<string, PropValue>
            {
                ["attack"] = PropValue.Of(attack),
                ["legendary"] = PropValue.Of(true),
                ["hero"] = PropValue.Of(hero.ToString()),
                ["epoch"] = PropValue.Of(epoch.Index),
                ["civilization"] = PropValue.Of(epoch.Civilization),
            }, null, component.Length > 0 ? new[] { component } : null);

            // Destino: guardián que lo lleva, o escondido; en una columna de tierra firme (hasta 30 intentos deterministas).
            var fate = rng.Chance(0.5f) ? ArtifactFate.Held : ArtifactFate.Hidden;
            int cx = 0, cz = 0, wx = 0, wz = 0;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                cx = rng.Next(columnsX); cz = rng.Next(columnsZ);
                wx = cx * size + rng.Next(size); wz = cz * scale.ChunkSizeZ + rng.Next(scale.ChunkSizeZ);
                if (isLand(wx, wz)) break;
            }
            artifacts.Add(new LegendaryArtifact(name, catalog.Registry.IdOf(name), item.Title, hero, epoch, fate, cx, cz, wx, wz, item.Attack,
                item.Components));
        }
        return new LegendaryRegistry(artifacts);
    }

    /// <summary>Dónde está ahora un objeto legendario. Mira al jugador, luego a las entidades (sin crear sus inventarios) y por último si su columna ya se generó.</summary>
    /// <param name="isPopulated">¿Ya se generó esa columna de chunks (cx, cz)? Si no, el objeto aún no existe en el mundo: está pendiente en sus coordenadas.</param>
    public ArtifactLocation Locate(LegendaryArtifact a, EntityStore store, IInventoryHolder? player, Func<int, int, bool> isPopulated)
    {
        if (player != null && Holds(player, a.ItemId))
            return new ArtifactLocation(ArtifactState.OnPlayer, null, null, "Lo tiene el jugador (en el inventario o equipado).");

        foreach (var e in store.All)
        {
            if (!e.Holds(a.ItemId)) continue;
            string where = $"({e.Position.X:0}, {e.Position.Y:0}, {e.Position.Z:0})";
            bool cache = e.Prototype?.HasComponent("container") == true;
            return cache
                ? new ArtifactLocation(ArtifactState.InCache, e, e.Position, $"Escondido en {e.Label} #{e.Id} en {where}.")
                : new ArtifactLocation(ArtifactState.HeldByEntity, e, e.Position, $"Lo lleva {e.Label} #{e.Id} en {where} ({e.Tier}).");
        }

        if (!isPopulated(a.ColumnX, a.ColumnZ))
            return new ArtifactLocation(ArtifactState.Pending, null, new Vec3d(a.X + 0.5, 0, a.Z + 0.5),
                $"Aún sin generar: se colocará {(a.Fate == ArtifactFate.Held ? "con un guardián" : "en un alijo")} en la columna ({a.ColumnX}, {a.ColumnZ}), hacia x {a.X}, z {a.Z}.");
        return new ArtifactLocation(ArtifactState.Lost, null, null, "Se generó, pero ya no lo tiene nadie (el guardián o el alijo desaparecieron sin soltarlo).");
    }

    private static bool Holds(IInventoryHolder holder, ushort item)
    {
        if (holder.Inventory.CountOf(item) > 0) return true;
        for (int s = 0; s < holder.Equipment.SlotCount; s++) if (holder.Equipment.Get(s) == item) return true;
        return false;
    }
}
