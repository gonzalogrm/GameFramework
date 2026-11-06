using System.Text;
using GF.Engine;
using GF.World.Cosmos;

namespace MiniCraft;

/// <summary>
/// La partida a nivel de universo: el universo (de su semilla salen galaxias, sistemas y planetas), el planeta donde se está jugando y los ajustes base
/// de worldsettings.json. Cada planeta habitable es un mundo aparte con su propia carpeta de guardado.
/// </summary>
public sealed class UniverseSession
{
    public UniverseSession(Universe universe, WorldSettings baseSettings, PlanetAddress? current = null)
    {
        Universe = universe;
        BaseSettings = baseSettings;
        Current = current;
    }

    public Universe Universe { get; }
    public WorldSettings BaseSettings { get; }
    /// <summary>Planeta donde se juega ahora (el último al que se viajó).</summary>
    public PlanetAddress? Current { get; private set; }

    /// <summary>Universo nuevo. Sus opciones (galaxias, sistemas por galaxia, planetas por sistema) salen de worldsettings.json y se guardan con él.</summary>
    public static UniverseSession Create(ulong seed, WorldSettings baseSettings) => new(new Universe(seed, new UniverseOptions
    {
        GalaxyCount = baseSettings.Galaxies, SystemsPerGalaxy = baseSettings.SystemsPerGalaxy,
        MinPlanets = baseSettings.MinPlanets, MaxPlanets = Math.Max(baseSettings.MinPlanets, baseSettings.MaxPlanets),
    }), baseSettings);

    /// <summary>Retoma el universo guardado, o null si no hay (o no se puede leer).</summary>
    public static UniverseSession? TryContinue(WorldSettings baseSettings)
    {
        var save = SaveSystem.TryLoadUniverse();
        return save == null ? null : new UniverseSession(new Universe(save.Seed, save.Options), baseSettings, save.Current);
    }

    public UniverseSave ToSave() => new() { Seed = Universe.Id, Options = Universe.Options, Current = Current };

    /// <summary>La escena de juego del planeta guardado como actual (o la del planeta de inicio si no hay ninguno válido).</summary>
    public PlayScene ContinueScene()
    {
        var planet = (Current is { } a ? Universe.Find(a) : null);
        if (planet is not { Habitable: true }) planet = Universe.FindHome()
            ?? throw new InvalidOperationException("Este universo no tiene ningún planeta habitable.");
        return CreateScene(planet);
    }

    /// <summary>
    /// La escena de juego de un planeta habitable. Si ya hay mundo guardado se retoma con SUS ajustes; si no, se genera de cero: la semilla del terreno
    /// y el clima salen solo de la dirección del planeta (universo, galaxia, sistema y planeta), así que los mismos IDs dan siempre el mismo mundo.
    /// </summary>
    public PlayScene CreateScene(Planet planet)
    {
        if (!planet.Habitable) throw new ArgumentException("El planeta no es habitable.", nameof(planet));
        Current = planet.Address;
        Biomes.ApplyPlanet(planet.Address.Seed);                           // materiales (suelo, roca, madera...) de este planeta
        Flora.ApplyPlanet(planet.Address.Seed);                            // especies de árboles y arbustos de este planeta
        PlanetPalette.Current = PlanetPalette.For(planet.Address.Seed);   // paleta alienígena de este planeta (atlas y terreno lejano)
        Fauna.ApplyPlanet(planet.Address.Seed);                            // especies de animales de este planeta (se registran como tipos de entidad)
        FloraSprites.ApplyPlanet(planet.Address.Seed);                     // qué sprites de flora crecen en cada bioma
        SpriteLoader.PixelFilter = PlanetSprites.Recolor;                  // hierba, hierba alta y matorral toman el verde del planeta
        long spriteSeed = unchecked((long)planet.Address.Seed);
        SpriteLoader.Extras = builder => PlanetSprites.AddAll(builder, unchecked((ulong)spriteSeed));   // sprites de flora propios del planeta
        string dir = SaveSystem.PlanetDir(planet.Address);
        SaveSystem.UseDirectory(dir);
        SaveSystem.WriteUniverse(ToSave());

        var save = SaveSystem.TryLoad();
        if (save != null)
        {
            save.Settings.ApplyViewSettingsFrom(BaseSettings);   // visión y rendimiento: los de worldsettings.json actual
            return new PlayScene(save.Seed, save.Settings, save, this, planet, dir);
        }
        return new PlayScene(planet.Address.WorldSeed, PlanetWorlds.CreateSettings(BaseSettings, planet), null, this, planet, dir);
    }

    /// <summary>Semilla de universo a partir de lo que escribe el jugador: vacío = aleatoria; un número = ese número; cualquier otro texto = su hash.</summary>
    public static ulong ParseSeed(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return (ulong)Random.Shared.NextInt64();
        if (ulong.TryParse(text, out ulong n)) return n;
        ulong h = 14695981039346656037UL;   // FNV-1a de 64 bits
        foreach (byte b in Encoding.UTF8.GetBytes(text)) { h ^= b; h *= 1099511628211UL; }
        return h;
    }
}
