using GF.Core;

namespace GF.World.Cosmos;

/// <summary>Cuántas galaxias, sistemas y planetas hay. Forman parte de la identidad del universo (se guardan con él): cambiar un número cambia los IDs.</summary>
public sealed class UniverseOptions
{
    public int GalaxyCount { get; set; } = 10;
    public int SystemsPerGalaxy { get; set; } = 100;
    public int MinPlanets { get; set; } = 1;
    public int MaxPlanets { get; set; } = 5;
}

public enum StarKind { RedDwarf, Orange, Yellow, White, Blue }

public enum PlanetKind { Temperate, Ocean, Desert, Frozen, Jungle, Barren, Lava, Toxic, GasGiant }

/// <summary>
/// Dirección completa de un planeta: los cuatro IDs (universo, galaxia, sistema y planeta). Con ellos se regenera SIEMPRE el mismo planeta y, por
/// tanto, el mismo mundo jugable: Seed y WorldSeed salen solo de estos cuatro números.
/// </summary>
public readonly record struct PlanetAddress(ulong UniverseId, ulong GalaxyId, ulong SystemId, ulong PlanetId)
{
    /// <summary>Semilla de 64 bits del planeta: mezcla ordenada de los cuatro IDs.</summary>
    public ulong Seed => UniverseGenerator.Mix(UniverseGenerator.Mix(UniverseGenerator.Mix(UniverseId, GalaxyId), SystemId), PlanetId);

    /// <summary>Semilla de 32 bits con la que se genera el terreno del mundo (la que usa el generador de chunks).</summary>
    public int WorldSeed => (int)(Seed ^ (Seed >> 32));

    /// <summary>Texto compartible "universo:galaxia:sistema:planeta" en hexadecimal.</summary>
    public override string ToString() => $"{UniverseId:x16}:{GalaxyId:x16}:{SystemId:x16}:{PlanetId:x16}";

    public static bool TryParse(string text, out PlanetAddress address)
    {
        address = default;
        var parts = text.Trim().Split(':');
        if (parts.Length != 4) return false;
        var v = new ulong[4];
        for (int i = 0; i < 4; i++)
            if (!ulong.TryParse(parts[i], System.Globalization.NumberStyles.HexNumber, null, out v[i])) return false;
        address = new PlanetAddress(v[0], v[1], v[2], v[3]);
        return true;
    }
}

/// <summary>
/// Parámetros genéricos de clima y relieve de un planeta habitable. Son números sin más: el juego decide cómo se traducen a sus ajustes de mundo.
/// Warmth/Wetness desplazan temperatura y humedad (-1..1), SeaLevelOffset y LandBias mueven el mar, el resto son multiplicadores (1 = normal).
/// </summary>
public sealed record PlanetClimate(float Warmth, float Wetness, float SeaLevelOffset, float LandBias, float MountainScale, float TreeDensity,
    float ContinentScale);

/// <summary>Un planeta. Todo sale de su dirección (PlanetAddress): no depende del orden en que se generen o se recorran los planetas.</summary>
public sealed class Planet
{
    internal Planet(PlanetAddress address, int index, string name, PlanetKind kind, StarKind star, float radiusEarths, float temperatureC,
        float orbitAu, int moons, PlanetClimate? climate)
    {
        Address = address; Index = index; Name = name; Kind = kind; Star = star; RadiusEarths = radiusEarths;
        TemperatureC = temperatureC; OrbitAu = orbitAu; Moons = moons; Climate = climate;
    }

    public PlanetAddress Address { get; }
    public ulong Id => Address.PlanetId;
    /// <summary>Posición dentro de su sistema (-1 si el planeta se creó solo a partir de su dirección).</summary>
    public int Index { get; }
    public string Name { get; }
    public PlanetKind Kind { get; }
    public StarKind Star { get; }
    public float RadiusEarths { get; }
    public float TemperatureC { get; }
    public float OrbitAu { get; }
    public int Moons { get; }
    /// <summary>Se puede jugar en él (genera un mundo con terreno, biomas y vida). Los gigantes gaseosos, mundos de lava, tóxicos o yermos no.</summary>
    public bool Habitable => Climate != null;
    /// <summary>Clima de un planeta habitable (null si no lo es).</summary>
    public PlanetClimate? Climate { get; }
}

