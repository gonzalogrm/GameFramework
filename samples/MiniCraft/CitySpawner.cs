using GF.Core;
using GF.World;
using GF.World.Entities;
using GF.World.Map;

namespace MiniCraft;

/// <summary>
/// Habitantes de las ciudades: al cargarse el chunk (columna inferior) que contiene el centro de una ciudad aparecen aldeanos en su plaza, un mercader, un alcalde (en el salón, o en la plaza si no hay) y herreros (en los talleres) y guardas que patrullan.
/// Sin estado propio: si ya hay aldeanos cerca del centro (partida cargada) no vuelve a crear nada.
/// </summary>
public sealed class CitySpawner
{
    private static readonly string[] MerchantNames = { "Zahir", "Pepa", "Lorenzo", "Ines" };
    private static readonly string[] SmithNames = { "Brokk", "Hilda", "Torin", "Ysolde" };
    private static readonly string[] MayorNames = { "Aldric", "Marisol", "Evandro", "Odalys" };
    private static readonly string[] GuardNames = { "Rufo", "Bela", "Cato", "Dora", "Nilo", "Fenna" };

    private readonly EntityStore _store;
    private readonly CityPlanner _planner;
    private readonly NpcSystem _npcs;
    private readonly WorldScale _scale;
    private readonly CitySettings _settings;
    private readonly List<Entity> _scratch = new();

    public CitySpawner(EntityStore store, World<ushort> world, CityPlanner planner, NpcSystem npcs, WorldScale scale, CitySettings settings)
    {
        _store = store; _planner = planner; _npcs = npcs; _scale = scale; _settings = settings;
        world.ChunkLoaded += OnChunkLoaded;
    }

    private void OnChunkLoaded(ChunkCoord c)
    {
        if (c.Y != 0 || _settings.Villagers <= 0) return;
        int size = _scale.ChunkSizeX;
        int cx = _scale.WrapChunkX(c.X);
        foreach (var city in _planner.CitiesAt(cx * size + size / 2, c.Z * size + size / 2))
        {
            if (city.CenterX < cx * size || city.CenterX >= (cx + 1) * size || city.CenterZ < c.Z * size || city.CenterZ >= (c.Z + 1) * size) continue;

            // ¿Ya hay aldeanos aquí (partida guardada)?
            _scratch.Clear();
            _store.QueryRadius(new Vec3d(city.CenterX, city.GroundY, city.CenterZ), city.Radius, _scratch);
            bool present = _scratch.Any(e => e.TypeId == EntityTypes.Villager);
            _scratch.Clear();
            if (present) continue;

            var rng = new SplitMixRandom(Hashing.Combine(city.Id, 0xB11A, 0, 0));
            SpawnVillagers(city, rng);
            if (_settings.SpecialNpcs && !city.Hamlet) SpawnSpecials(city, rng);   // los poblados no tienen mercader ni alcalde
            SpawnGuards(city, rng);
        }
    }

    private void SpawnVillagers(City city, SplitMixRandom rng)
    {
        var def = EntityTypes.Registry.Get(EntityTypes.Villager);
        for (int i = 0; i < (city.Hamlet ? Math.Max(1, _settings.Villagers / 2) : _settings.Villagers); i++)
        {
            // En la plaza, alrededor de la fuente (entre 4 y 9 bloques del centro).
            double angle = rng.NextFloat() * Math.PI * 2, radius = 4 + rng.NextFloat() * 5;
            double x = city.CenterX + Math.Cos(angle) * radius;
            double z = city.CenterZ + Math.Sin(angle) * radius;
            var e = _store.Spawn(EntityTypes.Villager, new Vec3d(x, city.GroundY + 2.0, z), def.DefaultSpeed);
            _npcs.BeginWander(e);
        }
    }

    /// <summary>Crea un NPC de un tipo del JSON (null si ese tipo no existe) con un nombre propio. Se asienta en el suelo al activarse.</summary>
    private Entity? SpawnNpc(string type, double x, double z, string[] names, SplitMixRandom rng, City city)
    {
        if (!EntityTypes.Registry.TryGetId(type, out ushort id)) return null;
        var def = EntityTypes.Registry.Get(id);
        var e = _store.Spawn(id, new Vec3d(x + 0.5, city.GroundY + 2.0, z + 0.5), def.DefaultSpeed);
        e.Set("name", names[rng.Next(names.Length)]);
        return e;
    }

    /// <summary>Mercader en la plaza; alcalde delante del primer salón; herrero delante de cada taller (máximo 2).</summary>
    private void SpawnSpecials(City city, SplitMixRandom rng)
    {
        bool mayor = false;
        int smiths = 0;
        foreach (var m in CityStage.Landmarks(city))
        {
            double x = city.CenterX + m.Rx, z = city.CenterZ + m.Rz;
            if (m.Role == "hall" && !mayor) { SpawnNpc("mayor", x, z, MayorNames, rng, city); mayor = true; }
            else if (m.Role == "workshop" && smiths < 2) { SpawnNpc("blacksmith", x, z, SmithNames, rng, city); smiths++; }
        }
        int o = Math.Max(3, city.PlazaHalf - 3);   // dentro de la plaza, que cambia de tamaño con el tipo de ciudad
        if (!mayor) SpawnNpc("mayor", city.CenterX + o, city.CenterZ + o, MayorNames, rng, city);
        SpawnNpc("merchant", city.CenterX - o, city.CenterZ + o - 1, MerchantNames, rng, city);
    }

    /// <summary>Guardas alrededor de la plaza. Su Tag recuerda a qué ciudad pertenecen: patrullan cerca de ella (ver GuardAi).</summary>
    private void SpawnGuards(City city, SplitMixRandom rng)
    {
        int count = _settings.Guards >= 0 ? _settings.Guards : Math.Clamp(2 + city.Radius / 25, 2, 6);
        if (city.Hamlet) count = Math.Min(count, 2);
        for (int i = 0; i < count; i++)
        {
            double angle = rng.NextFloat() * Math.PI * 2, radius = 8 + rng.NextFloat() * 8;
            var e = SpawnNpc("guard", city.CenterX + Math.Cos(angle) * radius, city.CenterZ + Math.Sin(angle) * radius, GuardNames, rng, city);
            if (e == null) return;   // el tipo "guard" no está en entitytypes.json
            e.Tag = $"home:{city.CenterX}:{city.CenterZ}";
            _store.SetDestination(e, GuardAi.PatrolTarget(e, (px, pz) => city.GroundY + 2.0));
        }
    }
}
