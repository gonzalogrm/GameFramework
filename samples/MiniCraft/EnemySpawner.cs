using GF.Core;
using GF.World;
using GF.World.Entities;
using GF.World.Items;
using GF.World.Map;

namespace MiniCraft;

/// <summary>
/// Enemigos en cada chunk: al cargarse la columna de chunks (una sola vez por columna, y se recuerda) aparecen 0, 1 o más zombis en tierra
/// firme, lejos del jugador. Es determinista: qué columna tiene cuántos y dónde sale de (semilla, columna), no del orden de carga.
/// Cada zombi lleva su botín en su INVENTARIO y su EQUIPO (lo que lleve puesto le protege), y al morir a manos del jugador pasa a él.
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
    private readonly HashSet<long> _populated = new();
    private int _count;

    /// <param name="player">Posición del jugador (en coordenadas de los chunks cargados), o null si aún no se puede aparecer (cargando).</param>
    public EnemySpawner(EntityStore store, World<ushort> world, MiniCraftClimate climate, WorldScale scale, NpcSystem npcs, int seed,
        WorldSettings settings, Func<Vec3d?> player)
    {
        _store = store; _climate = climate; _scale = scale; _npcs = npcs; _seed = seed; _settings = settings; _player = player;
        world.ChunkLoaded += OnChunkLoaded;
        store.Removed += e => { if (e.TypeId == EntityTypes.Zombie) _count--; };
    }

    public int EnemyCount => _count;

    public List<long> ExportPopulated() => _populated.ToList();

    public void ImportPopulated(IEnumerable<long>? keys)
    {
        _populated.Clear();
        if (keys != null) foreach (var k in keys) _populated.Add(k);
    }

    /// <summary>Recuenta los zombis que hay en el almacén (tras importar una partida).</summary>
    public void CountExisting() => _count = _store.All.Count(e => e.TypeId == EntityTypes.Zombie);

    private void OnChunkLoaded(ChunkCoord c)
    {
        if (c.Y != 0 || _settings.EnemiesPerChunk <= 0f) return;   // una vez por columna (el chunk más bajo)
        if (_player() is not { } player) return;

        int cx = _scale.WrapChunkX(c.X);
        long key = ((long)cx << 32) | (uint)c.Z;
        if (!_populated.Add(key)) return;

        var rng = new SplitMixRandom(Hashing.Combine(_seed, cx, 0xE4E3, c.Z));
        float expected = _settings.EnemiesPerChunk;
        int n = (int)expected + (rng.NextFloat() < expected - (int)expected ? 1 : 0);
        int size = _scale.ChunkSizeX;
        var def = EntityTypes.Registry.Get(EntityTypes.Zombie);

        for (int i = 0; i < n && _count < _settings.EnemyLimit; i++)
        {
            int wx = cx * size + rng.Next(size), wz = c.Z * size + rng.Next(size);
            int ground = MiniCraftClimate.ToBlockHeight(_climate.Sample(wx, wz).Height);
            if (ground <= _climate.SeaLevel + _settings.BeachHeight) continue;   // agua y playa: no

            double dx = _scale.DeltaX(player.X, wx + 0.5), dz = wz + 0.5 - player.Z;
            if (dx * dx + dz * dz < SafeDistance * SafeDistance) continue;

            var zombie = _store.Spawn(EntityTypes.Zombie, new Vec3d(wx + 0.5, ground + 2.0, wz + 0.5), def.DefaultSpeed);
            GiveLoot(zombie, rng);
            _npcs.BeginWander(zombie);
            _count++;
        }
    }

    /// <summary>Lo que lleva cada zombi: en el inventario lo que soltará, y en el equipo lo que lleva puesto (y le protege).</summary>
    private static void GiveLoot(Entity zombie, SplitMixRandom rng)
    {
        var items = ItemTypes.Registry;
        if (rng.Chance(0.6f)) zombie.Inventory.Add(items, ItemTypes.Bone, rng.Next(1, 3));
        if (rng.Chance(0.3f)) zombie.Inventory.Add(items, ItemTypes.RottenFlesh, 1);
        if (rng.Chance(0.08f)) zombie.Inventory.Add(items, ItemTypes.IronSword, 1);
        if (rng.Chance(0.15f)) zombie.Equipment.Set(EquipSlot.Head, ItemTypes.LeatherCap);
        if (rng.Chance(0.10f)) zombie.Equipment.Set(EquipSlot.Chest, ItemTypes.LeatherVest);
        if (rng.Chance(0.05f)) zombie.Equipment.Set(EquipSlot.Head, ItemTypes.IronHelmet);
    }
}