public sealed class SolarSystem
{
    private readonly UniverseOptions _options;
    private IReadOnlyList<Planet>? _planets;

    internal SolarSystem(ulong universeId, ulong galaxyId, int index, ulong id, UniverseOptions options)
    {
        UniverseId = universeId; GalaxyId = galaxyId; Index = index; Id = id; _options = options;
        var rng = new SplitMixRandom(UniverseGenerator.Mix(id, 0x5157E4));
        Name = UniverseGenerator.MakeName(rng);
        Star = UniverseGenerator.StarOf(id);
        X = rng.NextFloat();
        Y = rng.NextFloat();
    }

    public ulong UniverseId { get; }
    public ulong GalaxyId { get; }
    public int Index { get; }
    public ulong Id { get; }
    public string Name { get; }
    public StarKind Star { get; }
    /// <summary>Posición en el mapa de su galaxia (0..1).</summary>
    public float X { get; }
    public float Y { get; }

    /// <summary>Entre MinPlanets y MaxPlanets planetas, cada uno con su ID. Se generan al pedirlos por primera vez.</summary>
    public IReadOnlyList<Planet> Planets => _planets ??= UniverseGenerator.GeneratePlanets(UniverseId, GalaxyId, Id, _options);

    public int HabitableCount => Planets.Count(p => p.Habitable);
}

public sealed class Galaxy
{
    private readonly UniverseOptions _options;
    private IReadOnlyList<SolarSystem>? _systems;

    internal Galaxy(ulong universeId, int index, ulong id, UniverseOptions options)
    {
        UniverseId = universeId; Index = index; Id = id; _options = options;
        Name = UniverseGenerator.MakeName(new SplitMixRandom(UniverseGenerator.Mix(id, 0x6A1A)));
    }

    public ulong UniverseId { get; }
    public int Index { get; }
    public ulong Id { get; }
    public string Name { get; }

    /// <summary>Los sistemas solares de la galaxia, cada uno con su ID. Se generan al pedirlos por primera vez.</summary>
    public IReadOnlyList<SolarSystem> Systems => _systems ??= UniverseGenerator.GenerateSystems(UniverseId, Id, _options);
}

/// <summary>
/// Un universo: una semilla de la que salen, de forma determinista, sus galaxias; de cada galaxia, sus sistemas solares; y de cada sistema, sus
/// planetas. Cada nivel tiene IDs de 64 bits que salen del RNG de su nivel superior, así que con la semilla del universo se puede reconstruir
/// todo, y con los cuatro IDs de un planeta se regenera ese planeta (y su mundo) sin recorrer nada.
/// </summary>
public sealed class Universe
{
    public Universe(ulong id, UniverseOptions? options = null)
    {
        Id = id;
        Options = options ?? new UniverseOptions();
        Galaxies = UniverseGenerator.GenerateGalaxies(id, Options);
    }

    public ulong Id { get; }
    public UniverseOptions Options { get; }
    public IReadOnlyList<Galaxy> Galaxies { get; }

    /// <summary>Busca un planeta por su dirección. null si no pertenece a este universo (IDs que no existen en él).</summary>
    public Planet? Find(PlanetAddress address)
    {
        if (address.UniverseId != Id) return null;
        var galaxy = Galaxies.FirstOrDefault(g => g.Id == address.GalaxyId);
        var system = galaxy?.Systems.FirstOrDefault(s => s.Id == address.SystemId);
        return system?.Planets.FirstOrDefault(p => p.Id == address.PlanetId);
    }

    /// <summary>El primer planeta habitable en orden (galaxia 0, sistema 0, ...): el punto de partida de una partida nueva. Siempre el mismo para un universo.</summary>
    public Planet? FindHome()
    {
        foreach (var galaxy in Galaxies)
            foreach (var system in galaxy.Systems)
                foreach (var planet in system.Planets)
                    if (planet.Habitable) return planet;
        return null;
    }
}
