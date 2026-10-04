using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GF.Engine;

/// <summary>Textura dividida en una rejilla de tiles cuadrados. Sirve a 3D (UVs) y 2D (source rects).</summary>
public sealed class TextureAtlas
{
    private const float Inset = 0.002f;

    public Texture2D Texture { get; }
    public int TileSize { get; }
    public int Columns { get; }
    public int Rows { get; }

    public TextureAtlas(Texture2D texture, int tileSize)
    {
        Texture = texture;
        TileSize = tileSize;
        Columns = texture.Width / tileSize;
        Rows = texture.Height / tileSize;
    }

    public Rectangle GetSourceRect(int index) =>
        new((index % Columns) * TileSize, (index / Columns) * TileSize, TileSize, TileSize);

    /// <summary>UV dentro del tile; local en [0,1]x[0,1].</summary>
    public Vector2 GetUV(int index, Vector2 local)
    {
        int col = index % Columns, row = index / Columns;
        return new Vector2(
            (col + MathHelper.Lerp(Inset, 1 - Inset, local.X)) / Columns,
            (row + MathHelper.Lerp(Inset, 1 - Inset, local.Y)) / Rows);
    }
}
