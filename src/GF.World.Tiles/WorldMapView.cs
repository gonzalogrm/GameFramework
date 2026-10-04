using GF.Core;
using GF.Engine;
using GF.World.Map;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GF.World.Tiles;

/// <summary>Marcador sobre el mapa, en coordenadas de bloque del mundo (jugador, ciudades, caravanas...).</summary>
public readonly record struct MapMarker(float WorldX, float WorldZ, Color Color, float SizePx = 10f);

/// <summary>
/// Vista 2D del mapamundi: dibuja el IWorld&lt;MapCell&gt; con TilemapRenderer + Camera2D, con relieve
/// (sombreado de ladera), repetición en X (mundo cilíndrico), zoom hasta el nivel de chunk, selección
/// por cursor y marcadores. No decide qué ocurre al pinchar: expone HoverColumn y lo gestiona la escena.
/// </summary>
public sealed class WorldMapView : IDisposable
{
    public const int TileSize = 8;
    public const float MaxZoom = 64f;

    private readonly GraphicsDevice _device;
    private readonly World<MapCell> _map;
    private readonly WorldScale _scale;
    private readonly Func<MapCell, Color> _colorOf;
    private readonly Texture2D _white;
    private readonly TextureAtlas _atlas;
    private readonly SpriteBatch _sb;
    private readonly TilemapRenderer<MapCell> _tiles;
    private Color[] _tint = Array.Empty<Color>();
    private float _hoverCopyOffset;

    public Camera2D Camera { get; } = new();
    public List<MapMarker> Markers { get; } = new();
    public float MinZoom { get; private set; } = 0.1f;

    public bool HasHover { get; private set; }
    /// <summary>Región bajo el cursor (X = rx, Y = rz).</summary>
    public Point HoverRegion { get; private set; }
    /// <summary>Columna de bloque bajo el cursor, con precisión de bloque (X = wx, Y = wz).</summary>
    public Point HoverColumn { get; private set; }
    public MapCell HoverCell { get; private set; }

    public WorldMapView(GraphicsDevice device, World<MapCell> map, WorldScale scale, Func<MapCell, Color> colorOf)
    {
        _device = device; _map = map; _scale = scale; _colorOf = colorOf;

        _white = new Texture2D(device, TileSize, TileSize);
        var px = new Color[TileSize * TileSize];
        Array.Fill(px, Color.White);
        _white.SetData(px);
        _atlas = new TextureAtlas(_white, TileSize);
        _sb = new SpriteBatch(device);
        _tiles = new TilemapRenderer<MapCell>(_atlas, (coord, _) => TileAt(coord));
        Refresh();
    }

    /// <summary>Recalcula colores y relieve (llámalo si cambian los datos del mapa).</summary>
    public void Refresh()
    {
        int w = _scale.MapWidth, h = _scale.MapHeight;
        _tint = new Color[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var baseColor = _colorOf(_map.GetCell(new CellCoord(x, y, 0)));
            // Sombreado de ladera con luz desde el noroeste: más claro si el NO está más alto que el SE.
            float shade = Math.Clamp(1f + (Elevation(x - 1, y - 1) - Elevation(x + 1, y + 1)) * 0.06f, 0.6f, 1.4f);
            _tint[y * w + x] = new Color(
                (int)Math.Clamp(baseColor.R * shade, 0, 255),
                (int)Math.Clamp(baseColor.G * shade, 0, 255),
                (int)Math.Clamp(baseColor.B * shade, 0, 255));
        }
    }

    private float Elevation(int x, int y)
    {
        int w = _scale.MapWidth;
        x = ((x % w) + w) % w;
        y = Math.Clamp(y, 0, _scale.MapHeight - 1);
        return _map.GetCell(new CellCoord(x, y, 0)).Elevation;
    }

    private TileVisual TileAt(CellCoord c)
    {
        if ((uint)c.Y >= (uint)_scale.MapHeight) return TileVisual.None;
        int w = _scale.MapWidth;
        int x = ((c.X % w) + w) % w;
        return new TileVisual(0, _tint[c.Y * w + x]);
    }

    /// <summary>Centra la cámara en una columna de bloque del mundo.</summary>
    public void CenterOn(float worldX, float worldZ, float zoom)
    {
        Camera.Position = new Vector2(
            _scale.WrapX(worldX) / _scale.RegionBlocksX * TileSize,
            worldZ / _scale.RegionBlocksZ * TileSize);
        Camera.Zoom = zoom;
    }

