using GF.UI;
using GF.World.Entities;
using GF.World.History;
using GF.World.Items;
using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;

namespace MiniCraft;

/// <summary>
/// Depuración de la historia del mundo (tecla H): las épocas con sus héroes y la lista de objetos legendarios con DÓNDE están ahora (qué enemigo los lleva,
/// en qué alijo, o en qué coordenadas aún sin generar). "Ir" te lleva a esas coordenadas (así se genera la columna y se comprueba que el objeto existe).
/// Esc o H cierran.
/// </summary>
public sealed class HistoryScene : UiScene
{
    private readonly WorldHistory _history;
    private readonly LegendaryRegistry _legendary;
    private readonly EntityStore _store;
    private readonly PlayerState _player;
    private readonly EnemySpawner _spawner;
    private readonly Action<int, int> _teleport;
    private VerticalStackPanel _list = null!;

    public HistoryScene(WorldHistory history, LegendaryRegistry legendary, EntityStore store, PlayerState player, EnemySpawner spawner, Action<int, int> teleport)
    {
        _history = history; _legendary = legendary; _store = store; _player = player; _spawner = spawner; _teleport = teleport;
    }

    public override bool IsOverlay => true;

    protected override Widget BuildUi()
    {
        _list = new VerticalStackPanel { Spacing = 4 };
        Fill();
        var scroll = new ScrollViewer
        {
            Content = _list,
            Width = 980,
            Height = 600,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        return Ui.Dim(scroll);
    }

    public override void Load()
    {
        base.Load();
        Game.Input.SetMouseCaptured(false);
    }

    public override void Update(GameTime gameTime)
    {
        if (Game.Input.Pressed("History") || Game.Input.Pressed("Pause")) Game.Scenes.Pop();
    }

    private void Fill()
    {
        var defs = ItemTypes.Registry;
        _list.Widgets.Clear();
        _list.Widgets.Add(Ui.Title("Historia del mundo"));

        foreach (var epoch in _history.Epochs)
            _list.Widgets.Add(new Label
            {
                Text = $"Época {epoch.Index + 1}: {epoch.Civilization}  (años {epoch.StartYear}-{epoch.EndYear}, terminó por {epoch.Ending})\n" +
                       $"      Héroes: {string.Join(", ", epoch.Heroes)}",
            });

        _list.Widgets.Add(new Label { Text = $"\nObjetos legendarios ({_legendary.Artifacts.Count}):" });
        _list.Widgets.Add(Ui.Button("Actualizar", Fill, 160));
        if (_legendary.Artifacts.Count == 0) _list.Widgets.Add(new Label { Text = "  (ninguno: ningún héroe forjó un arma en este mundo)" });

        foreach (var a in _legendary.Artifacts)
        {
            var loc = _legendary.Locate(a, _store, _player, _spawner.IsPopulated);
            var def = defs.Get(a.ItemId);
            _list.Widgets.Add(new Label
            {
                Text = $"* {a.Title}   ataque {a.Attack:0.#}{(def.Components.Count > 0 ? "  [" + string.Join(", ", def.Components) + "]" : "")}\n" +
                       $"      Forjado por {a.Hero} en la época {a.Epoch.Index + 1} ({a.Epoch.Civilization}).\n" +
                       $"      [{loc.State}] {loc.Description}",
            });
            if (loc.Position is { } p)
            {
                int x = (int)Math.Floor(p.X), z = (int)Math.Floor(p.Z);
                _list.Widgets.Add(Ui.Button($"Ir a x {x}, z {z}", () => { Game.Scenes.Pop(); _teleport(x, z); }, 260));
            }
        }
        _list.Widgets.Add(Ui.Button("Cerrar", () => Game.Scenes.Pop(), 200));
    }
}
