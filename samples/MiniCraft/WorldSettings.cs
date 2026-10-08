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
    public float ContinentSize { get; set; } = 6000f;         // tamaño típico de un continente
    public float MountainSize { get; set; } = 1500f;          // tamaño de las cordilleras
    public float DetailSize { get; set; } = 110f;             // tamaño de las colinas
    public float ContinentAmplitude { get; set; } = 0.25f;    // fracción de la altura
    public float MountainAmplitude { get; set; } = 0.53f;
    public float DetailAmplitude { get; set; } = 0.08f;
    public float LandBias { get; set; } = 0f;                 // >0 más tierra, <0 más océano
    public int ContinentOctaves { get; set; } = 3;
    public int MountainOctaves { get; set; } = 4;
    public int DetailOctaves { get; set; } = 4;
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
    public float TreeDensityMultiplier { get; set; } = 1f;
    public float PropDensityMultiplier { get; set; } = 1f;    // hierba, flores y rocas (sprites)

    // ----- Juego -----
    public int FadeChunks { get; set; } = 2;                  // anchura (chunks) de la franja donde los bloques se disuelven sobre el terreno lejano; 0 = sin fundido
    public float FadeInSeconds { get; set; } = 0.6f;          // un chunk recién construido tarda esto en aparecer (disolviéndose desde nada)
    public int FarCloseChunks { get; set; } = 0;              // el terreno lejano empieza a esta distancia (chunks); 0 = automático: ViewDistance
    public int FarDistanceChunks { get; set; } = 64;          // terreno lejano (LOD) hasta esta distancia, en chunks; 0 = desactivado
    public float FarDetail { get; set; } = 1.6f;              // más = más detalle lejano y más tiles
    public int ViewDistance { get; set; } = 6;                // en chunks
    public int Villagers { get; set; } = 8;
    public int Caravans { get; set; } = 60;
    public int WorkerThreads { get; set; } = 0;               // hilos de fondo para generar y mallar (0 = automático: núcleos - 2)
    public int MapSamplesPerAxis { get; set; } = 6;           // precisión del mapamundi (muestras por región = n*n)

    // ----- Derivados -----
    [JsonIgnore] public int WorldHeight => VerticalChunks * ChunkSize;
    [JsonIgnore] public int SeaLevel => Math.Clamp((int)Math.Round(WorldHeight * SeaLevelFraction), 1, WorldHeight - 1);

    /// <summary>
    /// Copia los ajustes que solo afectan a CÓMO se ve y se rinde el juego (no al terreno generado): distancias de visión y
    /// hilos. Así "Continuar" respeta los valores actuales de worldsettings.json aunque el mundo se creara con otros.
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

        ViewDistance = I(nameof(ViewDistance), ViewDistance, 2, 24);
        FadeChunks = I(nameof(FadeChunks), FadeChunks, 0, 8);
        FadeInSeconds = F(nameof(FadeInSeconds), FadeInSeconds, 0f, 3f);
        FarCloseChunks = I(nameof(FarCloseChunks), FarCloseChunks, 0, 512);
        FarDistanceChunks = I(nameof(FarDistanceChunks), FarDistanceChunks, 0, 512);
        FarDetail = F(nameof(FarDetail), FarDetail, 0.8f, 4f);
        Villagers = I(nameof(Villagers), Villagers, 0, 100);
        Caravans = I(nameof(Caravans), Caravans, 0, 2000);
        MapSamplesPerAxis = I(nameof(MapSamplesPerAxis), MapSamplesPerAxis, 1, 16);
        WorkerThreads = I(nameof(WorkerThreads), WorkerThreads, 0, 64);
        return warnings;
    }
}
