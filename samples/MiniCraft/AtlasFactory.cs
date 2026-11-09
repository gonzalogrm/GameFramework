using GF.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace MiniCraft;

/// <summary>
/// Atlas de bloques. El contenido (Content/blocks.png, 4x4 tiles de 16 px) se AMPLÍA a 8x8 (128x128) conservando los índices de tile 0..15, y los tiles
/// 10..32 (bloques alienígenas) se pintan por código. Después se recolorea con la paleta del planeta actual (PlanetPalette.Current).
/// </summary>
public static class AtlasFactory
{
    private const int T = 16, OldN = 4, NewN = 8;

    public static TextureAtlas Load(ContentManager content, GraphicsDevice device)
    {
        var palette = PlanetPalette.Current;
        try
        {
            var src = content.Load<Texture2D>("blocks");
            var px = new Color[src.Width * src.Height];
            src.GetData(px);
            return Finish(Build(px, src.Width, src.Height, device), palette, device);
        }
        catch (ContentLoadException e)
        {
            Console.Error.WriteLine("[contenido] no se pudo cargar 'blocks' (" + e.Message + "): se genera el atlas por código");
            return Finish(Create(device).Texture, palette, device);
        }
    }

    private static TextureAtlas Finish(Texture2D tex, PlanetPalette palette, GraphicsDevice device)
    {
        var recolored = palette.Recolor(tex, device, T);
        if (!ReferenceEquals(recolored, tex)) tex.Dispose();
        ComputeTileColors(recolored);
        return new TextureAtlas(recolored, T);
    }

    /// <summary>Atlas generado por código (respaldo si falta el contenido), ya ampliado y sin recolorear.</summary>
    public static TextureAtlas Create(GraphicsDevice device)
    {
        var data = new Color[OldN * T * OldN * T];
        var rnd = new Random(1234);

        Color Jitter(Color c, int amt = 14)
        {
            int d = rnd.Next(-amt, amt + 1);
            return new Color(Math.Clamp(c.R + d, 0, 255), Math.Clamp(c.G + d, 0, 255), Math.Clamp(c.B + d, 0, 255));
        }

        void Fill(int tile, Func<int, int, Color> pixel)
        {
            int ox = (tile % OldN) * T, oy = (tile / OldN) * T;
            for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
                data[(oy + y) * OldN * T + ox + x] = pixel(x, y);
        }

        var grass = new Color(86, 160, 60);
        var dirt = new Color(134, 96, 67);
        Fill(Blocks.TileGrassTop, (_, _) => Jitter(grass));
        Fill(Blocks.TileGrassSide, (_, y) => y < 4 ? Jitter(grass) : Jitter(dirt));
        Fill(Blocks.TileDirt, (_, _) => Jitter(dirt));
        Fill(Blocks.TileStone, (_, _) => Jitter(new Color(125, 125, 125), 18));
        Fill(Blocks.TileSand, (_, _) => Jitter(new Color(219, 207, 142), 8));
        Fill(Blocks.TileWater, (_, _) => Jitter(new Color(50, 90, 200), 6));
        Fill(Blocks.TileLogTop, (x, y) =>
        {
            int ring = (int)MathF.Max(MathF.Abs(x - 7.5f), MathF.Abs(y - 7.5f));
            return ring % 3 == 0 ? Jitter(new Color(110, 80, 45), 5) : Jitter(new Color(160, 120, 70), 5);
        });
        Fill(Blocks.TileLogSide, (x, _) => x % 4 == 0 ? Jitter(new Color(80, 58, 34), 5) : Jitter(new Color(102, 75, 44), 6));
        Fill(Blocks.TileSnow, (_, _) => Jitter(new Color(238, 242, 248), 6));
        Fill(Blocks.TileLeaves, (_, _) => rnd.Next(4) == 0 ? Jitter(new Color(30, 90, 30), 6) : Jitter(new Color(55, 135, 50), 10));

        return new TextureAtlas(Build(data, OldN * T, OldN * T, device), T);
    }

    /// <summary>Copia los tiles del atlas viejo (índice i = columna i % columnas) a una rejilla de 8 columnas y pinta los tiles nuevos.</summary>
    private static Texture2D Build(Color[] old, int ow, int oh, GraphicsDevice device)
    {
        int size = NewN * T, oldCols = Math.Max(1, ow / T), oldTiles = oldCols * (oh / T);
        var data = new Color[size * size];
        for (int tile = 0; tile < Math.Min(oldTiles, NewN * NewN); tile++)
        {
            int sx = (tile % oldCols) * T, sy = (tile / oldCols) * T, dx = (tile % NewN) * T, dy = (tile / NewN) * T;
            for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
                data[(dy + y) * size + dx + x] = old[(sy + y) * ow + sx + x];
        }
        PaintAlien(data, size);
        PunchHoles(data, size, Blocks.TileLeaves, 41, 0.30f);    // hojas: un 30 % de píxeles transparentes
        PunchHoles(data, size, Blocks.TileFronds, 43, 0.30f);
        var tex = new Texture2D(device, size, size);
        tex.SetData(data);
        return tex;
    }

