using GF.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace MiniCraft;

/// <summary>Genera un atlas 4x4 de tiles de 16px por código, para no depender de assets.</summary>
public static class AtlasFactory
{
    private const int T = 16, N = 4;

    /// <summary>
    /// Atlas de bloques desde el contenido compilado por MGCB (Content/blocks.png, 64x64: rejilla 4x4 de tiles de 16 px; edítalo con
    /// cualquier programa de dibujo). Si no está en el contenido (aún no se ha compilado), se genera por código como antes.
    /// </summary>
    public static TextureAtlas Load(ContentManager content, GraphicsDevice device)
    {
        try { return new TextureAtlas(content.Load<Texture2D>("blocks"), T); }
        catch (ContentLoadException e)
        {
            Console.Error.WriteLine("[contenido] no se pudo cargar 'blocks' (" + e.Message + "): se genera el atlas por código");
            return Create(device);
        }
    }

    /// <summary>Atlas generado por código: respaldo si falta el contenido compilado.</summary>
    public static TextureAtlas Create(GraphicsDevice device)
    {
        var data = new Color[N * T * N * T];
        var rnd = new Random(1234);

        Color Jitter(Color c, int amt = 14)
        {
            int d = rnd.Next(-amt, amt + 1);
            return new Color(Math.Clamp(c.R + d, 0, 255), Math.Clamp(c.G + d, 0, 255), Math.Clamp(c.B + d, 0, 255));
        }

        void Fill(int tile, Func<int, int, Color> pixel)
        {
            int ox = (tile % N) * T, oy = (tile / N) * T;
            for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
                data[(oy + y) * N * T + ox + x] = pixel(x, y);
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

        var tex = new Texture2D(device, N * T, N * T);
        tex.SetData(data);
        return new TextureAtlas(tex, T);
    }
}
