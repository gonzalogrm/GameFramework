using GF.UI;
using GF.World;
using GF.World.Map;
using GF.World.Tiles;
using Microsoft.Xna.Framework;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;

namespace MiniCraft;

/// <summary>Mapamundi a pantalla completa. Pinchar en un punto lleva al jugador a esa columna del mundo.</summary>
public sealed class MapScene : UiScene
{
    private readonly World<MapCell> _map;
    private readonly WorldScale _scale;
    private readonly Func<Vector2> _playerColumn;
    private readonly Action<int, int> _teleport;
    private readonly Func<IEnumerable<MapMarker>> _extraMarkers;
    private readonly int _worldHeight;

    private WorldMapView _view = null!;
    private Label _info = null!;
    private string _lastText = "\0";

    public MapScene(World<MapCell> map, WorldScale scale, Func<Vector2> playerColumn, Action<int, int> teleport,
        Func<IEnumerable<MapMarker>> extraMarkers, int worldHeight)
    {
        _map = map; _scale = scale; _playerColumn = playerColumn; _teleport = teleport; _extraMarkers = extraMarkers;
        _worldHeight = worldHeight;
    }

    protected override Widget BuildUi()
    {
        var root = new Panel();
        _info = new Label { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8) };
        var help = new Label
        {
            Text = "Clic: viajar   Rueda: zoom   Boton derecho o WASD: mover   M o Esc: cerrar",
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8),
        };
        root.Widgets.Add(_info);
        root.Widgets.Add(help);
        return root;
    }

    public override void Load()
    {
        base.Load();
        Game.Input.SetMouseCaptured(false);
        _view = new WorldMapView(Game.GraphicsDevice, _map, _scale, ColorOf);
        var p = _playerColumn();
        _view.CenterOn(p.X, p.Y, 1f);
    }

    public override void Unload() => _view.Dispose();

    private Color ColorOf(MapCell c)
    {
        var b = Biomes.Registry.Get(c.Biome);
        float k = 0.78f + 0.22f * Math.Clamp(c.Elevation / _worldHeight, 0f, 1f);   // más oscuro cuanto más bajo
        return new Color((int)(b.R * k), (int)(b.G * k), (int)(b.B * k));
    }

    public override void Update(GameTime gameTime)
    {
        var input = Game.Input;
        if (input.Pressed("Map") || input.Pressed("Pause")) { Game.Scenes.Pop(); return; }

        var p = _playerColumn();
        _view.Markers.Clear();
        _view.Markers.AddRange(_extraMarkers());
        _view.Markers.Add(new MapMarker(p.X, p.Y, Color.Red, 12f));   // el jugador, encima de todo
        _view.Update(gameTime, input);

        if (input.LeftClicked && _view.HasHover)
        {
            _teleport(_view.HoverColumn.X, _view.HoverColumn.Y);
            Game.Scenes.Pop();
            return;
        }
        UpdateInfo();
    }

    private void UpdateInfo()
    {
        string text = "";
        if (_view.HasHover)
        {
            var c = _view.HoverCell;
            var r = _view.HoverRegion;
            var col = _view.HoverColumn;
            var chunk = _scale.ChunkOf(col.X, col.Y);
            text = $"Region ({r.X}, {r.Y}) - {Biomes.Registry.Get(c.Biome).Name}\n" +
                   $"Elevacion media {c.Elevation:0} (relieve {c.Relief:0}) | Temperatura {c.Temperature:P0} | " +
                   $"Humedad {c.Humidity:P0} | Agua {c.WaterFraction:P0}\n" +
                   $"Bloque ({col.X}, {col.Y}) | Chunk ({chunk.X}, {chunk.Z})";
        }
        if (text == _lastText) return;
        _info.Text = text;
        _lastText = text;
    }

    public override void Draw(GameTime gameTime)
    {
        Game.GraphicsDevice.Clear(new Color(10, 14, 24));
        _view.Draw();
        base.Draw(gameTime);
    }
}
