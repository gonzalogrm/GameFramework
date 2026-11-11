using System.Text.Json;
using System.Text.Json.Serialization;
using GF.World.Map;

namespace MiniCraft;

/// <summary>
/// Todos los parámetros del mundo en un único sitio. Se leen de worldsettings.json (junto al ejecutable).
/// Al crear un mundo NUEVO se guarda una copia dentro de su world.json: "Continuar" usa siempre la copia guardada,
/// porque cambiar el terreno de un mundo ya generado haría que los chunks nuevos no encajasen con los viejos.
/// Los tamaños de ruido y las distancias están en BLOQUES; las amplitudes, en fracción de la altura del mundo.
/// </summary>
public sealed class WorldSettings
{
    // ----- Escala del mundo -----
    public int MapWidth { get; set; } = 200;          // regiones en X (el mundo se envuelve)
    public int MapHeight { get; set; } = 50;          // regiones en Z (de polo a polo)
    public int RegionChunksX { get; set; } = 32;      // chunks por región, en X
    public int RegionChunksZ { get; set; } = 32;
    public int ChunkSize { get; set; } = 16;          // 8, 16 o 32 bloques de lado
    public int VerticalChunks { get; set; } = 4;      // chunks en vertical: altura = VerticalChunks * ChunkSize

    // ----- Relieve -----
    public float SeaLevelFraction { get; set; } = 0.5f;       // nivel del mar, fracción de la altura
    public float ContinentSize { get; set; } = 24000f;         // tamaño típico de un continente
    public float MountainSize { get; set; } = 3500f;          // tamaño de las cordilleras
    public float DetailSize { get; set; } = 80f;             // tamaño de las colinas
    public float ContinentAmplitude { get; set; } = 0.43f;    // fracción de la altura
    public float MountainAmplitude { get; set; } = 1.0f;
    public float DetailAmplitude { get; set; } = 0.02f;
    public float LandBias { get; set; } = 0.1f;                 // >0 más tierra, <0 más océano
    public int ContinentOctaves { get; set; } = 3;
    public int MountainOctaves { get; set; } = 5;
    public int DetailOctaves { get; set; } = 3;

    // ----- Relieve: llanuras, colinas, cordilleras súbitas, mesetas, lagos, ríos y valles -----
    public float RangeCoverage { get; set; } = 0.15f;         // fracción de la tierra cubierta por cordilleras (el resto: llanuras, colinas, mesetas)
    public float RangeSharpness { get; set; } = 0.10f;        // brusquedad del pie de las cordilleras: menor = más súbito
    public float RangeWarp { get; set; } = 700f;              // deformación (domain warping) de las cordilleras, en bloques: más = más sinuosas
    public float HillSize { get; set; } = 450f;               // tamaño de las colinas
    public float HillAmplitude { get; set; } = 0.12f;         // altura de las colinas (fracción de la altura del mundo)
    public float HillRegionSize { get; set; } = 2800f;        // tamaño de las regiones con colinas (entre ellas, llanura)
    public float PlateauHeight { get; set; } = 10f;           // salto de las mesetas (acantilados), en bloques
    public float Terracing { get; set; } = 0.35f;             // 0..1: terrazas escalonadas en algunas regiones
    public float RiverSpacing { get; set; } = 2200f;          // separación típica de los ríos
    public float RiverWidth { get; set; } = 14f;              // anchura del cauce, en bloques
    public float ValleyWidth { get; set; } = 80f;             // anchura del valle a cada lado del río
    public float ValleyDepth { get; set; } = 0.85f;           // 0..1: cuánto baja el terreno hacia el río
    public float Lakes { get; set; } = 0.4f;                  // 0..1: cantidad de lagos en las llanuras
    public float MinHeightFraction { get; set; } = 0.05f;     // fondo marino mínimo
    public float MaxHeightFraction { get; set; } = 0.90f;     // cumbre máxima

    // ----- Clima y biomas -----
    public float TemperatureNoiseSize { get; set; } = 4000f;
    public float HumidityNoiseSize { get; set; } = 3500f;
    public float LatitudeWeight { get; set; } = 0.8f;         // cuánto manda la latitud en la temperatura
    public float AltitudeCooling { get; set; } = 0.4f;        // enfriamiento por altura
    public float ClimateWarmth { get; set; } = 0f;            // desplaza todas las temperaturas
    public float ClimateWetness { get; set; } = 0f;           // desplaza toda la humedad
    public int BeachHeight { get; set; } = 2;                 // bloques de playa sobre el mar
    public float MountainFraction { get; set; } = 0.44f;      // desde qué fracción de la tierra hay montañas
    public float SnowPeakFraction { get; set; } = 0.62f;      // y cumbres nevadas
    public float TreeDensityMultiplier { get; set; } = 1f;    // se guarda con el mundo (cada planeta lo modifica según su clima)
    public float PropDensityMultiplier { get; set; } = 1f;    // hierba, flores y rocas (sprites)
    public WeatherSettings Weather { get; set; } = new();   // nubes, estrellas y cielo (se leen siempre del json actual)
    public VegetationSettings Vegetation { get; set; } = new();   // multiplicadores por separado de árboles, arbustos, hierbas, hongos, rocas y cristales (se aplican siempre)

