using GF.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RogueDemo;

/// <summary>Celda del mundo roguelike. Tile 0 = muro, así default(RogueCell) (chunk sin cargar) es un muro.</summary>
public struct RogueCell
{
    public byte Tile;
    public byte Flags;   // reservado: puertas, trampas, agua...
}

public static class RogueTiles
{
    public const byte Wall = 0, Floor = 1;
    public const int SpritePlayer = 2, SpriteEnemy = 3;

    public static readonly RogueCell WallCell = new() { Tile = Wall };
    public static readonly RogueCell FloorCell = new() { Tile = Floor };

    /// <summary>Atlas 4x4 de tiles de 16px generado por código.</summary>
    public static TextureAtlas CreateAtlas(GraphicsDevice device)
    {
        const int T = 16, N = 4;
        var data = new Color[N * T * N * T];
        var rnd = new Random(7);

        void Fill(int tile, Func<int, int, Color> pixel)
        {
            int ox = (tile % N) * T, oy = (tile / N) * T;
            for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
                data[(oy + y) * N * T + ox + x] = pixel(x, y);
        }
        Color Shade(int r, int g, int b, int amt)
        {
            int d = rnd.Next(-amt, amt + 1);
            return new Color(Math.Clamp(r + d, 0, 255), Math.Clamp(g + d, 0, 255), Math.Clamp(b + d, 0, 255));
        }

        Fill(Wall, (x, y) => (y % 8 == 0 || (x + (y / 8) * 4) % 8 == 0) ? Shade(70, 70, 80, 4) : Shade(110, 110, 125, 8));
        Fill(Floor, (_, _) => Shade(45, 40, 38, 6));
        Fill(SpritePlayer, (x, y) =>
        {
            float d = MathF.Sqrt((x - 7.5f) * (x - 7.5f) + (y - 7.5f) * (y - 7.5f));
            return d < 5.5f ? (d > 4.3f ? new Color(150, 110, 0) : new Color(255, 215, 50)) : Color.Transparent;
        });
        Fill(SpriteEnemy, (x, y) =>
        {
            float d = MathF.Sqrt((x - 7.5f) * (x - 7.5f) + (y - 7.5f) * (y - 7.5f));
            if (d >= 6f) return Color.Transparent;
            bool eye = y is 5 or 6 && (x is 5 or 10);
            return eye ? Color.Black : new Color(190, 50, 40);
        });

        var tex = new Texture2D(device, N * T, N * T);
        tex.SetData(data);
        return new TextureAtlas(tex, T);
    }
}
