using System.Collections.Concurrent;
using GF.Core;
using GF.World.Map;

namespace MiniCraft;

/// <summary>
/// Regiones de campos de estatuas y urnas: en cada celda de 4096 bloques puede haber un disco de 350 a 750 bloques de radio (hasta 1,5 km de diámetro) que cae
/// en TIERRA (Setup) y donde el terreno llano se llena de estatuas, urnas, tótems, agujas retorcidas y mausoleos. A diferencia del resto de ruinas no caben en una celda de
/// ciudad: se generan por regiones (StatueFieldStage) y son solo una definición determinista (semilla, celda).
/// </summary>
public static class StatueFields
{
    public const int Cell = 4096;

    /// <summary>Kind: 0 campo de estatuas, 1 campo de urnas, 2 mixto.</summary>
    public sealed record Region(int Id, int X, int Z, int Radius, int Kind);

    private static readonly ConcurrentDictionary<(int, int, int), Region?> Cache = new();

    public static string KindName(int kind) => kind == 0 ? "campo de estatuas" : kind == 1 ? "campo de urnas" : "campo de estatuas y urnas";

    public static Region? Get(int seed, int cx, int cz) => Cache.GetOrAdd((seed, cx, cz), k => Make(k.Item1, k.Item2, k.Item3));

    private static Func<int, int, int>? _height;
    private static int _sea;
    private static float _chance = 0.55f;
    private static int _minRadius = 350, _maxRadius = 750;

    /// <summary>
    /// Hay que llamarla al abrir el mundo, antes de generar: da la altura del terreno en una columna (un muestreo del clima, sin generar chunks) y el nivel del mar,
    /// para que las regiones solo caigan en tierra. Sin ella no se comprueba nada.
    /// </summary>
    public static void Setup(Func<int, int, int> heightAt, int seaLevel, float chance = 0.55f, int minRadius = 350, int maxRadius = 750)
    {
        _height = heightAt;
        _sea = seaLevel;
        _chance = chance;
        _minRadius = minRadius;
        _maxRadius = Math.Max(minRadius, maxRadius);
        Cache.Clear();
    }

    private static bool IsLand(int x, int z) => _height == null || _height(x, z) > _sea + 2;

    /// <summary>Centro y 8 puntos a 0,65 del radio: al menos el 80 % (8 de 9) tiene que ser tierra.</summary>
    private static bool MostlyLand(int x, int z, int radius)
    {
        if (_height == null) return true;
        if (!IsLand(x, z)) return false;
        int land = 1;
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            if (IsLand(x + (int)(Math.Cos(a) * radius * 0.65), z + (int)(Math.Sin(a) * radius * 0.65))) land++;
        }
        return land >= 8;
    }

    private static Region? Make(int seed, int cx, int cz)
    {
        var rng = new SplitMixRandom(Hashing.Combine(seed, cx, 0xF1E1D, cz));
        if (!rng.Chance(_chance)) return null;
        int kind = rng.Next(3);
        for (int attempt = 0; attempt < 24; attempt++)
        {
            int radius = rng.Next(_minRadius, _maxRadius + 1), margin = radius + 16, span = Cell - 2 * margin;
            int x = cx * Cell + margin + rng.Next(span), z = cz * Cell + margin + rng.Next(span);
            if (!MostlyLand(x, z, radius)) continue;   // nada de campos en mitad del mar
            ulong h = Hashing.Combine(seed, cx, 0xF1E1C, cz);
            return new Region((int)(h ^ (h >> 32)), x, z, radius, kind);
        }
        return null;   // la celda es casi toda mar
    }

    /// <summary>Todas las regiones del mundo (para marcarlas en el mapa y en la consola).</summary>
    public static IEnumerable<Region> All(WorldScale scale, int seed)
    {
        int nx = scale.WidthBlocks / Cell, nz = scale.HeightBlocks / Cell;
        for (int cz = 0; cz < nz; cz++)
        for (int cx = 0; cx < nx; cx++)
            if (Get(seed, cx, cz) is { } r) yield return r;
    }

    /// <summary>La región que contiene la columna, o null. f: 0 en el borde, 1 en el centro.</summary>
    public static Region? RegionAt(int seed, WorldScale scale, int wx, int wz, out double f)
    {
        f = 0;
        if ((uint)wz >= (uint)scale.HeightBlocks) return null;
        int cx = scale.WrapX(wx) / Cell, cz = wz / Cell;
        if (cx >= scale.WidthBlocks / Cell || cz >= scale.HeightBlocks / Cell) return null;
        var r = Get(seed, cx, cz);
        if (r == null) return null;
        double dx = scale.DeltaX(r.X, wx), dz = wz - r.Z, d = Math.Sqrt(dx * dx + dz * dz);
        if (d >= r.Radius) return null;
        f = 1 - d / r.Radius;
        return r;
    }
}
