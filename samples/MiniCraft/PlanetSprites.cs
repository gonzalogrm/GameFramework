using GF.Core;
using GF.Engine;
using Microsoft.Xna.Framework;

namespace MiniCraft;

/// <summary>
/// Sprites de flora que se dibujan por código al abrir un planeta: 16 formas (helechos, juncos, flores, arbustos, árboles, setas, cactus...) cuyo TAMAÑO,
/// proporciones y colores salen de la semilla del planeta. El verde de las hojas pasa por la paleta del planeta (PlanetPalette); las flores y los sombreros
/// de las setas eligen entre colores naturales (nunca negro). Se empaquetan en el atlas de sprites con los nombres flora_0..flora_15 (bloques Blocks.FloraSprites).
/// </summary>
public static class PlanetSprites
{
    private const int Ppb = 16;   // píxeles por bloque

    private enum Kind { Fern, Reed, GrassFan, Rosette, Flower, FlowerBed, BigFrond, BushRound, BushSpiky, TreeRound, TreeCone, TreePalm, TreeUmbrella, Mushroom, TreeWeeping, Cactus }

    // Por ranura: forma y rangos de tamaño en bloques (ancho min/max, alto min/max).
    private static readonly (Kind Kind, float WLo, float WHi, float HLo, float HHi)[] Slots =
    {
        (Kind.Fern, 0.9f, 1.5f, 0.9f, 1.5f), (Kind.Reed, 0.8f, 1.1f, 2.0f, 3.0f), (Kind.GrassFan, 1.0f, 1.4f, 0.8f, 1.1f), (Kind.Rosette, 1.0f, 1.8f, 0.8f, 1.4f),
        (Kind.Flower, 0.7f, 1.0f, 1.0f, 1.6f), (Kind.FlowerBed, 1.2f, 1.8f, 0.8f, 1.2f), (Kind.BigFrond, 1.8f, 2.6f, 1.8f, 2.8f), (Kind.BushRound, 1.6f, 2.6f, 1.2f, 1.8f),
        (Kind.BushSpiky, 1.6f, 2.4f, 1.6f, 2.2f), (Kind.TreeRound, 2.5f, 4.0f, 3.5f, 5.5f), (Kind.TreeCone, 2.5f, 3.5f, 4.5f, 6.5f), (Kind.TreePalm, 3.0f, 4.5f, 4.0f, 6.0f),
        (Kind.TreeUmbrella, 3.5f, 5.0f, 3.5f, 5.0f), (Kind.Mushroom, 1.5f, 3.0f, 1.5f, 3.0f), (Kind.TreeWeeping, 3.0f, 4.5f, 3.5f, 5.0f), (Kind.Cactus, 1.2f, 2.0f, 2.0f, 3.5f),
    };

    private static readonly Color[] LeafTints =
        { new(55, 140, 52), new(120, 150, 60), new(45, 135, 100), new(95, 170, 60), new(40, 115, 55) };
    private static readonly Color[] FlowerColors =
        { new(235, 205, 70), new(230, 140, 50), new(236, 236, 226), new(205, 65, 55), new(95, 135, 220), new(165, 135, 205) };
    private static readonly Color[] CapColors =
        { new(190, 70, 55), new(215, 130, 50), new(225, 205, 160), new(190, 150, 100) };

