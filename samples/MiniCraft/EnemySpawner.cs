using GF.Core;
using GF.World;
using GF.World.Entities;
using GF.World.Map;

namespace MiniCraft;

/// <summary>
/// Enemigos en cada chunk: al cargarse la columna de chunks (una sola vez por columna, y se recuerda) aparecen 0, 1 o más zombis en tierra
/// firme, lejos del jugador. Es determinista: qué columna tiene cuántos y dónde sale de (semilla, columna), no del orden de carga.
/// Cada zombi es de una VARIANTE (elegida al azar con los pesos de entitytypes.json); su inventario y equipamiento vienen de esa variante y
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

    /// <summary>Recuenta los zombis que hay en el almacén (tras importar una partida).</summary>
    public void CountExisting() => _count = _store.All.Count(e => e.TypeId == EntityTypes.Zombie);

    private void OnChunkLoaded(ChunkCoord c)
    {
        if (c.Y != 0 || _settings.EnemiesPerChunk <= 0f) return;   // una vez por columna (el chunk más bajo)
        if (_player() is not { } player) return;

        int cx = _scale.WrapChunkX(c.X);
        if (!_populated.Add((cx, c.Z))) return;

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

            var zombie = _store.Spawn(EntityTypes.Zombie, new Vec3d(wx + 0.5, ground + 2.0, wz + 0.5), def.DefaultSpeed, null, PickVariant(def, rng));
            _npcs.BeginWander(zombie);
            _count++;
        }
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
