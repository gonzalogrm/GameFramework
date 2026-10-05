using GF.Core;
using GF.Engine;
using GF.World;
using GF.World.Entities;
using GF.World.Map;
using GF.World.Voxel;
using Microsoft.Xna.Framework;

namespace MiniCraft;

/// <summary>
/// IA de los NPC. Las entidades activas (chunks cargados) caminan con física voxel; las aproximadas las mueve el
/// EntityStore en línea recta. Aquí solo hay lógica de juego: elegir destinos, y la física de las activas.
/// Las activas se simulan en coordenadas LOCALES al jugador (sin envolver), porque son las de los chunks cargados.
/// </summary>
public sealed class NpcSystem
{
    private sealed class NpcState
    {
        public Vector3 Vel;
        public bool OnGround;
        public bool NeedsUnstuck = true;
    }

    private const double MaxActiveSpeed = 2.0;   // por encima, la física por pasos atravesaría bloques

    private static readonly string[] VillagerNames =
        { "Roser", "Ovejo", "Cona", "Manek", "Hernan", "Pablo", "Oriol", "Carmen", "Pazaco" };
    private static readonly string[] DogNames = { "Pixel", "Lola", "Toby", "Nala", "Thor", "Luna", "Bruno", "Kira" };

    private readonly EntityStore _store;
    private readonly World<ushort> _world;
    private readonly MiniCraftClimate _climate;
    private readonly WorldScale _scale;
    private readonly Func<World<MapCell>?> _map;
    private readonly Random _rng;
    private readonly List<Entity> _scratch = new();

    public NpcSystem(EntityStore store, World<ushort> world, MiniCraftClimate climate, WorldScale scale,
        Func<World<MapCell>?> map, int seed)
    {
        _store = store; _world = world; _climate = climate; _scale = scale; _map = map;
        _rng = new Random(seed ^ 0x4E50C);
        store.Activated += OnActivated;
        store.Deactivated += e => e.Data = null;
        store.Arrived += OnArrived;
    }

    // ------------------------------------------------------------------ eventos del almacén

    private void OnActivated(Entity e)
    {
        e.Data = new NpcState();
        // La posición Y de una entidad aproximada es solo una interpolación: al activarse, a la altura del suelo.
        _store.Move(e, new Vec3d(e.Position.X, GroundY(e.Position.X, e.Position.Z), e.Position.Z));
    }

    private void OnArrived(Entity e)
    {
        if (e.TypeId == EntityTypes.Caravan && TryPickLandDestination(e.Position, 8, out var dest))
            _store.SetDestination(e, dest);
        else
            _store.SetDestination(e, WanderTarget(e.Position));
    }

    private double GroundY(double x, double z)
    {
        int h = MiniCraftClimate.ToBlockHeight(_climate.Sample((int)Math.Floor(x), (int)Math.Floor(z)).Height);
        return Math.Max(h, _climate.SeaLevel) + 2.0;
    }

    private Vec3d WanderTarget(Vec3d from)
    {
        double angle = _rng.NextDouble() * Math.PI * 2, r = 8 + _rng.NextDouble() * 32;
        double x = from.X + Math.Cos(angle) * r, z = from.Z + Math.Sin(angle) * r;
        return new Vec3d(x, GroundY(x, z), z);
    }

    private static bool IsLand(MapCell c) =>
        c.WaterFraction < 0.3f && c.Temperature > 0.15f && c.Biome != Biomes.Mountains && c.Biome != Biomes.SnowPeaks;