    private sealed class Canvas
    {
        public readonly int W, H;
        public readonly Color[] Px;
        public Canvas(int w, int h) { W = w; H = h; Px = new Color[w * h]; }
        public void Set(int x, int y, Color c) { if ((uint)x < (uint)W && (uint)y < (uint)H) Px[y * W + x] = c; }
        public void Line(int x0, int y0, int x1, int y1, Color c, int thick = 1)
        {
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx - dy;
            while (true)
            {
                Set(x0, y0, c);
                if (thick > 1) Set(x0 + 1, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }
    }

    private sealed class Style
    {
        public Color Leaf, Dark, Light, Bark, Flower, Center, Cap, Spot;
        public bool Berries;
    }

    // ------------------------------------------------------------------ API

    /// <summary>Añade los 16 sprites de flora del planeta al atlas (los .png sueltos con el mismo nombre mandan).</summary>
    public static void AddAll(SpriteAtlasBuilder builder, ulong seed)
    {
        for (int i = 0; i < Slots.Length; i++)
        {
            string name = $"flora_{i}";
            if (builder.Contains(name)) continue;
            var rng = new SplitMixRandom(Hashing.Mix(seed ^ (0x5B1UL + (ulong)i * 0x9E37UL)));
            var (kind, wLo, wHi, hLo, hHi) = Slots[i];
            int w = Math.Max(8, (int)MathF.Round((wLo + rng.NextFloat() * (wHi - wLo)) * Ppb));
            int h = Math.Max(8, (int)MathF.Round((hLo + rng.NextFloat() * (hHi - hLo)) * Ppb));
            var canvas = new Canvas(w, h);
            Draw(canvas, kind, rng, MakeStyle(rng), i);
            builder.Add(name, w, h, canvas.Px, new Vector2(w / (float)Ppb, h / (float)Ppb));
        }
    }

    /// <summary>Recolorea con el verde del planeta los sprites de hierba, hierba alta y matorral de los .png.</summary>
    public static void Recolor(string name, Color[] pixels)
    {
        if (name is not ("grass_tuft" or "tall_grass" or "bush_large")) return;
        var palette = PlanetPalette.Current;
        for (int i = 0; i < pixels.Length; i++)
            if (pixels[i].A > 0) pixels[i] = palette.Apply(PaletteRole.Foliage, pixels[i]);
    }

    // ------------------------------------------------------------------ estilo

    private static Color Shade(Color c, float f) =>
        new((int)Math.Min(255f, c.R * f), (int)Math.Min(255f, c.G * f), (int)Math.Min(255f, c.B * f), c.A);

    private static Style MakeStyle(SplitMixRandom rng)
    {
        var leaf = PlanetPalette.Current.Apply(PaletteRole.Foliage, LeafTints[rng.Next(LeafTints.Length)]);
        var flower = FlowerColors[rng.Next(FlowerColors.Length)];
        return new Style
        {
            Leaf = leaf, Dark = Shade(leaf, 0.72f), Light = Shade(leaf, 1.25f),
            Bark = PlanetPalette.Current.Apply(PaletteRole.Wood, new Color(112, 82, 54)),
            Flower = flower, Center = new Color(250, 225, 120),
            Cap = CapColors[rng.Next(CapColors.Length)], Spot = new Color(240, 235, 215),
            Berries = rng.Chance(0.4f),
        };
    }

    private static float Noise(int x, int y, int salt)
    {
        uint h = unchecked((uint)(x * 73856093 ^ y * 19349663 ^ salt * 83492791));
        h ^= h >> 13; h = unchecked(h * 0x5bd1e995u); h ^= h >> 15;
        return (h & 1023) / 1023f;
    }

    private static Color LeafAt(int x, int y, Style s, int salt)
    {
        float n = Noise(x, y, salt);
        return n < 0.15f ? s.Dark : n > 0.8f ? s.Light : s.Leaf;
    }

    // ------------------------------------------------------------------ dibujo

    private static void Draw(Canvas c, Kind kind, SplitMixRandom rng, Style s, int salt)
    {
        switch (kind)
        {
            case Kind.Fern: Fronds(c, rng, s, 5 + rng.Next(4), false); break;
            case Kind.BigFrond: Fronds(c, rng, s, 5 + rng.Next(3), true); break;
            case Kind.Reed: Reed(c, rng, s); break;
            case Kind.GrassFan: GrassFan(c, rng, s); break;
            case Kind.Rosette: Rosette(c, s, 7 + rng.Next(4)); break;
            case Kind.BushSpiky: Rosette(c, s, 11); break;
            case Kind.Flower: Flower(c, s); break;
            case Kind.FlowerBed: FlowerBed(c, rng, s); break;
            case Kind.BushRound: BushRound(c, s, salt); break;
            case Kind.TreeRound: TreeRound(c, rng, s, salt); break;
            case Kind.TreeCone: TreeCone(c, s, salt); break;
            case Kind.TreePalm: TreePalm(c, rng, s); break;
            case Kind.TreeUmbrella: TreeUmbrella(c, rng, s, salt); break;
            case Kind.TreeWeeping: TreeWeeping(c, rng, s, salt); break;
            case Kind.Mushroom: Mushroom(c, s, salt); break;
            default: Cactus(c, rng, s); break;
        }
    }

    private static void Ellipse(Canvas c, float cx, float cy, float rx, float ry, Func<int, int, Color> col, int maxY = int.MaxValue)
    {
        for (int y = (int)MathF.Floor(cy - ry); y <= (int)MathF.Ceiling(cy + ry) && y <= maxY; y++)
        for (int x = (int)MathF.Floor(cx - rx); x <= (int)MathF.Ceiling(cx + rx); x++)
        {
            float nx = (x - cx) / rx, ny = (y - cy) / ry;
            if (nx * nx + ny * ny <= 1f) c.Set(x, y, col(x, y));
        }
    }

    private static void Rect(Canvas c, int x0, int y0, int x1, int y1, Func<int, int, Color> col)
    {
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++) c.Set(x, y, col(x, y));
    }

