using GF.Core;
using GF.World.Map;

namespace MiniCraft;

/// <summary>
/// Modelo de terreno y clima del mundo, controlado por WorldSettings. Relieve: continentes, llanuras, colinas, mesetas, cordilleras súbitas, lagos,
/// ríos y valles (ver Elevation);
/// temperatura por latitud (Z) con ruido y enfriamiento por altitud; humedad por ruido. Todo el ruido es periódico
/// en X, así que el mundo se envuelve sin costura. Es la fuente única de verdad de chunks y mapamundi.
/// </summary>
public sealed class MiniCraftClimate : IClimateModel
{
    private readonly WorldSettings _s;
    private readonly WorldScale _scale;
    private readonly float _period;
    private readonly float _worldHeight, _minH, _maxH, _landRange;
    private readonly WrapNoise _wrap;
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
        Noise = new PerlinNoise(seed);      // temperatura y humedad (distribución de biomas)
        _wrap = new WrapNoise(seed, _period);   // relieve: ruido 2D periódico

        _worldHeight = settings.WorldHeight;
        SeaLevel = settings.SeaLevel;
        _minH = Math.Max(1f, _worldHeight * settings.MinHeightFraction);
        _maxH = Math.Max(_minH + 1f, _worldHeight * settings.MaxHeightFraction);
        _landRange = Math.Max(1f, _worldHeight - SeaLevel);
        _mountainLevel = SeaLevel + (int)MathF.Round(settings.MountainFraction * _landRange);
        _snowLevel = SeaLevel + (int)MathF.Round(settings.SnowPeakFraction * _landRange);
    }

    public static int ToBlockHeight(float h) => (int)MathF.Round(h);

    public ColumnSample Sample(int wx, int wz) => Build(wx, wz, 1);

    /// <summary>
    /// Versión FILTRADA de Sample para el terreno lejano: omite las octavas de ruido cuya longitud de onda es menor que 2 x spacing (filtro paso bajo),
    /// con la misma normalización. Es el MISMO cálculo que Sample (un solo camino), así que el terreno lejano y el cercano coinciden.
    /// </summary>
    public ColumnSample SampleLod(int wx, int wz, int spacing) => Build(wx, wz, Math.Max(1, spacing));

    private ColumnSample Build(int wx, int wz, int spacing)
    {
        float x = wx, z = wz;
        float h = Math.Clamp(Elevation(x, z, spacing), _minH, _maxH);

        float lat = Math.Clamp(z / _scale.HeightBlocks, 0f, 1f);
        float equator = 1f - MathF.Abs(2f * lat - 1f);                                          // 0 polos, 1 ecuador
        float tempNoise = Noise.Fbm2Periodic(x, z + 30000f, _period, 1f / _s.TemperatureNoiseSize, 2) * 0.5f + 0.5f;
        float temp = equator * _s.LatitudeWeight + tempNoise * (1f - _s.LatitudeWeight)
                     - MathF.Max(0f, h - SeaLevel) / _landRange * _s.AltitudeCooling
                     + _s.ClimateWarmth;
        float hum = Noise.Fbm2Periodic(x, z + 45000f, _period, 1f / _s.HumidityNoiseSize, 3) * 0.5f + 0.5f + _s.ClimateWetness;

        return new ColumnSample(h, Math.Clamp(temp, 0f, 1f), Math.Clamp(hum, 0f, 1f), ToBlockHeight(h) <= SeaLevel);
    }

    // ------------------------------------------------------------------ relieve

    private static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Clamp1(float v) => Math.Clamp(v, -1f, 1f);

    // Umbral de la máscara de cordilleras para que cubran la fracción pedida de la tierra (el campo es casi normal, desviación ~0,5).
    private static readonly (float Cover, float Threshold)[] Coverage =
        { (0f, 1.5f), (0.05f, 0.83f), (0.10f, 0.65f), (0.2f, 0.43f), (0.3f, 0.27f), (0.4f, 0.13f), (0.5f, 0f), (0.7f, -0.27f), (1f, -1.5f) };

    private static float CoverageThreshold(float cover)
    {
        for (int i = 1; i < Coverage.Length; i++)
            if (cover <= Coverage[i].Cover)
            {
                var (c0, t0) = Coverage[i - 1]; var (c1, t1) = Coverage[i];
                return t0 + (t1 - t0) * (cover - c0) / (c1 - c0);
            }
        return Coverage[^1].Threshold;
    }

    /// <summary>Terrazas escalonadas: sube a la siguiente grada con un escalón corto.</summary>
    private static float Terrace(float e, float step)
    {
        float q = MathF.Floor(e / step), f = e / step - q;
        return (q + Smooth(0.35f, 0.65f, f)) * step;
    }

    /// <summary>
    /// Altura del suelo en bloques. Capas, en este ORDEN: continentes -> llanura ondulada -> colinas por regiones -> mesetas (saltos bruscos) ->
    /// cordilleras (máscara alargada y muy deformada que aparece de golpe, con crestas y bordes irregulares) -> terrazas -> detalle -> lagos ->
    /// RÍOS Y VALLES al final, tallados sobre el relieve ya hecho. Los ríos se apartan de las cordilleras (se estrechan y se extinguen antes de llegar a su
    /// pie) en vez de ser cortados por ellas. 'spacing' > 1 filtra las octavas que no se pueden representar (terreno lejano).
    /// </summary>
    private float Elevation(float x, float z, int spacing)
    {
        var s = _s; var n = _wrap;
        float S = SeaLevel, H = _worldHeight;
        int U(float wl, int oct) => UsableOctaves(wl, oct, spacing);

        // continentes: mar o tierra, con costa suave
        float L = 3f * n.Fbm(x, z, s.ContinentSize, s.ContinentOctaves, 1, 1f, U(s.ContinentSize, s.ContinentOctaves)) + s.LandBias;
        float land = Smooth(-0.08f, 0.12f, L);
        float inland = Smooth(0.1f, 0.9f, L);

        // llanura con ondulación suave de gran escala
        float roll = Clamp1(3.3f * n.Fbm(x + 5000f, z, 900f, 2, 2, 1f, U(900f, 2)));
        float e = S + 2f + 5f * inland + 2.5f * roll * land;

        // colinas, solo en ciertas regiones
        float hr = 3.2f * n.Fbm(x + 9000f, z, s.HillRegionSize, 2, 3, 1f, U(s.HillRegionSize, 2));
        float hillMask = Smooth(-0.1f, 0.4f, hr) * land;
        float hills = Clamp1(3.3f * n.Fbm(x + 3000f, z, s.HillSize, 4, 4, 1f, U(s.HillSize, 4)));
        e += hillMask * s.HillAmplitude * H * (0.45f + 0.55f * hills);

        // mesetas: salto brusco (acantilado) y cima plana
        float pl = 3.2f * n.Fbm(x + 14000f, z, 1800f, 2, 5, 1f, U(1800f, 2));
        e += Smooth(0.55f, 0.62f, pl) * land * s.PlateauHeight;

        // cordilleras: doble domain warping (uno amplio y sinuoso y otro fino y nervioso), dos orientaciones alargadas y bordes ruidosos
        float w1 = s.RangeWarp, w2 = 0.3f * s.RangeWarp;
        float wx = x + w1 * 3.3f * n.Fbm(x + 2000f, z, 2600f, 2, 6, 1f, U(2600f, 2)) + w2 * 3.3f * n.Fbm(x + 6000f, z + 300f, 520f, 2, 7, 1f, U(520f, 2));
        float wz = z + w1 * 3.3f * n.Fbm(x + 4000f, z + 900f, 2600f, 2, 8, 1f, U(2600f, 2)) + w2 * 3.3f * n.Fbm(x + 8000f, z + 1300f, 520f, 2, 9, 1f, U(520f, 2));

        float rangeWl = s.MountainSize * 0.5f;
        float rmA = 3.2f * n.Fbm(wx + 7000f, wz, rangeWl, 2, 10, 3f, U(rangeWl, 2));        // alargada en Z
        float rmB = 3.2f * n.Fbm(wx + 9000f, wz, rangeWl, 2, 11, 0.33f, U(rangeWl, 2));     // alargada en X
        float edge = 0.35f * 3.3f * n.Fbm(wx, wz, 260f, 3, 12, 1f, U(260f, 3));              // bordes irregulares: rompe las manchas redondas
        float rm = MathF.Max(rmA, rmB) + edge;
        float thr = CoverageThreshold(s.RangeCoverage);
        float rangeMask = Smooth(thr, thr + s.RangeSharpness, rm) * Smooth(0f, 0.25f, L);
        float core = Smooth(thr, thr + 0.5f, rm);
        float crestWl = s.MountainSize / 4.5f;
        float crest = 0.8f * n.Ridged(wx, wz, crestWl, s.MountainOctaves, 14, 1f, U(crestWl, s.MountainOctaves))
                      + 0.2f * n.Ridged(x, z, 150f, 3, 15, 1f, U(150f, 3));                  // picos y aristas finas
        e += s.MountainAmplitude * _landRange * (rangeMask * 0.30f + core * rangeMask * 0.70f * MathF.Pow(crest, 1.2f));

        // terrazas en algunas regiones
        float mesa = Smooth(0.2f, 0.6f, 3.2f * n.Fbm(x + 11000f, z, 2200f, 2, 13, 1f, U(2200f, 2))) * land;
        e += (Terrace(e - S, 7f) + S - e) * s.Terracing * mesa;

        // detalle ANTES de los ríos (el cauce queda limpio), más suave dentro de las cordilleras
        e += s.DetailAmplitude * H * Clamp1(3.3f * n.Fbm(x, z, s.DetailSize, s.DetailOctaves, 16, 1f, U(s.DetailSize, s.DetailOctaves)))
             * (0.5f + 0.5f * (1f - rangeMask));

        // lagos redondos en las llanuras
        float lk = 3.2f * n.Fbm(x + 23000f, z, 1300f, 2, 17, 1f, U(1300f, 2));
        float lakeT = 0.72f - 0.10f * s.Lakes;
        float lake = Smooth(lakeT, lakeT + 0.04f, lk) * land * (1f - Smooth(0.1f, 0.5f, rangeMask)) * (1f - Smooth(0.3f, 0.6f, hillMask));
        e += (MathF.Min(e, S - 5f) - e) * lake;

        // ríos y valles: AL FINAL, sobre el relieve ya hecho. Cerca de una cordillera la distancia al río crece hasta anularlo antes de llegar al pie
        float rwx = x + 300f * 3.3f * n.Fbm(x + 13000f, z, 1500f, 2, 18, 1f, U(1500f, 2));
        float rwz = z + 300f * 3.3f * n.Fbm(x + 17000f, z + 5000f, 1500f, 2, 19, 1f, U(1500f, 2));
        float rn = MathF.Abs(n.Fbm(rwx + 21000f, rwz, s.RiverSpacing, 2, 20, 1f, U(s.RiverSpacing, 2)));
        float proximity = Smooth(thr - 0.6f, thr, rm);
        float dist = rn * 900f + proximity * proximity * 220f;
        float half = MathF.Max(s.RiverWidth * 0.5f, spacing);                 // con muestras espaciadas el cauce no baja de una muestra de ancho
        float valley = MathF.Max(s.ValleyWidth, half * 3f);
        float t = Math.Clamp((dist - half) / (valley - half), 0f, 1f);
        float floor = S - 3f + 17f * (1f - (1f - t) * (1f - t));
        e += (MathF.Min(e, floor) - e) * s.ValleyDepth * land;

        // el mar: el fondo baja con la lejanía a la costa
        float ocean = S - 3f - s.ContinentAmplitude * H * Smooth(-0.1f, -0.7f, L);
        return ocean + (e - ocean) * land;
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