    /// <summary>Hace transparente (alfa 0) la fracción 'fraction' de los píxeles de un tile, en manchas pequeñas: las hojas dejan ver lo que hay detrás.</summary>
    private static void PunchHoles(Color[] data, int width, int tile, int salt, float fraction)
    {
        int ox = (tile % NewN) * T, oy = (tile / NewN) * T;
        var scores = new float[T * T];
        for (int y = 0; y < T; y++)
        for (int x = 0; x < T; x++) scores[y * T + x] = Vn(x, y, salt, 2) * 0.55f + Hash01(x, y, salt + 7) * 0.45f;
        var sorted = (float[])scores.Clone();
        Array.Sort(sorted);
        float threshold = sorted[Math.Clamp((int)((1f - fraction) * sorted.Length), 0, sorted.Length - 1)];
        for (int y = 0; y < T; y++)
        for (int x = 0; x < T; x++)
            if (scores[y * T + x] >= threshold) data[(oy + y) * width + ox + x] = new Color(0, 0, 0, 0);
    }

    private static float Hash01(int ix, int iy, int salt)
    {
        uint h = unchecked((uint)(ix * 374761393 + iy * 668265263 + salt * 1274126177));
        h = unchecked((h ^ (h >> 13)) * 1274126177u);
        h ^= h >> 16;
        return (h & 0xFFFF) / 65535f;
    }

    /// <summary>Ruido de valores 0..1 dentro de un tile de T x T px, periódico (sin costuras al repetir el tile). 'cell' = tamaño de la mancha en px.</summary>
    private static float Vn(int x, int y, int salt, int cell)
    {
        int n = T / cell, gx = x / cell, gy = y / cell;
        float fx = (x % cell) / (float)cell, fy = (y % cell) / (float)cell;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        float a = Hash01(gx % n, gy % n, salt), b = Hash01((gx + 1) % n, gy % n, salt);
        float c = Hash01(gx % n, (gy + 1) % n, salt), d = Hash01((gx + 1) % n, (gy + 1) % n, salt);
        return (a + (b - a) * fx) * (1f - fy) + (c + (d - c) * fx) * fy;
    }

    private static Color[] _tileColors = new Color[NewN * NewN];

    /// <summary>Color medio de un tile del atlas YA recoloreado con la paleta del planeta: el terreno lejano lo usa para coincidir con los bloques.</summary>
    public static Color TileColor(int tile)
    {
        var t = _tileColors;
        return (uint)tile < (uint)t.Length ? t[tile] : new Color(128, 128, 128);
    }

