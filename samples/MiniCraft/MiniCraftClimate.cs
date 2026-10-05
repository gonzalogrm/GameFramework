using GF.Core;
using GF.World.Map;

namespace MiniCraft;

/// <summary>
/// Modelo de terreno y clima del mundo, controlado por WorldSettings. Altura = continentes + cordilleras + detalle;
/// temperatura por latitud (Z) con ruido y enfriamiento por altitud; humedad por ruido. Todo el ruido es periódico
/// en X, así que el mundo se envuelve sin costura. Es la fuente única de verdad de chunks y mapamundi.
/// </summary>
public sealed class MiniCraftClimate : IClimateModel
{
    private readonly WorldSettings _s;
    private readonly WorldScale _scale;
    private readonly float _period;
    private readonly float _worldHeight, _minH, _maxH, _landRange;
    private readonly int _mountainLevel, _snowLevel;

    public INoise Noise { get; }
    public WorldSettings Settings => _s;
    public int SeaLevel { get; }
    /// <summary>Altura máxima del suelo donde aún caben un tronco y una copa.</summary>
    public int MaxTreeGround => _s.WorldHeight - 14;

    public MiniCraftClimate(int seed, WorldSettings settings, WorldScale scale)
    {
        _s = settings;
        _scale = scale;
        _period = scale.WidthBlocks;
        Noise = new PerlinNoise(seed);

        _worldHeight = settings.WorldHeight;
        SeaLevel = settings.SeaLevel;
        _minH = Math.Max(1f, _worldHeight * settings.MinHeightFraction);
        _maxH = Math.Max(_minH + 1f, _worldHeight * settings.MaxHeightFraction);
        _landRange = Math.Max(1f, _worldHeight - SeaLevel);
        _mountainLevel = SeaLevel + (int)MathF.Round(settings.MountainFraction * _landRange);
        _snowLevel = SeaLevel + (int)MathF.Round(settings.SnowPeakFraction * _landRange);
    }

    public static int ToBlockHeight(float h) => (int)MathF.Round(h);

    public ColumnSample Sample(int wx, int wz)
    {
        float x = wx, z = wz;

        float continent = Noise.Fbm2Periodic(x, z, _period, 1f / _s.ContinentSize, _s.ContinentOctaves) + _s.LandBias;
        float ridge = Noise.Fbm2Periodic(x, z + 7000f, _period, 1f / _s.MountainSize, _s.MountainOctaves) * 0.5f + 0.5f;   // 0..1
        float detail = Noise.Fbm2Periodic(x, z + 15000f, _period, 1f / _s.DetailSize, _s.DetailOctaves);

        float h = SeaLevel
                  + continent * _s.ContinentAmplitude * _worldHeight
                  + MathF.Max(0f, continent - 0.1f) * ridge * _s.MountainAmplitude * _worldHeight
                  + detail * _s.DetailAmplitude * _worldHeight;
        h = Math.Clamp(h, _minH, _maxH);

        float lat = Math.Clamp(z / _scale.HeightBlocks, 0f, 1f);
        float equator = 1f - MathF.Abs(2f * lat - 1f);                                          // 0 polos, 1 ecuador
        float tempNoise = Noise.Fbm2Periodic(x, z + 30000f, _period, 1f / _s.TemperatureNoiseSize, 2) * 0.5f + 0.5f;
        float temp = equator * _s.LatitudeWeight + tempNoise * (1f - _s.LatitudeWeight)
                     - MathF.Max(0f, h - SeaLevel) / _landRange * _s.AltitudeCooling
                     + _s.ClimateWarmth;
        float hum = Noise.Fbm2Periodic(x, z + 45000f, _period, 1f / _s.HumidityNoiseSize, 3) * 0.5f + 0.5f + _s.ClimateWetness;

        return new ColumnSample(h, Math.Clamp(temp, 0f, 1f), Math.Clamp(hum, 0f, 1f), ToBlockHeight(h) <= SeaLevel);
    }