    // ----- Vegetación y formaciones (se aplican SIEMPRE con el worldsettings.json actual, también al continuar un mundo: no cambian el terreno) -----
    public float FaunaPerChunk { get; set; } = 1f;                // 0..4: animales esperados por columna de chunks al cargarse (0 = sin fauna); depende del bioma
    public int FaunaLimit { get; set; } = 400;                    // máximo de animales en el mundo
    public float CaveDensity { get; set; } = 1f;                  // 0..2: cuevas (túneles y cavernas subterráneas, con cristales y hongos); 0 = sin cuevas
    public float MysteryDensity { get; set; } = 1f;               // 0..3: lugares misteriosos (sumideros, círculos de monolitos, pilonos, arboledas de cristal); 0 = ninguno

    // ----- Juego -----
    public CitySettings Cities { get; set; } = new();          // ciudades: frecuencia, tamano, guardas, NPC especiales
    public RuinSettings Ruins { get; set; } = new();     // ruinas y campos de estatuas: frecuencia, reparto, tamano, enemigos, cofres
    public int FadeChunks { get; set; } = 2;                  // anchura (chunks) de la franja donde los bloques se disuelven sobre el terreno lejano; 0 = sin fundido
    public float FadeInSeconds { get; set; } = 0.6f;          // un chunk recién construido tarda esto en aparecer (disolviéndose desde nada)
    public int FarCloseChunks { get; set; } = 0;              // el terreno lejano empieza a esta distancia (chunks); 0 = automático: ViewDistance
    public int FarDistanceChunks { get; set; } = 64;          // terreno lejano (LOD) hasta esta distancia, en chunks; 0 = desactivado
    public float FarDetail { get; set; } = 1.6f;              // más = más detalle lejano y más tiles
    public int ViewDistance { get; set; } = 6;                // en chunks
    public int Villagers { get; set; } = 8;
    public int Caravans { get; set; } = 60;
    public float EnemiesPerChunk { get; set; } = 0.4f;        // zombis esperados por columna de chunks al cargarse (0 = sin enemigos)
    public int Galaxies { get; set; } = 10;                   // UNIVERSO NUEVO: galaxias (se guardan con el universo; cambiarlas cambia los IDs)
    public int SystemsPerGalaxy { get; set; } = 100;          // sistemas solares por galaxia
    public int MinPlanets { get; set; } = 1;                  // planetas por sistema solar: entre estos dos valores
    public int MaxPlanets { get; set; } = 5;
    public int EnemyLimit { get; set; } = 200;                // máximo de zombis en el mundo (para no acumular al explorar)
    public int WorkerThreads { get; set; } = 0;               // hilos de fondo para generar y mallar (0 = automático: núcleos - 2)
    public int MapSamplesPerAxis { get; set; } = 6;           // precisión del mapamundi (muestras por región = n*n)

    /// <summary>Copia independiente de estos ajustes (para derivar los de cada planeta del universo).</summary>
    public WorldSettings Clone() => JsonSerializer.Deserialize<WorldSettings>(JsonSerializer.Serialize(this))!;

    // ----- Derivados -----
    [JsonIgnore] public int WorldHeight => VerticalChunks * ChunkSize;
    [JsonIgnore] public int SeaLevel => Math.Clamp((int)Math.Round(WorldHeight * SeaLevelFraction), 1, WorldHeight - 1);

    /// <summary>
    /// Copia los ajustes que solo afectan a CÓMO se ve y se rinde el juego (no al terreno generado): distancias de visión y
    /// hilos, y la cantidad de vegetación y formaciones. Así "Continuar" respeta los valores actuales de worldsettings.json aunque el mundo se creara con otros.
    /// </summary>
    public void ApplyViewSettingsFrom(WorldSettings other)
    {
        ViewDistance = other.ViewDistance;
        FadeChunks = other.FadeChunks;
        FadeInSeconds = other.FadeInSeconds;
        FarCloseChunks = other.FarCloseChunks;
        FarDistanceChunks = other.FarDistanceChunks;
        FarDetail = other.FarDetail;
        WorkerThreads = other.WorkerThreads;
        EnemiesPerChunk = other.EnemiesPerChunk;
        EnemyLimit = other.EnemyLimit;
        Galaxies = other.Galaxies;
        SystemsPerGalaxy = other.SystemsPerGalaxy;
        MinPlanets = other.MinPlanets;
        MaxPlanets = other.MaxPlanets;
        PropDensityMultiplier = other.PropDensityMultiplier;
        FaunaPerChunk = other.FaunaPerChunk;
        FaunaLimit = other.FaunaLimit;
        CaveDensity = other.CaveDensity;
        MysteryDensity = other.MysteryDensity;
        Cities.Villagers = other.Cities.Villagers;
        Cities.Guards = other.Cities.Guards;
        Cities.SpecialNpcs = other.Cities.SpecialNpcs;
        Ruins.ApplyView(other.Ruins);
        Vegetation.CopyFrom(other.Vegetation);
    }

