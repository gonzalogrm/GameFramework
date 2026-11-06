using GF.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MiniCraft;

public enum PaletteRole { None, Ground, Foliage, Dirt, Stone, Sand, Water, Snow, Wood, Crystal, Fungus }

/// <summary>Hue: grados (relativo, o absoluto en grises). Sat/Val: multiplicadores. SatMin: saturación mínima (tiñe grises y blancos).</summary>
public readonly record struct HueShift(float Hue, float Sat, float Val, float SatMin);

/// <summary>
/// Paleta de colores de un planeta: sale solo de la semilla de su dirección (mismo planeta, misma paleta). Recolorea el atlas de bloques y el terreno
/// lejano con desplazamientos de tono por familia (vegetación, agua, arena, roca, nieve, madera), así cada planeta es reconocible pero extraño.
/// </summary>
public sealed class PlanetPalette
{
    public static readonly PlanetPalette Neutral = new(new Dictionary<PaletteRole, HueShift>(), "terrestre");
    /// <summary>La del planeta que se está jugando (la fija UniverseSession.CreateScene antes de abrir el mundo).</summary>
    public static PlanetPalette Current { get; set; } = Neutral;

    private static readonly (string Name, float Hue)[] Families =
    {
        ("terrestre", 110f), ("terrestre", 110f), ("lima", 85f), ("oliva", 65f), ("turquesa", 140f), ("otoño", 30f), ("ocre", 50f),
    };
    private static readonly float[] WaterHues = { 222f, 210f, 200f, 190f, 182f };
    private static readonly float[] StoneHues = { 0f, 15f, 30f, 40f };
    private static readonly float[] SnowHues = { 0f, 40f, 200f };

    private readonly Dictionary<PaletteRole, HueShift> _shifts;
    public string Name { get; }

    private PlanetPalette(Dictionary<PaletteRole, HueShift> shifts, string name)
    {
        _shifts = shifts; Name = name;
    }

    public static PlanetPalette For(ulong seed)
    {
        var rng = new SplitMixRandom(Hashing.Mix(seed ^ 0x9A1E77EUL));
        float J(float a) => (rng.NextFloat() * 2f - 1f) * a;

        var (name, foliageHue) = Families[rng.Next(Families.Length)];
        float fol = foliageHue - 110f + J(10f), gro = fol + J(16f);
        float waterHue = WaterHues[rng.Next(WaterHues.Length)];
        float stoneHue = StoneHues[rng.Next(StoneHues.Length)];
        float snowHue = SnowHues[rng.Next(SnowHues.Length)];

        var s = new Dictionary<PaletteRole, HueShift>
        {
            [PaletteRole.Foliage] = new(fol, 1.1f + J(0.2f), 1f + J(0.1f), 0f),
            [PaletteRole.Ground] = new(gro, 1.1f + J(0.2f), 1f + J(0.1f), 0f),
            [PaletteRole.Dirt] = new(gro * 0.4f + J(20f), 1f + J(0.2f), 1f + J(0.1f), 0f),
            [PaletteRole.Water] = new(waterHue - 222f, 1f + J(0.1f), 1f + J(0.06f), 0f),
            [PaletteRole.Sand] = new(J(12f), 1f + J(0.15f), 1f, 0f),
            [PaletteRole.Stone] = new(stoneHue, 1f, 1f + J(0.08f), 0.04f + rng.NextFloat() * 0.08f),
            [PaletteRole.Snow] = new(snowHue, 1f, 1f, 0.03f + rng.NextFloat() * 0.04f),
            [PaletteRole.Wood] = new(J(18f), 1f + J(0.2f), 1f + J(0.1f), 0f),
            [PaletteRole.Crystal] = new(J(25f), 1f, 1f, 0f),   // cada planeta, su cristal y su hongo
            [PaletteRole.Fungus] = new(J(20f), 1f, 1f, 0f),
        };
        return new PlanetPalette(s, $"flora {name}");
    }

    public static PaletteRole RoleOfBiome(ushort biome)
    {
        if (biome == Biomes.Ocean) return PaletteRole.Water;
        if (biome == Biomes.Beach || biome == Biomes.Desert) return PaletteRole.Sand;
        if (biome == Biomes.Tundra || biome == Biomes.SnowPeaks) return PaletteRole.Snow;
        if (biome == Biomes.Mountains) return PaletteRole.Stone;
        return PaletteRole.Ground;
    }