    /// <summary>Elige el centro (con algo de dispersión) de una región de tierra cercana del mapamundi.</summary>
    private bool TryPickLandDestination(Vec3d from, int regionRadius, out Vec3d destination)
    {
        destination = default;
        var map = _map();
        if (map == null) return false;

        var (rx, rz) = _scale.RegionOf((int)Math.Floor(from.X), (int)Math.Floor(from.Z));
        for (int attempt = 0; attempt < 20; attempt++)
        {
            int nz = rz + _rng.Next(-regionRadius, regionRadius + 1);
            if (nz < 0 || nz >= _scale.MapHeight) continue;
            int nx = ((rx + _rng.Next(-regionRadius, regionRadius + 1)) % _scale.MapWidth + _scale.MapWidth) % _scale.MapWidth;
            if (!IsLand(map.GetCell(new CellCoord(nx, nz, 0)))) continue;

            var c = _scale.RegionCenter(nx, nz);
            double x = c.X + _rng.Next(-200, 201), z = c.Z + _rng.Next(-200, 201);
            destination = new Vec3d(x, GroundY(x, z), z);
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ población inicial

    /// <summary>Aldeanos alrededor del jugador y caravanas repartidas por tierras del mapa (cantidades en WorldSettings).</summary>
    public void SpawnInitial(Vec3d player)
    {
        var villager = EntityTypes.Registry.Get(EntityTypes.Villager);
        for (int i = 0; i < _climate.Settings.Villagers; i++)
        {
            double angle = _rng.NextDouble() * Math.PI * 2, r = 6 + _rng.NextDouble() * 20;
            double x = player.X + Math.Cos(angle) * r, z = player.Z + Math.Sin(angle) * r;
            var pos = new Vec3d(x, GroundY(x, z), z);
            var e = _store.Spawn(EntityTypes.Villager, pos, villager.DefaultSpeed);
            e.Set("name", VillagerNames[_rng.Next(VillagerNames.Length)]);   // lo único propio de cada aldeano; el resto, del prototipo
            _store.SetDestination(e, WanderTarget(pos));
        }

        var dog = EntityTypes.Registry.Get(EntityTypes.Dog);
        for (int i = 0; i < 4; i++)
        {
            double angle = _rng.NextDouble() * Math.PI * 2, r = 4 + _rng.NextDouble() * 12;
            double x = player.X + Math.Cos(angle) * r, z = player.Z + Math.Sin(angle) * r;
            var pos = new Vec3d(x, GroundY(x, z), z);
            var e = _store.Spawn(EntityTypes.Dog, pos, dog.DefaultSpeed);
            e.Set("name", DogNames[_rng.Next(DogNames.Length)]);
            _store.SetDestination(e, WanderTarget(pos));
        }

        var map = _map();
        if (map == null) return;
        var caravan = EntityTypes.Registry.Get(EntityTypes.Caravan);
        int spawned = 0;
        for (int tries = 0; tries < 3000 && spawned < _climate.Settings.Caravans; tries++)
        {
            int rx = _rng.Next(_scale.MapWidth), rz = _rng.Next(_scale.MapHeight);
            if (!IsLand(map.GetCell(new CellCoord(rx, rz, 0)))) continue;

            var c = _scale.RegionCenter(rx, rz);
            double x = c.X + _rng.Next(-200, 201), z = c.Z + _rng.Next(-200, 201);
            var pos = new Vec3d(x, GroundY(x, z), z);
            if (!TryPickLandDestination(pos, 8, out var dest)) continue;

            var e = _store.Spawn(EntityTypes.Caravan, pos, caravan.DefaultSpeed);
            _store.SetDestination(e, dest);
            spawned++;
        }
    }

    // ------------------------------------------------------------------ física de las activas

    public void Update(float dt, Vec3d player)
    {
        _scratch.Clear();
        _scratch.AddRange(_store.Active);   // copia: Move/NotifyArrived pueden cambiar el conjunto

        foreach (var e in _scratch)
        {
            if (e.Tier != SimulationTier.Active) continue;
            var state = e.Data as NpcState;
            if (state == null) { state = new NpcState(); e.Data = state; }

            var def = EntityTypes.Registry.Get(e.TypeId);
            var size = new Vector3(def.Width, def.Height, def.Width);

            // A coordenadas locales al jugador (las de los chunks cargados, sin envolver).
            var local = new Vec3d(player.X + _scale.DeltaX(player.X, e.Position.X), e.Position.Y, e.Position.Z);
            if (!ColumnLoaded(local)) continue;

            if (state.NeedsUnstuck)
            {
                for (int i = 0; i < 64 && VoxelPhysics.Overlaps(_world, Blocks.Registry, local, size); i++)
                    local = local with { Y = local.Y + 1.0 };
                state.NeedsUnstuck = false;
            }

            float wishX = 0, wishZ = 0;
            if (e.Destination is { } d)
            {
                double dx = _scale.DeltaX(local.X, d.X), dz = d.Z - local.Z;
                double dist = Math.Sqrt(dx * dx + dz * dz);
                if (dist < 1.5) _store.NotifyArrived(e);
                else
                {
                    double speed = Math.Min(e.Speed, MaxActiveSpeed);
                    wishX = (float)(dx / dist * speed);
                    wishZ = (float)(dz / dist * speed);
                }
            }

            state.Vel.X = wishX;
            state.Vel.Z = wishZ;
            state.Vel.Y -= 28f * dt;

            var delta = new Vec3d(state.Vel.X * dt, state.Vel.Y * dt, state.Vel.Z * dt);
            local = VoxelPhysics.MoveAndCollide(_world, Blocks.Registry, local, size, delta, out var flags);
            state.OnGround = (flags & CollisionFlags.Ground) != 0;
            if ((flags & CollisionFlags.Y) != 0) state.Vel.Y = 0;
            if ((flags & (CollisionFlags.X | CollisionFlags.Z)) != 0 && state.OnGround) state.Vel.Y = 8f;   // saltar obstáculos

            _store.Move(e, local);   // vuelve a coordenadas canónicas
        }
        _scratch.Clear();
    }

    private bool ColumnLoaded(Vec3d local)
    {
        var cc = _world.Shape.ToChunk(new CellCoord((int)Math.Floor(local.X), 0, (int)Math.Floor(local.Z)));
        for (int cy = 0; cy < _climate.Settings.VerticalChunks; cy++)
            if (_world.GetChunk(new ChunkCoord(cc.X, cy, cc.Z)) == null) return false;
        return true;
    }
}
