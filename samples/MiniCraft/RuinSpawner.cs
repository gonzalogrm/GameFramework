using GF.Core;
using GF.World;
using GF.World.Entities;
using GF.World.Map;

namespace MiniCraft;

/// <summary>
/// Puebla las ruinas: al cargarse el chunk (columna inferior) que contiene el centro de una ruina, deja un mojón, los cofres con el tesoro y un NPC especial
/// (una sola vez: el mojón queda como señal de que ya se hizo) y repone los enemigos que la guardan hasta el número previsto (cada vez que se visita, con la
/// etiqueta "ruin"). Todo sale de RuinPlan. Si falta algún tipo en entitytypes.json, simplemente no se crea.
/// </summary>
public sealed class RuinSpawner
{
    private static readonly string[] HermitNames = { "Anselmo", "Berta", "Cirilo", "Dalia" };
    private static readonly string[] ArchaeologistNames = { "Irene", "Teodoro", "Wanda", "Xavi" };
    private static readonly string[] EngineerNames = { "Nora", "Olaf", "Petra", "Quim" };
    private static readonly string[] ClericNames = { "Fray Benito", "Hermana Clara", "Padre Tomás", "Sor Inés" };

    /// <summary>Nombres según el tipo de NPC: técnicos y pilotos, religiosos, estudiosos o ermitaños.</summary>
    private static string[] NamesFor(string type) => type switch
    {
        "engineer" or "reactor_tech" or "foreman" or "pilot" or "astronaut" => EngineerNames,
        "monk" or "sacristan" or "pilgrim" => ClericNames,
        "archaeologist" or "historian" => ArchaeologistNames,
        _ => HermitNames,
    };

    private readonly EntityStore _store;
    private readonly CityPlanner _planner;
    private readonly WorldScale _scale;
    private readonly RuinSettings _settings;
    private readonly List<Entity> _scratch = new();

    public RuinSpawner(EntityStore store, World<ushort> world, CityPlanner planner, WorldScale scale, RuinSettings settings)
    {
        _store = store; _planner = planner; _scale = scale; _settings = settings;
        world.ChunkLoaded += OnChunkLoaded;
    }

    private void OnChunkLoaded(ChunkCoord c)
    {
        if (c.Y != 0) return;
        int size = _scale.ChunkSizeX;
        int cx = _scale.WrapChunkX(c.X);
        foreach (var city in _planner.CitiesAt(cx * size + size / 2, c.Z * size + size / 2))
        {
            if (!city.Abandoned) continue;
            if (city.CenterX < cx * size || city.CenterX >= (cx + 1) * size || city.CenterZ < c.Z * size || city.CenterZ >= (c.Z + 1) * size) continue;
            Populate(city);
        }
    }

    private static bool TryId(string type, out ushort id) => EntityTypes.Registry.TryGetId(type, out id);

    private void Populate(City city)
    {
        _scratch.Clear();
        _store.QueryRadius(new Vec3d(city.CenterX, city.GroundY, city.CenterZ), city.Radius + 24, _scratch);
        bool marked = TryId("ruin_marker", out ushort markerId) && _scratch.Any(e => e.TypeId == markerId);
        int guards = _scratch.Count(e => e.Tag == "ruin");
        _scratch.Clear();

        var plan = RuinPlan.Make(city, new SplitMixRandom(Hashing.Combine(city.Id, 0x2018, 0, 0)));
        var rng = new SplitMixRandom(Hashing.Combine(city.Id, 0x2017, guards, 0));

        if (!marked)
        {
            Spawn("ruin_marker", plan.Marker, city, null, null);
            if (_settings.Chests) foreach (var chest in plan.Chests) Spawn(chest.Type, chest.Spot, city, null, null);
            if (_settings.Npcs && plan.NpcType.Length > 0)
            {
                var names = NamesFor(plan.NpcType);
                Spawn(plan.NpcType, plan.Npc, city, null, names[rng.Next(names.Length)]);
            }
        }

        // Los guardianes: se reponen hasta el número previsto, repartidos por la ruina.
        int target = (int)Math.Round(plan.EnemyCount * _settings.EnemyFactor);
        for (int i = guards; i < target && plan.EnemyTypes.Length > 0; i++)
        {
            double angle = rng.NextFloat() * Math.PI * 2, dist = city.Radius * (0.25 + 0.6 * rng.NextFloat());
            var spot = new RuinPlan.Spot((int)(Math.Cos(angle) * dist), (int)(Math.Sin(angle) * dist), city.GroundY + 3);
            Spawn(plan.EnemyTypes[rng.Next(plan.EnemyTypes.Length)], spot, city, "ruin", null);
        }
    }

    private void Spawn(string type, RuinPlan.Spot spot, City city, string? tag, string? name)
    {
        if (!TryId(type, out ushort id)) return;
        var def = EntityTypes.Registry.Get(id);
        var e = _store.Spawn(id, new Vec3d(city.CenterX + spot.Rx + 0.5, spot.Y, city.CenterZ + spot.Rz + 0.5), def.DefaultSpeed);
        if (tag != null) e.Tag = tag;
        if (name != null) e.Set("name", name);
    }
}
