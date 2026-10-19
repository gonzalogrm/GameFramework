using GF.Core;
using GF.World;
using GF.World.Cosmos;
using GF.World.Entities;
using GF.World.History;
using GF.World.Map;

namespace MiniCraft;

/// <summary>
/// Enemigos en cada chunk: al cargarse la columna de chunks (una sola vez por columna, y se recuerda) aparecen 0, 1 o más enemigos en tierra
/// firme, lejos del jugador. Es determinista: qué columna tiene cuántos y dónde sale de (semilla, columna), no del orden de carga.
/// El tipo de enemigo sale de los que tienen "spawnWeight" en entitytypes.json (zombi, ogro...) y su VARIANTE se elige al azar con sus pesos; su inventario y equipamiento vienen de esa variante y
/// al guardar solo se anota cuál es. Lo que lleve puesto le protege y, al morir a manos del jugador, todo pasa a él.
/// Hay un tope global (EnemyLimit) para no acumular enemigos al explorar.
/// </summary>
public sealed class EnemySpawner
{
    private const double SafeDistance = 24.0;   // no aparecen más cerca del jugador

    private readonly EntityStore _store;
    private readonly MiniCraftClimate _climate;
    private readonly WorldScale _scale;
    private readonly NpcSystem _npcs;
    private readonly int _seed;
    private readonly WorldSettings _settings;
    private readonly Func<Vec3d?> _player;
    private readonly HashSet<(int X, int Z)> _populated = new();
    private readonly List<(ushort Id, int Weight)> _table = new();   // tipos que aparecen y su peso
    private readonly int _tableTotal;
    private int _count;

    /// <param name="player">Posición del jugador (en coordenadas de los chunks cargados), o null si aún no se puede aparecer (cargando).</param>
    public EnemySpawner(EntityStore store, World<ushort> world, MiniCraftClimate climate, WorldScale scale, NpcSystem npcs, int seed,
        WorldSettings settings, Func<Vec3d?> player)
    {
        _store = store; _climate = climate; _scale = scale; _npcs = npcs; _seed = seed; _settings = settings; _player = player;
        for (ushort id = 0; id < EntityTypes.Registry.Count; id++)
        {
            int weight = EntityTypes.Registry.Get(id).SpawnWeight;
            if (weight > 0) { _table.Add((id, weight)); _tableTotal += weight; }
        }
        world.ChunkLoaded += OnChunkLoaded;
        store.Removed += e => { if (e.Def?.SpawnWeight > 0) _count--; };
    }

    public int EnemyCount => _count;

    /// <summary>Objetos legendarios del mundo: al generarse la columna donde están, se colocan (en un alijo o con un guardián).</summary>
    public LegendaryRegistry? Legendary { get; set; }

    /// <summary>¿Ya se generó esta columna de chunks (cx envuelto, cz)? Sus objetos legendarios ya se colocaron.</summary>
    public bool IsPopulated(int cx, int cz) => _populated.Contains((cx, cz));

    /// <summary>
    /// Las columnas ya pobladas como TRAMOS [z, x0, x1] (las columnas visitadas son contiguas: un tramo sustituye a decenas de números).
    /// </summary>
    public List<int[]> ExportPopulated()
    {
        var runs = new List<int[]>();
        foreach (var row in _populated.GroupBy(c => c.Z).OrderBy(g => g.Key))
        {
            int start = 0, previous = 0;
            bool open = false;
            foreach (int x in row.Select(c => c.X).OrderBy(x => x))
            {
                if (open && x == previous + 1) { previous = x; continue; }
                if (open) runs.Add(new[] { row.Key, start, previous });
                start = previous = x;
                open = true;
            }
            if (open) runs.Add(new[] { row.Key, start, previous });
        }
        return runs;
    }

    public void ImportPopulated(IEnumerable<int[]>? runs)
    {
        _populated.Clear();
        if (runs == null) return;
        foreach (var r in runs)
            if (r is { Length: 3 })
                for (int x = r[1]; x <= r[2]; x++) _populated.Add((x, r[0]));
    }

    /// <summary>Recuenta los enemigos que aparecen por spawner que hay en el almacén (tras importar una partida).</summary>
    public void CountExisting() => _count = _store.All.Count(e => e.Def?.SpawnWeight > 0);

    private void OnChunkLoaded(ChunkCoord c)
    {
        if (c.Y != 0) return;   // una vez por columna (el chunk más bajo)
        if (_player() is not { } player) return;

        int cx = _scale.WrapChunkX(c.X);
        if (!_populated.Add((cx, c.Z))) return;

        PlaceArtifacts(cx, c.Z);   // los objetos legendarios de esta columna, aunque los enemigos comunes estén desactivados
        if (_settings.EnemiesPerChunk <= 0f || _tableTotal <= 0) return;

        var rng = new SplitMixRandom(Hashing.Combine(_seed, cx, 0xE4E3, c.Z));
        float expected = _settings.EnemiesPerChunk;
        int n = (int)expected + (rng.NextFloat() < expected - (int)expected ? 1 : 0);
        int size = _scale.ChunkSizeX;

        for (int i = 0; i < n && _count < _settings.EnemyLimit; i++)
        {
            int wx = cx * size + rng.Next(size), wz = c.Z * size + rng.Next(size);
            int ground = MiniCraftClimate.ToBlockHeight(_climate.Sample(wx, wz).Height);
            if (ground <= _climate.SeaLevel + _settings.BeachHeight) continue;   // agua y playa: no

            double dx = _scale.DeltaX(player.X, wx + 0.5), dz = wz + 0.5 - player.Z;
            if (dx * dx + dz * dz < SafeDistance * SafeDistance) continue;

            ushort type = PickType(rng);
            var def = EntityTypes.Registry.Get(type);
            var enemy = _store.Spawn(type, new Vec3d(wx + 0.5, ground + 2.0, wz + 0.5), def.DefaultSpeed, null, PickVariant(def, rng));
            if (IsLeader(def, enemy.Variant)) GiveLeaderWeapon(enemy, rng);
            _npcs.BeginWander(enemy);
            _count++;
        }
    }