    public Color Apply(PaletteRole role, Color c)
    {
        if (!_shifts.TryGetValue(role, out var sh)) return c;
        ToHsv(c, out float h, out float s, out float v);
        h = (h + sh.Hue) % 360f;
        if (h < 0f) h += 360f;
        s = Math.Clamp(MathF.Max(s * sh.Sat, sh.SatMin), 0f, 1f);
        v = Math.Clamp(MathF.Max(v * sh.Val, 0.30f), 0f, 1f);   // nunca negro
        var o = FromHsv(h, s, v);
        return new Color(o.R, o.G, o.B, c.A);
    }

    private static PaletteRole TileRole(int tile, Color c)
    {
        switch (tile)
        {
            case 0: return PaletteRole.Ground;
            case 1: case 21: case 23:   // lateral de hierba: verdes = suelo, el resto = tierra
                ToHsv(c, out float h, out float s, out _);
                return s > 0.25f && h > 70f && h < 170f ? PaletteRole.Ground : PaletteRole.Dirt;
            case 2: return PaletteRole.Dirt;
            case 3: return PaletteRole.Stone;
            case 4: return PaletteRole.Sand;
            case 5: return PaletteRole.Water;
            case 6: case 7: return PaletteRole.Wood;
            case 8: return PaletteRole.Foliage;
            case 9: return PaletteRole.Snow;
            case 10: return PaletteRole.Crystal;
            case 11: return PaletteRole.Fungus;
            case 13: return PaletteRole.Snow;    // sal
            case 14: return PaletteRole.Stone;   // ceniza
            case 16: case 17: return PaletteRole.Sand;
            case 18: case 19: return PaletteRole.Dirt;
            case 20: case 22: return PaletteRole.Ground;
            case 24: case 25: case 26: return PaletteRole.Stone;
            case 27: case 28: case 29: case 30: return PaletteRole.Wood;
            case 31: return PaletteRole.Foliage;
            case 32: return PaletteRole.Fungus;
            default: return PaletteRole.None;
        }
    }

    /// <summary>Copia recoloreada del atlas de bloques (rejilla de tiles cuadrados). La textura original no se toca (el ContentManager la comparte).</summary>
    public Texture2D Recolor(Texture2D src, GraphicsDevice device, int tileSize)
    {
        if (_shifts.Count == 0) return src;
        int w = src.Width, h = src.Height, cols = Math.Max(1, w / tileSize);
        var px = new Color[w * h];
        src.GetData(px);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            if (px[i].A == 0) continue;
            var role = TileRole((y / tileSize) * cols + x / tileSize, px[i]);
            if (role != PaletteRole.None) px[i] = Apply(role, px[i]);
        }
        var tex = new Texture2D(device, w, h);
        tex.SetData(px);
        return tex;
    }

    private static void ToHsv(Color c, out float h, out float s, out float v)
    {
        float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
        float max = MathF.Max(r, MathF.Max(g, b)), min = MathF.Min(r, MathF.Min(g, b)), d = max - min;
        v = max;
        s = max <= 0f ? 0f : d / max;
        if (d <= 1e-6f) { h = 0f; return; }
        if (max == r) h = 60f * (((g - b) / d) % 6f);
        else if (max == g) h = 60f * ((b - r) / d + 2f);
        else h = 60f * ((r - g) / d + 4f);
        if (h < 0f) h += 360f;
    }

    private static Color FromHsv(float h, float s, float v)
    {
        float c = v * s, x = c * (1f - MathF.Abs((h / 60f) % 2f - 1f)), m = v - c;
        float r, g, b;
        int sector = (int)(h / 60f) % 6;
        switch (sector)
        {
            case 0: r = c; g = x; b = 0; break;
            case 1: r = x; g = c; b = 0; break;
            case 2: r = 0; g = c; b = x; break;
            case 3: r = 0; g = x; b = c; break;
            case 4: r = x; g = 0; b = c; break;
            default: r = c; g = 0; b = x; break;
        }
        return new Color((int)((r + m) * 255f + 0.5f), (int)((g + m) * 255f + 0.5f), (int)((b + m) * 255f + 0.5f));
    }
}
