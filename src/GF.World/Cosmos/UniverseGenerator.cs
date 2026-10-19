using GF.Core;

namespace GF.World.Cosmos;

/// <summary>
/// Generación determinista del universo. Todo sale de RNGs (SplitMixRandom) sembrados con IDs: la semilla del universo da los IDs de las galaxias; el
/// ID de una galaxia da los de sus sistemas; el de un sistema, los de sus planetas; y la dirección completa de un planeta (los cuatro IDs) da sus
/// propiedades. Mismos IDs = mismo resultado, siempre y en cualquier orden.
/// </summary>
public static class UniverseGenerator
{
    /// <summary>Mezcla ordenada de dos valores de 64 bits (avalancha de SplitMix64): Mix(a, b) != Mix(b, a).</summary>
    public static ulong Mix(ulong a, ulong b)
    {
        ulong z = a + 0x9E3779B97F4A7C15UL * (b + 1);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    internal static IReadOnlyList<Galaxy> GenerateGalaxies(ulong universeId, UniverseOptions options)
    {
        var rng = new SplitMixRandom(Mix(universeId, 0xCA1A71E5));
        var list = new List<Galaxy>(options.GalaxyCount);
        for (int i = 0; i < options.GalaxyCount; i++) list.Add(new Galaxy(universeId, i, rng.NextU64(), options));
        return list;
    }

    internal static IReadOnlyList<SolarSystem> GenerateSystems(ulong universeId, ulong galaxyId, UniverseOptions options)
    {
        var rng = new SplitMixRandom(Mix(galaxyId, 0x5E57E3));
        var list = new List<SolarSystem>(options.SystemsPerGalaxy);
        for (int i = 0; i < options.SystemsPerGalaxy; i++) list.Add(new SolarSystem(universeId, galaxyId, i, rng.NextU64(), options));
        return list;
    }

    internal static IReadOnlyList<Planet> GeneratePlanets(ulong universeId, ulong galaxyId, ulong systemId, UniverseOptions options)
    {
        var rng = new SplitMixRandom(Mix(systemId, 0x91A4E7));
        int count = rng.Next(Math.Max(1, options.MinPlanets), Math.Max(options.MinPlanets, options.MaxPlanets) + 1);
        var list = new List<Planet>(count);
        for (int i = 0; i < count; i++) list.Add(CreatePlanet(new PlanetAddress(universeId, galaxyId, systemId, rng.NextU64()), i));
        return list;
    }

    public static StarKind StarOf(ulong systemId)
    {
        int roll = new SplitMixRandom(Mix(systemId, 0x57A2)).Next(100);
        return roll < 40 ? StarKind.RedDwarf : roll < 60 ? StarKind.Orange : roll < 85 ? StarKind.Yellow : roll < 95 ? StarKind.White : StarKind.Blue;
    }

    // Tipos de planeta y su probabilidad relativa. Los cinco primeros son habitables (40 %).
    private static readonly (PlanetKind Kind, int Weight)[] Kinds =
    {
        (PlanetKind.Temperate, 12), (PlanetKind.Ocean, 8), (PlanetKind.Desert, 8), (PlanetKind.Frozen, 7), (PlanetKind.Jungle, 5),
        (PlanetKind.Barren, 20), (PlanetKind.Lava, 8), (PlanetKind.Toxic, 10), (PlanetKind.GasGiant, 22),
    };

    /// <summary>
    /// Crea un planeta SOLO a partir de su dirección (los cuatro IDs): sus propiedades y, si es habitable, su clima. Es lo que permite regenerar un
    /// mundo sabiendo únicamente esos IDs. 'index' es su posición en el sistema, si se conoce (solo informativa).
    /// </summary>
    public static Planet CreatePlanet(PlanetAddress address, int index = -1)
    {
        var rng = new SplitMixRandom(address.Seed);
        var star = StarOf(address.SystemId);

        int total = Kinds.Sum(k => k.Weight), roll = rng.Next(total);
        var kind = Kinds[0].Kind;
        foreach (var (k, w) in Kinds) { roll -= w; if (roll < 0) { kind = k; break; } }

        string name = MakeName(rng);
        float radius = kind == PlanetKind.GasGiant ? 5f + rng.NextFloat() * 8f : 0.3f + rng.NextFloat() * 2.2f;
        float orbit = 0.1f + rng.NextFloat() * 8f;
        int moons = kind == PlanetKind.GasGiant ? rng.Next(2, 9) : rng.Next(0, 4);
        float t = rng.NextFloat();
        float temp = kind switch
        {
            PlanetKind.Temperate => 5f + t * 23f,
            PlanetKind.Ocean => 10f + t * 20f,
            PlanetKind.Desert => 30f + t * 30f,
            PlanetKind.Frozen => -60f + t * 55f,
            PlanetKind.Jungle => 25f + t * 15f,
            PlanetKind.Barren => -90f + t * 210f,
            PlanetKind.Lava => 300f + t * 600f,
            PlanetKind.Toxic => 40f + t * 160f,
            _ => -200f + t * 100f,
        };

        PlanetClimate? climate = null;
        if (kind is PlanetKind.Temperate or PlanetKind.Ocean or PlanetKind.Desert or PlanetKind.Frozen or PlanetKind.Jungle)
        {
            // Variación propia de cada mundo (sale del mismo RNG: mismo planeta, mismo clima).
            float jitter(float amount) => (rng.NextFloat() * 2f - 1f) * amount;
            climate = kind switch
            {
                PlanetKind.Ocean => new PlanetClimate(jitter(0.1f), 0.2f + jitter(0.1f), 0.07f + jitter(0.03f), -0.25f + jitter(0.05f),
                    0.8f + jitter(0.2f), 1f, 1f + jitter(0.2f)),
                PlanetKind.Desert => new PlanetClimate(0.35f + jitter(0.1f), -0.55f + jitter(0.1f), jitter(0.04f), 0.1f + jitter(0.05f),
                    1f + jitter(0.3f), 0.2f, 1f + jitter(0.3f)),
                PlanetKind.Frozen => new PlanetClimate(-0.4f + jitter(0.1f), -0.1f + jitter(0.1f), jitter(0.04f), jitter(0.1f),
                    1.2f + jitter(0.3f), 0.6f, 1f + jitter(0.3f)),
                PlanetKind.Jungle => new PlanetClimate(0.15f + jitter(0.1f), 0.5f + jitter(0.1f), jitter(0.04f), jitter(0.1f),
                    0.9f + jitter(0.2f), 1.6f, 1f + jitter(0.3f)),
                _ => new PlanetClimate(jitter(0.1f), jitter(0.1f), jitter(0.04f), jitter(0.1f), 1f + jitter(0.3f), 1f, 1f + jitter(0.3f)),
            };
        }
        return new Planet(address, index, name, kind, star, radius, temp, orbit, moons, climate);
    }

    private static readonly string[] Onsets = { "ka", "ve", "lo", "mi", "ra", "su", "ta", "ze", "no", "fa", "ri", "do", "ba", "he", "lu", "pa", "xe", "yo", "dra", "tor", "vel", "kor", "sha" };
    private static readonly string[] Codas = { "", "n", "r", "s", "x", "th", "l", "m" };

    /// <summary>Nombre pronunciable (2 o 3 sílabas) a partir de un RNG.</summary>
    public static string MakeName(SplitMixRandom rng)
    {
        int syllables = rng.Next(2, 4);
        var name = new System.Text.StringBuilder();
        for (int i = 0; i < syllables; i++) name.Append(Onsets[rng.Next(Onsets.Length)]);
        name.Append(Codas[rng.Next(Codas.Length)]);
        name[0] = char.ToUpperInvariant(name[0]);
        return name.ToString();
    }
}