    /// <summary>
    /// Versión FILTRADA de Sample para el terreno lejano: omite las octavas de ruido cuya longitud de onda es menor que 2 x spacing
    /// (filtro paso bajo), con la misma normalización que Sample. Así continentes y cordilleras se ven igual a cualquier distancia,
    /// y las colinas pequeñas solo aparecen cuando la separación entre muestras es lo bastante fina para representarlas.
    /// Mantener las fórmulas en línea con Sample.
    /// </summary>
    public ColumnSample SampleLod(int wx, int wz, int spacing)
    {
        float x = wx, z = wz;
        int continentOctaves = UsableOctaves(_s.ContinentSize, _s.ContinentOctaves, spacing);
        int mountainOctaves = UsableOctaves(_s.MountainSize, _s.MountainOctaves, spacing);
        int detailOctaves = UsableOctaves(_s.DetailSize, _s.DetailOctaves, spacing);

        float continent = Noise.Fbm2PeriodicBandLimited(x, z, _period, 1f / _s.ContinentSize, _s.ContinentOctaves, continentOctaves) + _s.LandBias;
        float ridge = Noise.Fbm2PeriodicBandLimited(x, z + 7000f, _period, 1f / _s.MountainSize, _s.MountainOctaves, mountainOctaves) * 0.5f + 0.5f;
        float detail = Noise.Fbm2PeriodicBandLimited(x, z + 15000f, _period, 1f / _s.DetailSize, _s.DetailOctaves, detailOctaves);

        float h = SeaLevel
                  + continent * _s.ContinentAmplitude * _worldHeight
                  + MathF.Max(0f, continent - 0.1f) * ridge * _s.MountainAmplitude * _worldHeight
                  + detail * _s.DetailAmplitude * _worldHeight;
        h = Math.Clamp(h, _minH, _maxH);

        float lat = Math.Clamp(z / _scale.HeightBlocks, 0f, 1f);
        float equator = 1f - MathF.Abs(2f * lat - 1f);
        float tempNoise = Noise.Fbm2Periodic(x, z + 30000f, _period, 1f / _s.TemperatureNoiseSize, 2) * 0.5f + 0.5f;
        float temp = equator * _s.LatitudeWeight + tempNoise * (1f - _s.LatitudeWeight)
                     - MathF.Max(0f, h - SeaLevel) / _landRange * _s.AltitudeCooling
                     + _s.ClimateWarmth;
        float hum = Noise.Fbm2Periodic(x, z + 45000f, _period, 1f / _s.HumidityNoiseSize, 3) * 0.5f + 0.5f + _s.ClimateWetness;

        return new ColumnSample(h, Math.Clamp(temp, 0f, 1f), Math.Clamp(hum, 0f, 1f), ToBlockHeight(h) <= SeaLevel);
    }

    /// <summary>Octavas (de las 'octaves' totales) cuya longitud de onda alcanza a 2 muestras: las únicas que se pueden representar sin aliasing.</summary>
    private static int UsableOctaves(float size, int octaves, int spacing)
    {
        int used = 0;
        for (int i = 0; i < octaves && size / (1 << i) >= 2f * spacing; i++) used++;
        return used;
    }

    /// <summary>Clasificación Whittaker simplificada. La usan el generador de chunks y el mapa.</summary>
    public ushort Classify(ColumnSample s)
    {
        int h = ToBlockHeight(s.Height);
        if (h <= SeaLevel) return Biomes.Ocean;
        if (h <= SeaLevel + _s.BeachHeight) return s.Temperature < 0.12f ? Biomes.Tundra : Biomes.Beach;
        if (h >= _snowLevel) return Biomes.SnowPeaks;
        if (h >= _mountainLevel) return Biomes.Mountains;

        float t = s.Temperature, m = s.Humidity;
        if (t < 0.2f) return Biomes.Tundra;
        if (t < 0.4f) return m > 0.45f ? Biomes.Taiga : Biomes.Tundra;
        if (t < 0.7f) return m > 0.55f ? Biomes.Forest : Biomes.Plains;
        return m < 0.35f ? Biomes.Desert : m < 0.6f ? Biomes.Savanna : Biomes.Jungle;
    }
}
