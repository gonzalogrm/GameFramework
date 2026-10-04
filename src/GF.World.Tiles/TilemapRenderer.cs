using GF.Core;
using GF.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GF.World.Tiles;

public readonly record struct TileVisual(int TileIndex, Color Tint)
{
    /// <summary>No dibujar nada en esta celda.</summary>
    public static readonly TileVisual None = new(-1, Color.White);
}

/// <summary>
/// Dibuja un plano Z del mundo con SpriteBatch. La función visualOf traduce celda (y coordenada,
/// para visibilidad/niebla) a tile+color, así que el mismo IWorld sirve para cualquier roguelike.
/// </summary>
public sealed class TilemapRenderer<TCell> where TCell : unmanaged
{
    private readonly TextureAtlas _atlas;
    private readonly Func<CellCoord, TCell, TileVisual> _visualOf;

    public TilemapRenderer(TextureAtlas atlas, Func<TCell, TileVisual> visualOf)
        : this(atlas, (_, cell) => visualOf(cell)) { }

    public TilemapRenderer(TextureAtlas atlas, Func<CellCoord, TCell, TileVisual> visualOf)
    {
        _atlas = atlas;
        _visualOf = visualOf;
    }

    public void Draw(SpriteBatch sb, IWorld<TCell> world, Camera2D camera, int z = 0)
    {
        int ts = _atlas.TileSize;
        var b = camera.VisibleBounds;
        int x0 = IntMath.FloorDiv(b.Left, ts), x1 = IntMath.FloorDiv(b.Right, ts);
        int y0 = IntMath.FloorDiv(b.Top, ts), y1 = IntMath.FloorDiv(b.Bottom, ts);

        sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: camera.View);
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            var coord = new CellCoord(x, y, z);
            var vis = _visualOf(coord, world.GetCell(coord));
            if (vis.TileIndex < 0) continue;
            sb.Draw(_atlas.Texture, new Rectangle(x * ts, y * ts, ts, ts), _atlas.GetSourceRect(vis.TileIndex), vis.Tint);
        }
        sb.End();
    }
}
