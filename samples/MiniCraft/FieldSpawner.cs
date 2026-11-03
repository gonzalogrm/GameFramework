using GF.Core;
using GF.World;
using GF.World.Entities;
using GF.World.Map;

namespace MiniCraft;

/// <summary>
/// Hallazgos de los campos kilométricos de estatuas y urnas (ver StatueFields): en una malla de 192 x 192 hay, con una probabilidad que baja hacia el borde de la
/// región, un punto de interés con un cofre (ofrenda en los campos de estatuas, funerario en los de urnas), unos guardianes y a veces un peregrino o un sepulturero.
/// Se crea al cargarse el chunk que lo contiene, una sola vez (un mojón lo marca). Sin estado propio.
/// </summary>
public sealed class FieldSpawner
{
    private const int Grid = 192;
    private static readonly string[] Names = { "Isidro", "Jimena", "Lupe", "Mauro", "Noelia", "Ovidio" };

    private readonly EntityStore _store;
    private readonly CityPlanner _planner;
    private readonly WorldScale _scale;
    private readonly TerrainColumnCache _columns;
    private readonly int _seed, _sea;
    private readonly RuinSettings _settings;
    private readonly List<Entity> _scratch = new();

    public FieldSpawner(EntityStore store, World<ushort> world, CityPlanner planner, WorldScale scale, TerrainColumnCache columns, int seed, int seaLevel, RuinSettings settings)
    {
        _settings = settings;
        _store = store; _planner = planner; _scale = scale; _columns = columns; _seed = seed; _sea = seaLevel;
        world.ChunkLoaded += OnChunkLoaded;
    }

    private void OnChunkLoaded(ChunkCoord c)
    {
        if (c.Y != 0) return;
        int size = _scale.ChunkSizeX, x0 = _scale.WrapChunkX(c.X) * size, z0 = c.Z * size;
        if (StatueFields.RegionAt(_seed, _scale, x0 + size / 2, z0 + size / 2, out _) == null) return;

        for (int gz = IntMath.FloorDiv(z0 - 30, Grid); gz <= IntMath.FloorDiv(z0 + size + 30, Grid); gz++)
        for (int gx = IntMath.FloorDiv(x0 - 30, Grid); gx <= IntMath.FloorDiv(x0 + size + 30, Grid); gx++)
        {
            ulong h = Hashing.Combine(_seed, gx, 0xF0F0, gz);
            int px = gx * Grid + 20 + (int)(h % (ulong)(Grid - 40)), pz = gz * Grid + 20 + (int)((h >> 16) % (ulong)(Grid - 40));
            if (px < x0 || px >= x0 + size || pz < z0 || pz >= z0 + size) continue;   // cada punto lo crea el chunk que lo contiene
            Populate(px, pz, h);
        }
    }

    private static bool TryId(string type, out ushort id) => EntityTypes.Registry.TryGetId(type, out id);

    private void Populate(int px, int pz, ulong h)
    {
        var region = StatueFields.RegionAt(_seed, _scale, px, pz, out double f);
        if (region == null) return;
        if ((h >> 40) * (1.0 / (1 << 24)) >= _settings.FieldFindChance * Math.Clamp(f / 0.3, 0.0, 1.0)) return;   // no todas las celdas tienen un hallazgo
        if ((uint)pz >= (uint)_scale.HeightBlocks || _planner.IsReserved(px, pz)) return;
        int g = _columns.HeightAt(px, pz);
        if (g <= _sea + 1 || !TryId("ruin_marker", out ushort markerId)) return;

        _scratch.Clear();
        _store.QueryRadius(new Vec3d(px, g, pz), 10, _scratch);
        bool marked = _scratch.Any(e => e.TypeId == markerId);
        _scratch.Clear();
        if (marked) return;

        var rng = new SplitMixRandom(h);
        int kind = region.Kind == 2 ? (int)((h >> 36) & 1UL) : region.Kind;
        Spawn("ruin_marker", px, g + 3, pz, null, null);
        if (_settings.Chests) Spawn(kind == 1 ? "chest_urn" : "chest_offering", px + 1, g + 3, pz, null, null);

        string[] guards = kind == 1 ? new[] { "urn_wraith", "revenant" } : new[] { "golem", "revenant", "royal_guard" };
        int n = (int)Math.Round((1 + rng.Next(3)) * _settings.EnemyFactor);
        for (int i = 0; i < n; i++)
        {
            double angle = rng.NextFloat() * Math.PI * 2, dist = 4 + rng.NextFloat() * 4;
            Spawn(guards[rng.Next(guards.Length)], px + (int)(Math.Cos(angle) * dist), g + 3, pz + (int)(Math.Sin(angle) * dist), "field", null);
        }
        if (_settings.Npcs && rng.Chance(0.25f)) Spawn(kind == 1 ? "gravekeeper" : "pilgrim", px - 3, g + 3, pz + 2, null, Names[rng.Next(Names.Length)]);
    }

    private void Spawn(string type, int x, int y, int z, string? tag, string? name)
    {
        if (!TryId(type, out ushort id)) return;
        var e = _store.Spawn(id, new Vec3d(x + 0.5, y, z + 0.5), EntityTypes.Registry.Get(id).DefaultSpeed);
        if (tag != null) e.Tag = tag;
        if (name != null) e.Set("name", name);
    }
}
