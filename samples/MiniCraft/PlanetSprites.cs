using GF.Core;
using GF.Engine;
using Microsoft.Xna.Framework;

namespace MiniCraft;

/// <summary>
/// Sprites de matorral y plantas bajas que se dibujan por código al abrir un planeta: 16 formas (helechos, juncos, hierbas, rosetas, flores, matorrales ramosos,
/// enredos espinosos, retamas, brezos, tapices, setas, zarzas, cactus bajos...) cuyo TAMAÑO, proporciones y colores salen de la semilla del planeta. Todas son
/// bajas (como mucho ~2 bloques) y de silueta irregular, nada de copas redondas. El verde pasa por la paleta del planeta (PlanetPalette); las flores y los
/// sombreros de las setas eligen entre colores naturales (nunca negro). Se empaquetan en el atlas de sprites con los nombres flora_0..flora_15 (Blocks.FloraSprites).
/// </summary>
public static class PlanetSprites
{
    private const int Ppb = 16;   // píxeles por bloque

    private enum Kind { Fern, Reed, GrassFan, Rosette, Flower, FlowerBed, BigFrond, TwigScrub, SpikyBush, ThornTangle, BroomShrub, Heather, Mat, MushroomCluster, Bramble, Cactus }

    // Por ranura: forma y rangos de tamaño en bloques (ancho min/max, alto min/max).
    private static readonly (Kind Kind, float WLo, float WHi, float HLo, float HHi)[] Slots =
    {
        (Kind.Fern, 0.9f, 1.5f, 0.9f, 1.5f), (Kind.Reed, 0.8f, 1.1f, 1.4f, 2.2f), (Kind.GrassFan, 1.0f, 1.4f, 0.8f, 1.1f), (Kind.Rosette, 1.0f, 1.8f, 0.8f, 1.4f),
        (Kind.Flower, 0.7f, 1.0f, 0.9f, 1.4f), (Kind.FlowerBed, 1.2f, 1.8f, 0.8f, 1.2f), (Kind.BigFrond, 1.4f, 2.0f, 1.3f, 2.0f), (Kind.TwigScrub, 1.2f, 2.0f, 1.0f, 1.6f),
        (Kind.SpikyBush, 1.2f, 1.8f, 1.0f, 1.6f), (Kind.ThornTangle, 1.4f, 2.2f, 0.8f, 1.3f), (Kind.BroomShrub, 1.0f, 1.6f, 1.0f, 1.6f), (Kind.Heather, 1.0f, 1.6f, 0.9f, 1.4f),
        (Kind.Mat, 1.2f, 2.0f, 0.5f, 0.8f), (Kind.MushroomCluster, 0.8f, 1.5f, 0.7f, 1.2f), (Kind.Bramble, 1.4f, 2.2f, 1.2f, 1.8f), (Kind.Cactus, 0.8f, 1.4f, 1.2f, 2.2f),
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

    /// <summary>Añade los 16 sprites de plantas bajas del planeta al atlas (los .png sueltos con el mismo nombre mandan).</summary>
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
            case Kind.SpikyBush: Rosette(c, s, 11); break;
            case Kind.Flower: Flower(c, s); break;
            case Kind.FlowerBed: FlowerBed(c, rng, s); break;
            case Kind.TwigScrub: TwigScrub(c, rng, s); break;
            case Kind.ThornTangle: ThornTangle(c, rng, s); break;
            case Kind.BroomShrub: BroomShrub(c, rng, s); break;
            case Kind.Heather: Heather(c, rng, s); break;
            case Kind.Mat: Mat(c, s, salt); break;
            case Kind.MushroomCluster: MushroomCluster(c, rng, s, salt); break;
            case Kind.Bramble: Bramble(c, rng, s); break;
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

    private static (int X, int Y) ArcPoint(float x0, float y0, float x1, float y1, float arch, float t) =>
        ((int)MathF.Round(x0 + (x1 - x0) * t), (int)MathF.Round(y0 + (y1 - y0) * t - arch * MathF.Sin(MathF.PI * t)));

    private static void ArcFrond(Canvas c, float x0, float y0, float x1, float y1, float arch, Color col, Color dark, bool thick, bool pinnae)
    {
        int steps = (int)MathF.Max(8f, MathF.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) * 1.5f);
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            var (x, y) = ArcPoint(x0, y0, x1, y1, arch, t);
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

    /// <summary>Matorral ramoso: ramas leñosas que se bifurcan y acaban en pequeñas matas de hojas irregulares (nada de bola redonda).</summary>
    private static void Branch(Canvas c, SplitMixRandom rng, Style s, float x0, float y0, float ang, float len, int depth)
    {
        float x1 = x0 + MathF.Sin(ang) * len, y1 = y0 - MathF.Cos(ang) * len;
        c.Line((int)MathF.Round(x0), (int)MathF.Round(y0), (int)MathF.Round(x1), (int)MathF.Round(y1), depth == 0 ? s.Dark : s.Bark);
        if (depth > 0)
        {
            for (int k = 0; k < 2; k++) Branch(c, rng, s, x1, y1, ang + (k == 0 ? -1f : 1f) * (0.35f + rng.NextFloat() * 0.45f), len * 0.68f, depth - 1);
            return;
        }
        for (int i = 0; i < 7; i++)   // mata de hojas en la punta
        {
            int lx = (int)MathF.Round(x1) + rng.Next(-2, 3), ly = (int)MathF.Round(y1) + rng.Next(-2, 2);
            c.Set(lx, ly, i % 3 == 0 ? s.Light : i % 3 == 1 ? s.Leaf : s.Dark);
        }
    }

    private static void TwigScrub(Canvas c, SplitMixRandom rng, Style s)
    {
        int n = 3 + rng.Next(3);
        for (int i = 0; i < n; i++)
        {
            float ang = -0.9f + 1.8f * (i + 0.5f) / n + (rng.NextFloat() - 0.5f) * 0.3f;
            Branch(c, rng, s, c.W / 2f, c.H - 1, ang, c.H * 0.42f, 2);
        }
    }

    /// <summary>Enredo espinoso: muchas varas finas que se cruzan, bajas y anchas, con hojillas y espinas sueltas.</summary>
    private static void ThornTangle(Canvas c, SplitMixRandom rng, Style s)
    {
        int n = 9 + rng.Next(5);
        for (int i = 0; i < n; i++)
        {
            float sx = c.W * 0.5f + (rng.NextFloat() - 0.5f) * c.W * 0.3f;
            float ex = c.W * 0.08f + c.W * 0.84f * rng.NextFloat(), ey = c.H * (0.15f + 0.6f * rng.NextFloat());
            float arch = c.H * 0.25f * (rng.NextFloat() - 0.3f);
            ArcFrond(c, sx, c.H - 1, ex, ey, arch, s.Bark, s.Dark, false, false);
            for (int k = 0; k < 3; k++)
            {
                var (px, py) = ArcPoint(sx, c.H - 1, ex, ey, arch, 0.3f + 0.25f * k);
                c.Set(px, py - 1, s.Leaf);
                c.Set(px + 1, py, s.Light);
            }
        }
    }

    /// <summary>Retama: varas casi rectas y divergentes con matitas de hojas alternas a lo largo de su mitad superior.</summary>
    private static void BroomShrub(Canvas c, SplitMixRandom rng, Style s)
    {
        int n = 5 + rng.Next(4);
        for (int i = 0; i < n; i++)
        {
            float tx = c.W * 0.15f + c.W * 0.7f * (i + 0.5f) / n, ty = c.H * (0.05f + 0.2f * rng.NextFloat());
            float bx = c.W / 2f + rng.Next(-1, 2), by = c.H - 1;
            c.Line((int)bx, (int)by, (int)tx, (int)ty, s.Bark);
            for (int k = 0; k < 4; k++)
            {
                float t = 0.4f + 0.2f * k;
                int px = (int)(bx + (tx - bx) * t), py = (int)(by + (ty - by) * t);
                c.Set(px - 1, py, s.Leaf); c.Set(px + 1, py, s.Light); c.Set(px, py - 1, s.Leaf);
            }
        }
    }

    /// <summary>Brezo: muchos tallos finos y rectos con espigas de flores en la punta.</summary>
    private static void Heather(Canvas c, SplitMixRandom rng, Style s)
    {
        int n = 8 + rng.Next(6);
        for (int i = 0; i < n; i++)
        {
            int x = (int)(c.W * 0.1f + c.W * 0.8f * (i + 0.5f) / n), top = (int)(c.H * (0.05f + 0.3f * rng.NextFloat()));
            c.Line(c.W / 2 + (x - c.W / 2) / 3, c.H - 1, x, top, i % 2 == 0 ? s.Leaf : s.Dark);
            for (int k = 0; k < 3; k++) c.Set(x, top + k, Shade(s.Flower, 0.9f + 0.2f * Noise(x, k, 3)));
            c.Set(x + 1, top + 1, s.Flower);
        }
    }

    /// <summary>Tapiz bajo: una alfombra irregular de hojas, de borde dentado y altura variable.</summary>
    private static void Mat(Canvas c, Style s, int salt)
    {
        for (int x = 0; x < c.W; x++)
        {
            float edge = MathF.Min(1f, MathF.Min(x, c.W - 1 - x) / (c.W * 0.18f));
            int hh = Math.Max(1, (int)((c.H * (0.35f + 0.6f * Noise(x, 0, salt))) * edge) + 1);
            for (int y = c.H - 1; y >= c.H - hh; y--) c.Set(x, y, LeafAt(x, y, s, salt));
            if (Noise(x, 1, salt) > 0.88f) c.Set(x, c.H - hh, s.Flower);
        }
    }

    private static void MushroomAt(Canvas c, Style s, int cx, int stemH, int capRx, int capRy, int salt)
    {
        var stem = new Color(225, 215, 190);
        int capY = c.H - 1 - stemH;
        Rect(c, cx, capY, cx + (capRx >= 3 ? 1 : 0), c.H - 1, (x, y) => x == cx ? Shade(stem, 0.85f) : stem);
        Ellipse(c, cx, capY, capRx, capRy, (x, y) => Noise(x, y, salt) > 0.88f ? s.Spot : Shade(s.Cap, 0.9f + 0.2f * (1f - (y - (capY - capRy)) / (float)Math.Max(1, capRy))), capY);
    }

    private static void MushroomCluster(Canvas c, SplitMixRandom rng, Style s, int salt)
    {
        int tall = Math.Max(3, c.H / 2);
        MushroomAt(c, s, (int)(c.W * 0.28f), tall - 1 + rng.Next(2), Math.Max(2, c.W / 5), Math.Max(2, c.H / 4), salt);
        MushroomAt(c, s, (int)(c.W * 0.62f), tall + 1, Math.Max(3, c.W / 4), Math.Max(2, c.H / 3), salt + 1);
        MushroomAt(c, s, (int)(c.W * 0.85f), Math.Max(2, tall - 3), Math.Max(2, c.W / 7), Math.Max(1, c.H / 5), salt + 2);
    }

    /// <summary>Zarza: cañas largas y arqueadas con hojas sueltas y, a veces, bayas.</summary>
    private static void Bramble(Canvas c, SplitMixRandom rng, Style s)
    {
        int n = 3 + rng.Next(2);
        for (int i = 0; i < n; i++)
        {
            float ex = c.W * (0.1f + 0.8f * (i + 0.5f) / n), ey = c.H * (0.1f + 0.3f * rng.NextFloat()), arch = c.H * 0.35f;
            ArcFrond(c, c.W / 2f, c.H - 1, ex, ey, arch, s.Bark, s.Dark, false, false);
            for (int k = 1; k <= 4; k++)
            {
                var (px, py) = ArcPoint(c.W / 2f, c.H - 1, ex, ey, arch, k / 5f);
                c.Set(px - 1, py, s.Leaf); c.Set(px + 1, py - 1, s.Light);
                if (s.Berries && k % 2 == 0) c.Set(px, py + 1, s.Flower);
            }
        }
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
