using GF.Core;
using GF.World;
using GF.World.Entities;
using GF.World.Map;

namespace MiniCraft;

/// <summary>
/// Reparte la fauna del planeta (ver Fauna). Al cargarse la columna de chunks se repone hasta el número previsto de animales (WorldSettings.FaunaPerChunk
/// por el factor del bioma): si ya hay los suficientes en la columna (partida guardada) no crea nada. Los animales aparecen en MANADA (hasta HerdMax) en
/// tierra firme, lejos del jugador, y empiezan a vagar. Tope global: FaunaLimit.
/// </summary>
public sealed class FaunaSpawner
{
    private const double SafeDistance = 14.0;

    private readonly EntityStore _store;
    private readonly MiniCraftClimate _climate;
    private readonly WorldScale _scale;
    private readonly NpcSystem _npcs;
    private readonly int _seed;
    private readonly WorldSettings _settings;
    private readonly Func<Vec3d?> _player;
    private int _count = -1;   // se cuenta al cargarse el primer chunk (las entidades guardadas ya están importadas)

    public FaunaSpawner(EntityStore store, World<ushort> world, MiniCraftClimate climate, WorldScale scale, NpcSystem npcs, int seed,
        WorldSettings settings, Func<Vec3d?> player)
    {
        _store = store; _climate = climate; _scale = scale; _npcs = npcs; _seed = seed; _settings = settings; _player = player;
        world.ChunkLoaded += OnChunkLoaded;
        store.Removed += e => { if (IsFauna(e)) _count--; };
    }

    public int FaunaCount => Math.Max(0, _count);

    private static bool IsFauna(Entity e) => e.Def != null && e.Def.Name.StartsWith("fauna_", StringComparison.Ordinal);

    /// <summary>Cuánta fauna admite cada bioma, relativa a FaunaPerChunk.</summary>
    private static float Factor(ushort b)
    {
        if (b == Biomes.Savanna) return 1.4f;
        if (b == Biomes.Jungle) return 1.3f;
        if (b == Biomes.Plains) return 1.2f;
        if (b == Biomes.Forest) return 1.0f;
        if (b == Biomes.Taiga) return 0.8f;
        if (b == Biomes.Tundra || b == Biomes.Mountains) return 0.5f;
        if (b == Biomes.Desert) return 0.4f;
        if (b == Biomes.SnowPeaks) return 0.25f;
        return 0f;   // océano y playa
    }

    private void OnChunkLoaded(ChunkCoord c)
    {
        if (c.Y != 0 || _settings.FaunaPerChunk <= 0f) return;
        var fauna = Fauna.Current;
        if (fauna.Species.Count == 0) return;
        if (_player() is not { } player) return;
        if (_count < 0) _count = _store.All.Count(IsFauna);

        int size = _scale.ChunkSizeX, cx = _scale.WrapChunkX(c.X);
        int x0 = cx * size, z0 = c.Z * size, sea = _climate.SeaLevel, beach = _settings.BeachHeight;
        int existing = _store.InColumn(new ChunkCoord(cx, 0, c.Z)).Count(IsFauna);

        var rng = new SplitMixRandom(Hashing.Combine(_seed, cx, 0xFA17A, c.Z) ^ ((ulong)existing * 0x9E3779B97F4A7C15UL));
        float expected = _settings.FaunaPerChunk * Factor(_climate.Classify(_climate.Sample(x0 + size / 2, z0 + size / 2)));
        int target = (int)expected + (rng.NextFloat() < expected - (int)expected ? 1 : 0);
        int need = target - existing;

        for (int attempt = 0; need > 0 && attempt < 12 && _count < _settings.FaunaLimit; attempt++)
        {
            int wx = x0 + rng.Next(size), wz = z0 + rng.Next(size);
            var sample = _climate.Sample(wx, wz);
            if (MiniCraftClimate.ToBlockHeight(sample.Height) <= sea + beach) continue;   // agua y playa: no

            int index = fauna.PickIndex(_climate.Classify(sample), rng.NextU64());
            if (index < 0) continue;
            double dx = _scale.DeltaX(player.X, wx + 0.5), dz = wz + 0.5 - player.Z;
            if (dx * dx + dz * dz < SafeDistance * SafeDistance) continue;

            var species = fauna.Species[index];
            int herd = 1 + rng.Next(species.HerdMax);
            for (int k = 0; k < herd && need > 0 && _count < _settings.FaunaLimit; k++)
            {
                int px = wx + rng.Next(-3, 4), pz = wz + rng.Next(-3, 4);
                int ground = MiniCraftClimate.ToBlockHeight(_climate.Sample(px, pz).Height);
                if (ground <= sea + beach) continue;
                var animal = _store.Spawn(fauna.TypeIds[index], new Vec3d(px + 0.5, ground + 2.0, pz + 0.5), species.Speed);
                _npcs.BeginWander(animal);
                _count++;
                need--;
            }
        }
    }
}