    private static void ArcFrond(Canvas c, float x0, float y0, float x1, float y1, float arch, Color col, Color dark, bool thick, bool pinnae)
    {
        int steps = (int)MathF.Max(8f, MathF.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) * 1.5f);
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            int x = (int)MathF.Round(x0 + (x1 - x0) * t), y = (int)MathF.Round(y0 + (y1 - y0) * t - arch * MathF.Sin(MathF.PI * t));
            var cc = t < 0.5f ? dark : col;
            c.Set(x, y, cc);
            if (thick) c.Set(x + 1, y, cc);
            if (pinnae && i % 2 == 0 && t > 0.15f) { c.Set(x - 1, y + 1, col); c.Set(x + 2, y + 1, col); }
        }
    }

    private static void Fronds(Canvas c, SplitMixRandom rng, Style s, int n, bool big)
    {
        for (int i = 0; i < n; i++)
        {
            float tx = c.W * 0.12f + c.W * 0.76f * (i + 0.5f) / n;
            float ty = c.H * (0.05f + 0.45f * rng.NextFloat());
            ArcFrond(c, c.W / 2f, c.H - 1, tx, ty, c.H * (big ? 0.25f : 0.18f), i % 2 == 0 ? s.Leaf : s.Light, s.Dark, big, true);
        }
    }

    private static void Reed(Canvas c, SplitMixRandom rng, Style s)
    {
        int n = 3 + rng.Next(3);
        var seed = new Color(190, 165, 100);
        for (int i = 0; i < n; i++)
        {
            int x = (int)(c.W * 0.15f + c.W * 0.7f * (i + 0.5f) / n), top = (int)(c.H * (0.05f + 0.25f * rng.NextFloat()));
            c.Line(c.W / 2, c.H - 1, x + rng.Next(-1, 2), top, i % 2 == 0 ? s.Leaf : s.Light);
            for (int k = 0; k < 3; k++) c.Set(x, top + k, seed);
        }
    }

    private static void GrassFan(Canvas c, SplitMixRandom rng, Style s)
    {
        int n = 8 + rng.Next(5);
        for (int i = 0; i < n; i++)
        {
            int tx = (int)(c.W * 0.1f + c.W * 0.8f * (i + 0.5f) / n), ty = (int)(c.H * (0.1f + 0.5f * rng.NextFloat()));
            c.Line(c.W / 2 + rng.Next(-1, 2), c.H - 1, tx, ty, i % 2 == 0 ? s.Leaf : s.Light);
        }
    }

    private static void Rosette(Canvas c, Style s, int n)
    {
        for (int i = 0; i < n; i++)
        {
            float a = -1.2f + 2.4f * i / Math.Max(1, n - 1);
            int tx = (int)(c.W / 2f + MathF.Sin(a) * c.W * 0.48f), ty = (int)(c.H - 1 - (0.45f + 0.55f * MathF.Cos(a)) * c.H * 0.9f);
            c.Line(c.W / 2, c.H - 1, tx, ty, i % 2 == 0 ? s.Leaf : s.Light, 2);
            c.Set(tx, ty, s.Light);
        }
    }

    private static void FlowerHead(Canvas c, Style s, int cx, int cy, int r)
    {
        Ellipse(c, cx, cy, r, r * 0.9f, (x, y) => Shade(s.Flower, 0.9f + Noise(x, y, 5) * 0.2f));
        c.Set(cx, cy, s.Center);
        if (r >= 3) { c.Set(cx + 1, cy, s.Center); c.Set(cx, cy + 1, s.Center); }
    }

    private static void Flower(Canvas c, Style s)
    {
        int r = Math.Max(2, c.W / 3), cx = c.W / 2, headY = r + 1;
        c.Line(cx, c.H - 1, cx, headY, s.Leaf);
        c.Line(cx, c.H - 2, cx - c.W / 3, c.H - 4 - c.H / 6, s.Light);
        c.Line(cx, c.H - 3, cx + c.W / 3, c.H - 5 - c.H / 6, s.Light);
        FlowerHead(c, s, cx, headY, r);
    }

    private static void FlowerBed(Canvas c, SplitMixRandom rng, Style s)
    {
        GrassFan(c, rng, s);
        foreach (float fx in new[] { 0.2f, 0.5f, 0.8f })
        {
            int x = (int)(c.W * fx), y = (int)(c.H * (0.1f + 0.25f * rng.NextFloat()));
            c.Line(c.W / 2, c.H - 1, x, y, s.Leaf);
            FlowerHead(c, s, x, y, 2);
        }
    }

    private static void BushRound(Canvas c, Style s, int salt)
    {
        float ry = c.H * 0.5f, cy = c.H - 1 - ry * 0.9f;
        Ellipse(c, c.W / 2f, cy, c.W / 2f - 0.5f, ry, (x, y) =>
        {
            float n = Noise(x, y, salt);
            if (s.Berries && n > 0.5f && n < 0.54f) return s.Flower;
            return y < cy - ry * 0.35f && n > 0.4f ? s.Light : LeafAt(x, y, s, salt);
        });
    }

    private static void Trunk(Canvas c, Style s, int x0, int y0, int x1, int y1)
    {
        c.Line(x0, y0, x1, y1, s.Bark, 2);
        c.Set(x0 - 1, y0, s.Bark); c.Set(x0 + 2, y0, s.Bark);   // base ensanchada
    }

    private static void TreeRound(Canvas c, SplitMixRandom rng, Style s, int salt)
    {
        int lean = rng.Next(-1, 2), tx = c.W / 2 + lean;
        float cy = c.H * 0.32f;
        Trunk(c, s, c.W / 2, c.H - 1, tx, (int)cy);
        Ellipse(c, tx, cy, c.W * 0.45f, c.H * 0.32f, (x, y) => LeafAt(x, y, s, salt));
    }

    private static void TreeCone(Canvas c, Style s, int salt)
    {
        int top = (int)(c.H * 0.04f), bottom = (int)(c.H * 0.86f);
        c.Line(c.W / 2, c.H - 1, c.W / 2, bottom, s.Bark, 2);
        for (int y = top; y <= bottom; y++)
        {
            float t = (y - top) / (float)Math.Max(1, bottom - top);
            int hw = (int)MathF.Round(1 + t * (c.W * 0.46f - 1));
            if ((y - top) % 5 == 4) hw = Math.Max(1, hw - 2);   // pisos
            for (int x = c.W / 2 - hw; x <= c.W / 2 + hw; x++) c.Set(x, y, LeafAt(x, y, s, salt));
        }
    }

    private static void TreePalm(Canvas c, SplitMixRandom rng, Style s)
    {
        int lean = rng.Next(-c.W / 5, c.W / 5 + 1), tx = c.W / 2 + lean, topY = (int)(c.H * 0.28f);
        ArcFrond(c, c.W / 2f, c.H - 1, tx, topY, lean * 0.4f, s.Bark, s.Bark, true, false);
        const int n = 7;
        for (int i = 0; i < n; i++)
        {
            float a = MathF.PI * i / (n - 1);
            float ex = tx + MathF.Cos(a) * c.W * 0.48f, ey = topY + c.H * 0.18f * (0.4f + MathF.Abs(MathF.Cos(a)));
            ArcFrond(c, tx, topY, ex, ey, c.H * 0.12f, i % 2 == 0 ? s.Leaf : s.Light, s.Dark, false, true);
        }
        c.Set(tx, topY + 1, s.Bark); c.Set(tx + 1, topY + 1, s.Bark);   // cocos
    }

    private static void TreeUmbrella(Canvas c, SplitMixRandom rng, Style s, int salt)
    {
        int lean = rng.Next(-1, 2), tx = c.W / 2 + lean;
        float cy = c.H * 0.3f, ry = c.H * 0.14f;
        Trunk(c, s, c.W / 2, c.H - 1, tx, (int)cy);
        Ellipse(c, tx, cy, c.W * 0.5f - 0.5f, ry, (x, y) => LeafAt(x, y, s, salt));
        Ellipse(c, tx, cy - ry, c.W * 0.3f, ry * 0.9f, (x, y) => LeafAt(x, y, s, salt + 1));
    }

    private static void TreeWeeping(Canvas c, SplitMixRandom rng, Style s, int salt)
    {
        int tx = c.W / 2;
        float cy = c.H * 0.3f, ry = c.H * 0.2f, rx = c.W * 0.48f;
        Trunk(c, s, tx, c.H - 1, tx, (int)cy);
        Ellipse(c, tx, cy, rx, ry, (x, y) => LeafAt(x, y, s, salt));
        for (int x = (int)(tx - rx); x <= (int)(tx + rx); x += 2)
        {
            int len = (int)(c.H * (0.2f + 0.3f * rng.NextFloat()));
            c.Line(x, (int)(cy + ry * 0.5f), x, (int)(cy + ry * 0.5f) + len, rng.Chance(0.5f) ? s.Leaf : s.Light);
        }
    }

    private static void Mushroom(Canvas c, Style s, int salt)
    {
        int sw = Math.Max(2, c.W / 5), cx = c.W / 2, capY = (int)(c.H * 0.5f);
        var stem = new Color(225, 215, 190);
        Rect(c, cx - sw / 2, capY, cx - sw / 2 + sw - 1, c.H - 1, (x, y) => x == cx - sw / 2 ? Shade(stem, 0.85f) : stem);
        Ellipse(c, cx, capY, c.W * 0.48f, c.H * 0.4f, (x, y) => Noise(x, y, salt) > 0.9f ? s.Spot : Shade(s.Cap, 0.9f + 0.2f * (1f - y / (float)Math.Max(1, capY))), capY);
    }

    private static void Cactus(Canvas c, SplitMixRandom rng, Style s)
    {
        int cw = Math.Max(3, c.W / 4), x0 = c.W / 2 - cw / 2, x1 = x0 + cw - 1, top = (int)(c.H * 0.08f);
        Func<int, int, Color> body = (x, y) => x == x0 ? s.Dark : x == x1 ? s.Dark : x == (x0 + x1) / 2 ? s.Light : s.Leaf;
        Rect(c, x0, top, x1, c.H - 1, body);
        int arms = 1 + rng.Next(2);
        for (int a = 0; a < arms; a++)
        {
            bool left = (a + rng.Next(2)) % 2 == 0;
            int armY = (int)(c.H * (0.45f + 0.25f * rng.NextFloat())), len = Math.Max(3, c.W / 3), up = (int)(c.H * (0.2f + 0.2f * rng.NextFloat()));
            int ax0 = left ? x0 - len : x1 + 1, ax1 = left ? x0 - 1 : x1 + len, vx0 = left ? x0 - len : x1 + len - 2, vx1 = vx0 + 2;
            Rect(c, ax0, armY, ax1, armY + 2, body);
            Rect(c, vx0, armY - up, vx1, armY + 2, (x, y) => x == vx0 || x == vx1 ? s.Dark : s.Leaf);
        }
        if (rng.Chance(0.5f)) FlowerHead(c, s, (x0 + x1) / 2, top, 2);
    }
}
