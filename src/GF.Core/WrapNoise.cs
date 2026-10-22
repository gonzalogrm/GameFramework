namespace GF.Core;

/// <summary>
/// Ruido de gradiente 2D PERIÓDICO en X (el mundo se envuelve sin costura) y mucho más barato que el 3D sobre un cilindro: en cada octava el
/// periodo contiene un número ENTERO de celdas, y la retícula se envuelve con un módulo. Devuelve valores en torno a [-1, 1] (la desviación típica es
/// baja, ~0.15 en fbm: el llamador lo escala). Determinista por semilla, sin estado mutable: seguro desde varios hilos.
/// 'aspect' alarga el ruido: es la longitud de onda en Z dividida por la de X (3 = alargado en Z, 0,33 = alargado en X), lo que da cordilleras largas
/// en vez de manchas redondas.
/// </summary>
public sealed class WrapNoise
{
    private readonly uint _seed;
    private readonly double _period;

    public WrapNoise(int seed, float periodX)
    {
        _seed = unchecked((uint)seed * 0x9E3779B1u);
        _period = Math.Max(1.0, periodX);
    }

    public float Perlin(float x, float z, float wavelength, int layer, float aspect = 1f)
    {
        float wx = aspect >= 1f ? wavelength : wavelength / aspect;
        float wz = aspect >= 1f ? wavelength * aspect : wavelength;
        int cells = Math.Max(1, (int)Math.Round(_period / wx));
        double csx = _period / cells, csz = csx * (wz / wx);

        double u = x / csx, v = z / csz;
        int iu = (int)Math.Floor(u), iv = (int)Math.Floor(v);
        float fx = (float)(u - iu), fz = (float)(v - iv);
        int i0 = iu % cells; if (i0 < 0) i0 += cells;
        int i1 = i0 + 1 == cells ? 0 : i0 + 1;

        uint seed = unchecked(_seed + (uint)layer * 0x85EBCA6Bu);
        float n00 = Grad(i0, iv, fx, fz, seed);
        float n10 = Grad(i1, iv, fx - 1f, fz, seed);
        float n01 = Grad(i0, iv + 1, fx, fz - 1f, seed);
        float n11 = Grad(i1, iv + 1, fx - 1f, fz - 1f, seed);
        float sx = Fade(fx), sz = Fade(fz);
        float a = n00 + (n10 - n00) * sx;
        float b = n01 + (n11 - n01) * sx;
        return (a + (b - a) * sz) * 0.8f;
    }

    /// <summary>fBm (cada octava, la mitad de longitud de onda y de amplitud). Solo suma las primeras 'usedOctaves' (filtro paso bajo para el terreno lejano) pero normaliza como si estuvieran todas.</summary>
    public float Fbm(float x, float z, float wavelength, int octaves, int layer, float aspect = 1f, int usedOctaves = -1)
    {
        int used = usedOctaves < 0 ? octaves : Math.Min(octaves, usedOctaves);
        float total = 0f, amp = 1f, norm = 0f, w = wavelength;
        for (int o = 0; o < octaves; o++)
        {
            if (o < used) total += amp * Perlin(x, z, w, layer * 16 + o, aspect);
            norm += amp;
            amp *= 0.5f;
            w *= 0.5f;
        }
        return total / norm;
    }

    /// <summary>Ruido de crestas (0..1): 1 - |ruido| elevado al cuadrado, con las octavas finas ponderadas por la señal de la anterior (valles suaves, picos agudos).</summary>
    public float Ridged(float x, float z, float wavelength, int octaves, int layer, float aspect = 1f, int usedOctaves = -1)
    {
        int used = usedOctaves < 0 ? octaves : Math.Min(octaves, usedOctaves);
        float total = 0f, amp = 1f, norm = 0f, w = wavelength, weight = 1f;
        for (int o = 0; o < octaves; o++)
        {
            if (o < used)
            {
                float r = 1f - MathF.Abs(Perlin(x, z, w, layer * 16 + o, aspect));
                r *= r;
                r *= weight;
                weight = Math.Clamp(r * 2f, 0f, 1f);
                total += amp * r;
            }
            norm += amp;
            amp *= 0.5f;
            w *= 0.5f;
        }
        return total / norm;
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    private static float Grad(int ix, int iz, float dx, float dz, uint seed)
    {
        uint h = seed ^ unchecked((uint)ix * 0x27D4EB2Du) ^ unchecked((uint)iz * 0x165667B1u);
        h ^= h >> 15; h = unchecked(h * 0x2C1B3C6Du);
        h ^= h >> 12; h = unchecked(h * 0x297A2D39u);
        h ^= h >> 15;
        return (h & 7) switch
        {
            0 => dx + dz,
            1 => -dx + dz,
            2 => dx - dz,
            3 => -dx - dz,
            4 => dx,
            5 => -dx,
            6 => dz,
            _ => -dz,
        };
    }
}