    public WorldScale CreateScale() => new(MapWidth, MapHeight, RegionChunksX, RegionChunksZ, ChunkSize, ChunkSize);

    // ----- Carga -----
    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Lee worldsettings.json de la carpeta del ejecutable. Si no existe o es inválido, usa los valores por defecto.</summary>
    public static WorldSettings Load(out List<string> messages, out string source)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "worldsettings.json");
        WorldSettings settings;
        messages = new List<string>();
        source = path;

        if (!File.Exists(path))
        {
            settings = new WorldSettings();
            source = "valores por defecto (no hay worldsettings.json)";
        }
        else
        {
            try { settings = JsonSerializer.Deserialize<WorldSettings>(File.ReadAllText(path), Options) ?? new WorldSettings(); }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                settings = new WorldSettings();
                messages.Add("worldsettings.json no es valido (" + e.Message + "); se usan los valores por defecto");
            }
        }
        messages.AddRange(settings.Validate());
        return settings;
    }

    /// <summary>Limita cada parámetro a un rango seguro y devuelve un aviso por cada ajuste.</summary>
    public List<string> Validate()
    {
        var warnings = new List<string>();

        int I(string name, int v, int min, int max)
        {
            int c = Math.Clamp(v, min, max);
            if (c != v) warnings.Add($"{name}: {v} fuera de [{min}, {max}], se usa {c}");
            return c;
        }
        float F(string name, float v, float min, float max)
        {
            float c = float.IsNaN(v) ? min : Math.Clamp(v, min, max);
            if (c != v) warnings.Add($"{name}: {v} fuera de [{min}, {max}], se usa {c}");
            return c;
        }

        MapWidth = I(nameof(MapWidth), MapWidth, 8, 2000);
        MapHeight = I(nameof(MapHeight), MapHeight, 4, 500);
        RegionChunksX = I(nameof(RegionChunksX), RegionChunksX, 1, 256);
        RegionChunksZ = I(nameof(RegionChunksZ), RegionChunksZ, 1, 256);
        if (ChunkSize is not (8 or 16 or 32)) { warnings.Add($"ChunkSize: {ChunkSize} no valido (8, 16 o 32), se usa 16"); ChunkSize = 16; }
        VerticalChunks = I(nameof(VerticalChunks), VerticalChunks, 2, 32);

        SeaLevelFraction = F(nameof(SeaLevelFraction), SeaLevelFraction, 0.1f, 0.9f);
        ContinentSize = F(nameof(ContinentSize), ContinentSize, 500f, 100_000f);
        MountainSize = F(nameof(MountainSize), MountainSize, 200f, 50_000f);
        DetailSize = F(nameof(DetailSize), DetailSize, 10f, 2_000f);
        ContinentAmplitude = F(nameof(ContinentAmplitude), ContinentAmplitude, 0f, 1f);
        MountainAmplitude = F(nameof(MountainAmplitude), MountainAmplitude, 0f, 1.5f);
        DetailAmplitude = F(nameof(DetailAmplitude), DetailAmplitude, 0f, 0.5f);
        LandBias = F(nameof(LandBias), LandBias, -0.5f, 0.5f);
        ContinentOctaves = I(nameof(ContinentOctaves), ContinentOctaves, 1, 8);
        MountainOctaves = I(nameof(MountainOctaves), MountainOctaves, 1, 8);
        DetailOctaves = I(nameof(DetailOctaves), DetailOctaves, 1, 8);
        RangeCoverage = F(nameof(RangeCoverage), RangeCoverage, 0f, 0.6f);
        RangeSharpness = F(nameof(RangeSharpness), RangeSharpness, 0.02f, 0.6f);
        RangeWarp = F(nameof(RangeWarp), RangeWarp, 0f, 3000f);
        HillSize = F(nameof(HillSize), HillSize, 50f, 5000f);
        HillAmplitude = F(nameof(HillAmplitude), HillAmplitude, 0f, 0.5f);
        HillRegionSize = F(nameof(HillRegionSize), HillRegionSize, 200f, 20_000f);
        PlateauHeight = F(nameof(PlateauHeight), PlateauHeight, 0f, 40f);
        Terracing = F(nameof(Terracing), Terracing, 0f, 1f);
        RiverSpacing = F(nameof(RiverSpacing), RiverSpacing, 300f, 20_000f);
        RiverWidth = F(nameof(RiverWidth), RiverWidth, 2f, 80f);
        ValleyWidth = F(nameof(ValleyWidth), ValleyWidth, 10f, 400f);
        ValleyDepth = F(nameof(ValleyDepth), ValleyDepth, 0f, 1f);
        Lakes = F(nameof(Lakes), Lakes, 0f, 1f);
        MinHeightFraction = F(nameof(MinHeightFraction), MinHeightFraction, 0f, 0.5f);
        MaxHeightFraction = F(nameof(MaxHeightFraction), MaxHeightFraction, 0.5f, 1f);
        if (MaxHeightFraction < SeaLevelFraction + 0.1f)
        {
            MaxHeightFraction = Math.Min(1f, SeaLevelFraction + 0.1f);
            warnings.Add($"MaxHeightFraction debe superar el nivel del mar; se usa {MaxHeightFraction}");
        }

        TemperatureNoiseSize = F(nameof(TemperatureNoiseSize), TemperatureNoiseSize, 500f, 100_000f);
        HumidityNoiseSize = F(nameof(HumidityNoiseSize), HumidityNoiseSize, 500f, 100_000f);
        LatitudeWeight = F(nameof(LatitudeWeight), LatitudeWeight, 0f, 1f);
        AltitudeCooling = F(nameof(AltitudeCooling), AltitudeCooling, 0f, 1f);
        ClimateWarmth = F(nameof(ClimateWarmth), ClimateWarmth, -0.5f, 0.5f);
        ClimateWetness = F(nameof(ClimateWetness), ClimateWetness, -0.5f, 0.5f);
        BeachHeight = I(nameof(BeachHeight), BeachHeight, 0, 6);
        MountainFraction = F(nameof(MountainFraction), MountainFraction, 0f, 1f);
        SnowPeakFraction = F(nameof(SnowPeakFraction), SnowPeakFraction, 0f, 1f);
        if (SnowPeakFraction < MountainFraction)
        {
            SnowPeakFraction = MountainFraction;
            warnings.Add("SnowPeakFraction no puede ser menor que MountainFraction; se iguala");
        }
        TreeDensityMultiplier = F(nameof(TreeDensityMultiplier), TreeDensityMultiplier, 0f, 5f);
        PropDensityMultiplier = F(nameof(PropDensityMultiplier), PropDensityMultiplier, 0f, 5f);
        FaunaPerChunk = F(nameof(FaunaPerChunk), FaunaPerChunk, 0f, 4f);
        FaunaLimit = I(nameof(FaunaLimit), FaunaLimit, 0, 5000);
        CaveDensity = F(nameof(CaveDensity), CaveDensity, 0f, 2f);
        MysteryDensity = F(nameof(MysteryDensity), MysteryDensity, 0f, 3f);

        ViewDistance = I(nameof(ViewDistance), ViewDistance, 2, 24);
        FadeChunks = I(nameof(FadeChunks), FadeChunks, 0, 8);
        FadeInSeconds = F(nameof(FadeInSeconds), FadeInSeconds, 0f, 3f);
        FarCloseChunks = I(nameof(FarCloseChunks), FarCloseChunks, 0, 512);
        FarDistanceChunks = I(nameof(FarDistanceChunks), FarDistanceChunks, 0, 512);
        FarDetail = F(nameof(FarDetail), FarDetail, 0.8f, 4f);
        Villagers = I(nameof(Villagers), Villagers, 0, 100);
        Caravans = I(nameof(Caravans), Caravans, 0, 2000);
        EnemiesPerChunk = F(nameof(EnemiesPerChunk), EnemiesPerChunk, 0f, 4f);
        EnemyLimit = I(nameof(EnemyLimit), EnemyLimit, 0, 5000);
        Galaxies = I(nameof(Galaxies), Galaxies, 1, 100);
        SystemsPerGalaxy = I(nameof(SystemsPerGalaxy), SystemsPerGalaxy, 1, 1000);
        MinPlanets = I(nameof(MinPlanets), MinPlanets, 1, 12);
        MaxPlanets = I(nameof(MaxPlanets), MaxPlanets, 1, 12);
        MapSamplesPerAxis = I(nameof(MapSamplesPerAxis), MapSamplesPerAxis, 1, 16);
        WorkerThreads = I(nameof(WorkerThreads), WorkerThreads, 0, 64);
        Cities.Validate(warnings);
        Ruins.Validate(warnings);
        Vegetation.Validate(warnings);
        Weather.Validate(warnings);
        return warnings;
    }
}