    private static void ComputeTileColors(Texture2D tex)
    {
        var px = new Color[tex.Width * tex.Height];
        tex.GetData(px);
        int cols = tex.Width / T, rows = tex.Height / T;
        var result = new Color[cols * rows];
        for (int tile = 0; tile < result.Length; tile++)
        {
            long r = 0, g = 0, b = 0, count = 0;
            int ox = (tile % cols) * T, oy = (tile / cols) * T;
            for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                var p = px[(oy + y) * tex.Width + ox + x];
                if (p.A == 0) continue;
                r += p.R; g += p.G; b += p.B; count++;
            }
            result[tile] = count == 0 ? new Color(128, 128, 128) : new Color((int)(r / count), (int)(g / count), (int)(b / count));
        }
        _tileColors = result;
    }

    /// <summary>Tiles 10..32: cristal, hongo, metal, sal, ceniza, panel, arena roja/negra, arcilla, suelo oscuro, musgo, liquen, basalto, estratos, tiza, tallo, madera petrificada, frondas, sombrero de espora.</summary>
    private static void PaintAlien(Color[] data, int width)
    {
        var rnd = new Random(777);
        Color J(Color c, int amt)
        {
            int d = rnd.Next(-amt, amt + 1);
            return new Color(Math.Clamp(c.R + d, 0, 255), Math.Clamp(c.G + d, 0, 255), Math.Clamp(c.B + d, 0, 255));
        }
        void Fill(int tile, Func<int, int, Color> pixel)
        {
            int ox = (tile % NewN) * T, oy = (tile / NewN) * T;
            for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
                data[(oy + y) * width + ox + x] = pixel(x, y);
        }
        // Suelos y rocas: manchas suaves (ruido de valores que casa en los bordes del tile), sin franjas ni vetas regulares.
        Color Mottle(int x, int y, int salt, Color dark, Color mid, Color light)
        {
            float n = Vn(x, y, salt, 4) * 0.7f + Vn(x, y, salt + 1, 2) * 0.3f;
            return J(n < 0.38f ? dark : n > 0.64f ? light : mid, 5);
        }
        int Ring(int x, int y) => (int)MathF.Max(MathF.Abs(x - 7.5f), MathF.Abs(y - 7.5f));
        var dirtSide = new Color(120, 90, 70);

        Fill(Blocks.TileCrystal, (x, y) => Mottle(x, y, 10, new Color(196, 204, 216), new Color(224, 230, 238), new Color(246, 248, 252)));   // cuarzo
        Fill(Blocks.TileFungus, (x, y) => Mottle(x, y, 11, new Color(150,105,70), new Color(205,150,100), new Color(235,215,170)));
        Fill(Blocks.TileMetal, (x, y) =>
        {
            if (x % 8 == 2 && y % 8 == 2) return new Color(110, 88, 75);
            if (x % 8 == 0 || y % 8 == 0) return J(new Color(105, 82, 70), 4);
            return rnd.Next(5) == 0 ? J(new Color(175, 100, 52), 10) : J(new Color(122, 92, 72), 8);
        });
        Fill(Blocks.TileSalt, (x, y) => rnd.Next(14) == 0 ? J(new Color(170, 190, 205), 6) : J(new Color(236, 236, 230), 6));
        Fill(Blocks.TileAsh, (x, y) => Mottle(x, y, 14, new Color(140,133,126), new Color(150,143,136), new Color(172,165,158)));
        Fill(Blocks.TilePanel, (x, y) =>
        {
            if (x % 8 == 0 || y % 8 == 0) return J(new Color(100, 116, 130), 3);
            if (y >= 6 && y <= 8) return new Color(90, 235, 210);   // franja luminosa
            return J(new Color(145, 162, 176), 5);
        });
        Fill(Blocks.TileSandRed, (x, y) => Mottle(x, y, 16, new Color(185,112,78), new Color(205,130,90), new Color(222,150,108)));
        Fill(Blocks.TileSandBlack, (x, y) => Mottle(x, y, 17, new Color(150, 112, 70), new Color(176, 136, 88), new Color(204, 164, 108)));   // arena ocre (nada de arena blanca)
        Fill(Blocks.TileClay, (x, y) => Mottle(x, y, 18, new Color(160,98,70), new Color(176,112,80), new Color(192,128,94)));
        Fill(Blocks.TileDarkSoil, (x, y) => Mottle(x, y, 19, new Color(98,74,52), new Color(112,86,62), new Color(128,100,74)));
        Fill(Blocks.TileMossTop, (x, y) => rnd.Next(6) == 0 ? J(new Color(95, 165, 95), 8) : J(new Color(58, 128, 72), 10));
        Fill(Blocks.TileMossSide, (x, y) => y < 5 || (y == 5 && rnd.Next(2) == 0) ? J(new Color(58, 128, 72), 10) : J(dirtSide, 8));
        Fill(Blocks.TileLichenTop, (x, y) => rnd.Next(7) == 0 ? J(new Color(205, 215, 150), 8) : J(new Color(150, 170, 90), 10));
        Fill(Blocks.TileLichenSide, (x, y) => y < 4 || (y == 4 && rnd.Next(2) == 0) ? J(new Color(150, 170, 90), 10) : J(dirtSide, 8));
        Fill(Blocks.TileBasalt, (x, y) => Mottle(x, y, 24, new Color(98,74,66), new Color(122,94,84), new Color(140,110,98)));
        Fill(Blocks.TileStrata, (x, y) => Mottle(x, y, 25, new Color(150, 106, 84), new Color(168, 120, 95), new Color(188, 148, 115)));
        Fill(Blocks.TileChalk, (x, y) => rnd.Next(10) == 0 ? J(new Color(190, 186, 176), 6) : J(new Color(226, 222, 210), 6));
        Fill(Blocks.TileStemTop, (x, y) => Ring(x, y) % 3 == 0 ? J(new Color(200, 185, 160), 5) : J(new Color(230, 215, 190), 5));
        Fill(Blocks.TileStemSide, (x, y) => x % 4 == 0 ? J(new Color(170, 150, 125), 5) : J(new Color(222, 206, 180), 5));
        Fill(Blocks.TilePetrifiedTop, (x, y) => Ring(x, y) % 3 == 0 ? J(new Color(135, 131, 127), 4) : J(new Color(160, 156, 152), 4));
        Fill(Blocks.TilePetrifiedSide, (x, y) => y % 6 == 2 ? J(new Color(120, 116, 112), 4) : x % 3 == 0 ? J(new Color(128, 124, 120), 4) : J(new Color(150, 146, 142), 6));
        Fill(Blocks.TileFronds, (x, y) => (x + y) % 6 == 0 ? J(new Color(90, 190, 130), 6) : rnd.Next(4) == 0 ? J(new Color(24, 100, 70), 6) : J(new Color(48, 150, 100), 10));
        Fill(Blocks.TileSporeCap, (x, y) => rnd.Next(7) == 0 ? J(new Color(240, 230, 205), 6) : J(new Color(200, 90, 60), 10));
    }
}
