namespace GF.Core;

public static class Hashing
{
    private const ulong Gamma = 0x9E3779B97F4A7C15UL;

    public static ulong Mix(ulong z)
    {
        z += Gamma;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Hash determinista de (seed, x, y, z). Base del RNG por chunk.</summary>
    public static ulong Combine(int seed, int x, int y, int z)
    {
        ulong h = Mix((ulong)(uint)seed);
        h = Mix(h + (ulong)(uint)x);
        h = Mix(h + (ulong)(uint)y);
        h = Mix(h + (ulong)(uint)z);
        return h;
    }

    /// <summary>Hash a float en [0, 1) usando sus 24 bits altos. Es el (h >> 40) * (1f / (1 << 24)) que se repetía por todo el código.</summary>
    public static float Unit(ulong hash) => (hash >> 40) * (1f / (1 << 24));

    /// <summary>Igual que Unit pero en double (mismos 24 bits: mismos resultados que el (h >> 40) * (1.0 / (1 << 24)) de ciudades y ruinas).</summary>
    public static double UnitD(ulong hash) => (hash >> 40) * (1.0 / (1 << 24));
}

public interface IRandom
{
    int Next(int max);            // [0, max)
    int Next(int min, int max);   // [min, max)
    float NextFloat();            // [0, 1)
    bool Chance(float probability);
}

public sealed class SplitMixRandom : IRandom
{
    private ulong _state;
    public SplitMixRandom(ulong seed) => _state = seed;

    public ulong NextU64()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public int Next(int max) => max <= 0 ? 0 : (int)(NextU64() % (ulong)max);
    public int Next(int min, int max) => min + Next(max - min);
    public float NextFloat() => Hashing.Unit(NextU64());
    public bool Chance(float probability) => NextFloat() < probability;
}
