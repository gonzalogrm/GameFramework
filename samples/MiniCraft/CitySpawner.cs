using GF.Core;
using GF.World;
using GF.World.Entities;
using GF.World.Map;

namespace MiniCraft;

/// <summary>
/// Aldeanos de las ciudades: al cargarse el chunk (columna inferior) que contiene el centro de una ciudad, aparecen unos cuantos
/// aldeanos por sus calles. Sin estado propio: si ya hay aldeanos cerca del centro (partida cargada) no vuelve a crearlos.
/// </summary>
public sealed class CitySpawner
{
    private readonly EntityStore _store;
    private readonly CityPlanner _planner;
    private readonly NpcSystem _npcs;
    private readonly WorldScale _scale;
    private readonly int _villagers;
    private readonly List<Entity> _scratch = new();

    public CitySpawner(EntityStore store, World<ushort> world, CityPlanner planner, NpcSystem npcs, WorldScale scale, int villagers = 6)
    {
        _store = store; _planner = planner; _npcs = npcs; _scale = scale; _villagers = villagers;
        world.ChunkLoaded += OnChunkLoaded;
    }

    private void OnChunkLoaded(ChunkCoord c)
    {
        if (c.Y != 0 || _villagers <= 0) return;
        int size = _scale.ChunkSizeX;
        int cx = _scale.WrapChunkX(c.X);
        var city = _planner.CityAt(cx * size + size / 2, c.Z * size + size / 2);
        if (city == null) return;
        if (city.CenterX < cx * size || city.CenterX >= (cx + 1) * size || city.CenterZ < c.Z * size || city.CenterZ >= (c.Z + 1) * size) return;

        // ¿Ya hay aldeanos aquí (partida guardada)?
        _scratch.Clear();
        _store.QueryRadius(new Vec3d(city.CenterX, city.GroundY, city.CenterZ), city.Radius, _scratch);
        bool present = _scratch.Any(e => e.TypeId == EntityTypes.Villager);
        _scratch.Clear();
        if (present) return;

        var def = EntityTypes.Registry.Get(EntityTypes.Villager);
        var rng = new SplitMixRandom(Hashing.Combine(city.Id, 0xB11A, 0, 0));
        int half = Math.Max(1, city.Radius / 2);
        for (int i = 0; i < _villagers; i++)
        {
            // Por las calles: columnas de calle x = k * Pitch + 1, a lo largo de ±radio/2.
            double x = city.CenterX + CityStage.Pitch * rng.Next(-1, 2) + 1.5;
            double z = city.CenterZ + rng.Next(-half, half) + 0.5;
            var e = _store.Spawn(EntityTypes.Villager, new Vec3d(x, city.GroundY + 2.0, z), def.DefaultSpeed);
            _npcs.BeginWander(e);
        }
    }
}