    public void Update(GameTime gameTime, InputService input)
    {
        var vp = _device.Viewport;
        Camera.SetViewport(vp);
        float mapW = _scale.MapWidth * TileSize, mapH = _scale.MapHeight * TileSize;
        MinZoom = vp.Height * 0.95f / mapH;
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // Zoom alrededor del cursor.
        var mouse = input.MousePosition.ToVector2();
        float notches = input.ScrollDelta / 120f;
        if (notches != 0)
        {
            var before = Camera.ScreenToWorld(mouse);
            Camera.Zoom = MathHelper.Clamp(Camera.Zoom * MathF.Pow(1.25f, notches), MinZoom, MaxZoom);
            var after = Camera.ScreenToWorld(mouse);
            Camera.Position += before - after;
        }
        else Camera.Zoom = MathHelper.Clamp(Camera.Zoom, MinZoom, MaxZoom);

        // Mover: arrastrar con botón derecho/central, o WASD.
        if (input.RightDown || input.MiddleDown) Camera.Position -= input.MouseMove.ToVector2() / Camera.Zoom;
        var dir = Vector2.Zero;
        if (input.IsDown("Forward")) dir.Y -= 1;
        if (input.IsDown("Back")) dir.Y += 1;
        if (input.IsDown("Left")) dir.X -= 1;
        if (input.IsDown("Right")) dir.X += 1;
        if (dir != Vector2.Zero)
        {
            dir.Normalize();
            Camera.Position += dir * (600f / Camera.Zoom) * dt;
        }

        // Límites: Z limitado (polos), X se envuelve.
        var p = Camera.Position;
        p.Y = MathHelper.Clamp(p.Y, 0, mapH);
        p.X -= MathF.Floor(p.X / mapW) * mapW;
        Camera.Position = p;

        UpdateHover(mouse, mapW);
    }

    private void UpdateHover(Vector2 mouse, float mapW)
    {
        var world = Camera.ScreenToWorld(mouse);
        float tx = world.X / TileSize, ty = world.Y / TileSize;
        if (ty < 0 || ty >= _scale.MapHeight) { HasHover = false; return; }

        float copy = MathF.Floor(tx / _scale.MapWidth);
        _hoverCopyOffset = copy * mapW;
        float wrapped = tx - copy * _scale.MapWidth;

        int rx = Math.Clamp((int)wrapped, 0, _scale.MapWidth - 1);
        int rz = Math.Clamp((int)ty, 0, _scale.MapHeight - 1);
        HoverRegion = new Point(rx, rz);
        HoverColumn = new Point(
            Math.Clamp((int)(wrapped * _scale.RegionBlocksX), 0, _scale.WidthBlocks - 1),
            Math.Clamp((int)(ty * _scale.RegionBlocksZ), 0, _scale.HeightBlocks - 1));
        HoverCell = _map.GetCell(new CellCoord(rx, rz, 0));
        HasHover = true;
    }

    public void Draw()
    {
        _tiles.Draw(_sb, _map, Camera);

        float zoom = Camera.Zoom;
        float tilePx = TileSize * zoom;
        float line = 1f / zoom;
        float mapH = _scale.MapHeight * TileSize;
        var vb = Camera.VisibleBounds;

        _sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: Camera.View);

        // Rejilla de regiones (solo si los tiles son lo bastante grandes para que no sea ruido).
        if (tilePx >= 20f)
        {
            var grid = Color.Black * 0.25f;
            int x0 = IntMath.FloorDiv(vb.Left, TileSize), x1 = IntMath.FloorDiv(vb.Right, TileSize) + 1;
            for (int x = x0; x <= x1; x++) Fill(x * TileSize - line * 0.5f, 0, line, mapH, grid);
            for (int y = 0; y <= _scale.MapHeight; y++) Fill(vb.Left, y * TileSize - line * 0.5f, vb.Width, line, grid);
        }

        if (HasHover) DrawHover(tilePx, line);

        foreach (var m in Markers)
        {
            float mx = _scale.WrapX(m.WorldX) / _scale.RegionBlocksX * TileSize;
            float mz = m.WorldZ / _scale.RegionBlocksZ * TileSize;
            float s = m.SizePx / zoom;
            Fill(mx - s * 0.5f - line, mz - s * 0.5f - line, s + 2 * line, s + 2 * line, Color.Black);
            Fill(mx - s * 0.5f, mz - s * 0.5f, s, s, m.Color);
        }
        _sb.End();
    }

    private void DrawHover(float tilePx, float line)
    {
        float ox = _hoverCopyOffset + HoverRegion.X * TileSize;
        float oy = HoverRegion.Y * TileSize;
        float t = 2f * line;
        var c = Color.White;
        Fill(ox, oy, TileSize, t, c);
        Fill(ox, oy + TileSize - t, TileSize, t, c);
        Fill(ox, oy, t, TileSize, c);
        Fill(ox + TileSize - t, oy, t, TileSize, c);

        // Con mucho zoom: rejilla de chunks dentro de la región y chunk bajo el cursor resaltado.
        if (tilePx >= 300f)
        {
            var faint = Color.White * 0.25f;
            float cw = TileSize / (float)_scale.RegionChunksX, ch = TileSize / (float)_scale.RegionChunksZ;
            for (int i = 1; i < _scale.RegionChunksX; i++) Fill(ox + i * cw - line * 0.5f, oy, line, TileSize, faint);
            for (int j = 1; j < _scale.RegionChunksZ; j++) Fill(ox, oy + j * ch - line * 0.5f, TileSize, line, faint);

            int cx = (HoverColumn.X % _scale.RegionBlocksX) / _scale.ChunkSizeX;
            int cz = (HoverColumn.Y % _scale.RegionBlocksZ) / _scale.ChunkSizeZ;
            Fill(ox + cx * cw, oy + cz * ch, cw, ch, Color.White * 0.3f);
        }
    }

    private void Fill(float x, float y, float w, float h, Color color) =>
        _sb.Draw(_white, new Vector2(x, y), new Rectangle(0, 0, 1, 1), color, 0f, Vector2.Zero, new Vector2(w, h), SpriteEffects.None, 0f);

    public void Dispose()
    {
        _sb.Dispose();
        _white.Dispose();
    }
}