    /// <summary>
    /// Coloca los objetos legendarios cuya columna se acaba de generar. ESCONDIDO: un alijo (entidad contenedor) con el arma en su inventario, en las coordenadas
    /// del objeto; se abre golpeándolo. EN POSESIÓN: un guardián (ogro líder) que lleva el arma en su última ranura de mano. En los dos casos es solo un
    /// objeto en un inventario o equipo de una entidad: se guarda como cualquier cambio de entidad.
    /// </summary>
    private void PlaceArtifacts(int cx, int cz)
    {
        if (Legendary == null) return;
        var items = ItemTypes.Registry;
        foreach (var a in Legendary.ForColumn(cx, cz))
        {
            int ground = MiniCraftClimate.ToBlockHeight(_climate.Sample(a.X, a.Z).Height);
            if (a.Fate == ArtifactFate.Hidden)
            {
                var cache = _store.Spawn(EntityTypes.RelicCache, new Vec3d(a.X + 0.5, ground + 1.0, a.Z + 0.5), 0.0);
                cache.Inventory.Add(items, a.ItemId, 1);
                continue;
            }

            var def = EntityTypes.Registry.Get(EntityTypes.Ogre);
            int variant = def.Variants == null ? 0 : Math.Max(0, def.Variants.ToList().FindIndex(v => v.Name == "warlord"));
            var guardian = _store.Spawn(EntityTypes.Ogre, new Vec3d(a.X + 0.5, ground + 2.0, a.Z + 0.5), def.DefaultSpeed, null, variant);
            if (IsLeader(def, variant)) GiveLeaderWeapon(guardian, new SplitMixRandom(Hashing.Combine(_seed, cx, 0x6A4D, cz)));
            var weapon = items.Get(a.ItemId);
            for (int slot = guardian.Equipment.SlotCount - 1; slot >= 0; slot--)   // la última ranura del tipo que le sirve
                if (guardian.Equipment.Layout.Kind(slot) == weapon.Slot) { guardian.Equipment.Set(slot, a.ItemId); break; }
            _npcs.BeginWander(guardian);
            _count++;
        }
    }

    private static bool IsLeader(EntityDef def, int variant) =>
        def.Variants != null && variant < def.Variants.Count &&
        PropertyOps.TryGet(def.Variants[variant].Prototype, null, PropertyIds.Of("leader"), out var v) && v.AsBool;

    /// <summary>
    /// Los líderes llevan un arma ÚNICA creada en tiempo de ejecución: una variante de "knife" con el nombre ficticio del líder y más
    /// ataque que el normal. El objeto nuevo es un prototipo más, con su ID; la entidad solo guarda ese ID en su ranura, y el mundo guarda el objeto
    /// (nombre, objeto base y lo que cambia). Determinista: el nombre y el ataque salen del RNG de la columna.
    /// </summary>
    private void GiveLeaderWeapon(Entity enemy, SplitMixRandom rng)
    {
        var catalog = ItemTypes.Catalog;
        string leader = UniverseGenerator.MakeName(rng);
        float baseAttack = PropertyOps.Get(catalog.GetPrototype("knife"), null, PropertyIds.Of("attack")).AsFloat;
        var weapon = catalog.CreateRuntime("knife", $"knife_of_{leader.ToLowerInvariant()}_{(uint)_seed:x8}", $"Cuchillo de {leader}",   // el nombre lleva el mundo: puede viajar con el jugador
            new Dictionary<string, PropValue> { ["attack"] = PropValue.Of(baseAttack + 1f + rng.NextFloat() * 3f) });
        enemy.Set("name", leader);
        int slot = enemy.Equipment.FindSlot("hand");
        if (slot >= 0) enemy.Equipment.Set(slot, catalog.Registry.IdOf(weapon.Name));
    }

    /// <summary>Tipo de enemigo al azar según su spawnWeight.</summary>
    private ushort PickType(SplitMixRandom rng)
    {
        int roll = rng.Next(_tableTotal);
        foreach (var (id, weight) in _table)
        {
            roll -= weight;
            if (roll < 0) return id;
        }
        return _table[0].Id;
    }

    /// <summary>Variante al azar según los pesos de entitytypes.json (la primera si el tipo no tiene variantes).</summary>
    private static int PickVariant(EntityDef def, SplitMixRandom rng)
    {
        var variants = def.Variants;
        if (variants == null || variants.Count <= 1) return 0;
        int total = 0;
        foreach (var v in variants) total += Math.Max(0, v.Weight);
        if (total <= 0) return 0;
        int roll = rng.Next(total);
        for (int i = 0; i < variants.Count; i++)
        {
            roll -= Math.Max(0, variants[i].Weight);
            if (roll < 0) return i;
        }
        return 0;
    }
}
