using GF.UI;
using GF.World.Cosmos;
using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;

namespace MiniCraft;

/// <summary>
/// Mapa del universo: galaxias -> sistemas solares -> planetas. Se abre con U desde el juego. En un planeta habitable se puede viajar (se guarda el
/// mundo actual y se abre el del destino); los demás se pueden consultar pero no jugar. Esc o U cierran.
/// </summary>
public sealed class UniverseScene : UiScene
{
    private const int PageSize = 10;

    private readonly UniverseSession _session;
    private readonly PlanetAddress? _current;
    private readonly Action<Planet> _travel;
    private VerticalStackPanel _list = null!;

    private Galaxy? _galaxy;
    private SolarSystem? _system;
    private int _page;

    public UniverseScene(UniverseSession session, PlanetAddress? current, Action<Planet> travel)
    {
        _session = session; _current = current; _travel = travel;
        // Se abre en el sistema donde se está.
        if (current is { } a && session.Universe.Galaxies.FirstOrDefault(g => g.Id == a.GalaxyId) is { } galaxy)
        {
            _galaxy = galaxy;
            _system = galaxy.Systems.FirstOrDefault(s => s.Id == a.SystemId);
            if (_system != null) _page = _system.Index / PageSize;
        }
    }

    public override bool IsOverlay => true;

    protected override Widget BuildUi()
    {
        _list = new VerticalStackPanel
        {
            Spacing = 5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Fill();
        return Ui.Dim(_list);
    }

    public override void Load()
    {
        base.Load();
        Game.Input.SetMouseCaptured(false);
    }

    public override void Update(GameTime gameTime)
    {
        if (Game.Input.Pressed("Universe") || Game.Input.Pressed("Pause")) Game.Scenes.Pop();
    }

    private static string KindTitle(PlanetKind k) => k switch
    {
        PlanetKind.Temperate => "templado",
        PlanetKind.Ocean => "oceánico",
        PlanetKind.Desert => "desértico",
        PlanetKind.Frozen => "helado",
        PlanetKind.Jungle => "selvático",
        PlanetKind.Barren => "yermo",
        PlanetKind.Lava => "de lava",
        PlanetKind.Toxic => "tóxico",
        _ => "gigante gaseoso",
    };

    private static string StarTitle(StarKind s) => s switch
    {
        StarKind.RedDwarf => "enana roja",
        StarKind.Orange => "naranja",
        StarKind.Yellow => "amarilla",
        StarKind.White => "blanca",
        _ => "azul",
    };

    private void Fill()
    {
        _list.Widgets.Clear();
        var universe = _session.Universe;
        _list.Widgets.Add(Ui.Title("Universo"));
        _list.Widgets.Add(new Label { Text = $"Semilla {universe.Id}   ({universe.Galaxies.Count} galaxias)", HorizontalAlignment = HorizontalAlignment.Center });

        if (_galaxy == null) FillGalaxies();
        else if (_system == null) FillSystems(_galaxy);
        else FillPlanets(_galaxy, _system);

        _list.Widgets.Add(Ui.Button("Cerrar", () => Game.Scenes.Pop(), 200));
    }

    private void FillGalaxies()
    {
        _list.Widgets.Add(new Label { Text = "Galaxias:" });
        foreach (var galaxy in _session.Universe.Galaxies)
        {
            var g = galaxy;
            _list.Widgets.Add(Ui.Button($"{g.Index + 1}. {g.Name}  ({g.Systems.Count} sistemas)", () => { _galaxy = g; _page = 0; Fill(); }, 420));
        }
    }

    private void FillSystems(Galaxy galaxy)
    {
        int pages = (galaxy.Systems.Count + PageSize - 1) / PageSize;
        _page = Math.Clamp(_page, 0, Math.Max(0, pages - 1));
        _list.Widgets.Add(new Label { Text = $"Galaxia {galaxy.Name}: sistemas solares (página {_page + 1}/{pages})" });
        foreach (var system in galaxy.Systems.Skip(_page * PageSize).Take(PageSize))
        {
            var s = system;
            string here = _current?.SystemId == s.Id ? "  <- estás aquí" : "";
            _list.Widgets.Add(Ui.Button($"{s.Index + 1}. {s.Name} (estrella {StarTitle(s.Star)}): {s.Planets.Count} planetas, {s.HabitableCount} habitables{here}",
                () => { _system = s; Fill(); }, 560));
        }
        var pager = new HorizontalStackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        pager.Widgets.Add(Ui.Button("< Anterior", () => { _page--; Fill(); }, 140));
        pager.Widgets.Add(Ui.Button("Siguiente >", () => { _page++; Fill(); }, 140));
        pager.Widgets.Add(Ui.Button("Galaxias", () => { _galaxy = null; Fill(); }, 140));
        _list.Widgets.Add(pager);
    }

    private void FillPlanets(Galaxy galaxy, SolarSystem system)
    {
        _list.Widgets.Add(new Label { Text = $"{galaxy.Name} / {system.Name}  (estrella {StarTitle(system.Star)}): {system.Planets.Count} planetas" });
        foreach (var planet in system.Planets)
        {
            var p = planet;
            _list.Widgets.Add(new Label
            {
                Text = $"{p.Index + 1}. {p.Name}: {KindTitle(p.Kind)}, radio {p.RadiusEarths:0.0} (tierras), {p.TemperatureC:0} °C, {p.Moons} lunas, órbita {p.OrbitAu:0.0} UA\n" +
                       $"    ID {p.Address}",
            });
            if (!p.Habitable) _list.Widgets.Add(new Label { Text = "    No habitable: no se puede jugar aquí." });
            else if (_current?.PlanetId == p.Id && _current?.SystemId == system.Id) _list.Widgets.Add(new Label { Text = "    Estás aquí." });
            else _list.Widgets.Add(Ui.Button($"Viajar a {p.Name} (habitable)", () => _travel(p), 360));
        }
        _list.Widgets.Add(Ui.Button("Sistemas de la galaxia", () => { _system = null; Fill(); }, 260));
    }
}
