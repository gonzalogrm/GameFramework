namespace GF.Core;

/// <summary>Ruido coherente en [-1, 1]. Las implementaciones deben ser inmutables (thread-safe).</summary>
public interface INoise
{
    float Sample(float x, float y);
    float Sample(float x, float y, float z);
}

public sealed class PerlinNoise : INoise
{
    private readonly int[] _p = new int[512];

    public PerlinNoise(int seed)
    {
        var perm = Enumerable.Range(0, 256).ToArray();
        var rng = new SplitMixRandom((ulong)(uint)seed ^ 0xA5A5A5A5UL);
        for (int i = 255; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (perm[i], perm[j]) = (perm[j], perm[i]);
        }
        for (int i = 0; i < 512; i++) _p[i] = perm[i & 255];
    }

    public float Sample(float x, float y) => Sample(x, y, 0f);

    public float Sample(float x, float y, float z)
    {
        int xi = IntMath.FloorToInt(x), yi = IntMath.FloorToInt(y), zi = IntMath.FloorToInt(z);
        float xf = x - xi, yf = y - yi, zf = z - zi;
        int X = xi & 255, Y = yi & 255, Z = zi & 255;
        float u = Fade(xf), v = Fade(yf), w = Fade(zf);

        int A = _p[X] + Y, AA = _p[A] + Z, AB = _p[A + 1] + Z;
        int B = _p[X + 1] + Y, BA = _p[B] + Z, BB = _p[B + 1] + Z;

        return Lerp(
            Lerp(Lerp(Grad(_p[AA], xf, yf, zf), Grad(_p[BA], xf - 1, yf, zf), u),
                 Lerp(Grad(_p[AB], xf, yf - 1, zf), Grad(_p[BB], xf - 1, yf - 1, zf), u), v),
            Lerp(Lerp(Grad(_p[AA + 1], xf, yf, zf - 1), Grad(_p[BA + 1], xf - 1, yf, zf - 1), u),
                 Lerp(Grad(_p[AB + 1], xf, yf - 1, zf - 1), Grad(_p[BB + 1], xf - 1, yf - 1, zf - 1), u), v),
            w);
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
    private static float Lerp(float a, float b, float t) => a + t * (b - a);

    private static float Grad(int hash, float x, float y, float z)
    {
        int h = hash & 15;
        float u = h < 8 ? x : y;
        float v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }
}

public static class NoiseExtensions
{
    /// <summary>
    /// Fbm 2D periódico en X con periodo periodX (mundo cilíndrico: este y oeste se conectan sin costura).
    /// Muestrea el ruido 3D sobre un cilindro, así que el resultado es idéntico en x y x + periodX.
    /// frequency = 1 / tamaño típico de la característica, en bloques.
    /// </summary>
    public static float Fbm2Periodic(this INoise n, float x, float z, float periodX, float frequency,
        int octaves = 4, float lacunarity = 2f, float gain = 0.5f)
    {
        double u = x / (double)periodX;
        u -= Math.Floor(u);
        double theta = 2.0 * Math.PI * u;
        float cos = (float)Math.Cos(theta), sin = (float)Math.Sin(theta);

        float sum = 0, amp = 1, freq = frequency, norm = 0;
        for (int i = 0; i < octaves; i++)
        {
            float r = periodX * freq / (2f * MathF.PI);   // radio del cilindro para esta octava
            sum += n.Sample(r * cos, r * sin, z * freq) * amp;
            norm += amp; amp *= gain; freq *= lacunarity;
        }
        return sum / norm;
    }

    public static float Fbm2(this INoise n, float x, float y, int octaves = 4, float lacunarity = 2f, float gain = 0.5f)
    {
        float sum = 0, amp = 1, freq = 1, norm = 0;
        for (int i = 0; i < octaves; i++)
        {
            sum += n.Sample(x * freq, y * freq) * amp;
            norm += amp; amp *= gain; freq *= lacunarity;
        }
        return sum / norm;
    }

    public static float Fbm3(this INoise n, float x, float y, float z, int octaves = 4, float lacunarity = 2f, float gain = 0.5f)
    {
        float sum = 0, amp = 1, freq = 1, norm = 0;
        for (int i = 0; i < octaves; i++)
        {
            sum += n.Sample(x * freq, y * freq, z * freq) * amp;
            norm += amp; amp *= gain; freq *= lacunarity;
        }
        return sum / norm;
    }
}
