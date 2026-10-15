using GF.UI;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;

namespace MiniCraft;

public sealed class MainMenuScene : UiScene
{
    private TextBox _seedBox = null!;

    protected override Widget BuildUi()
    {
        var hasUniverse = SaveSystem.TryLoadUniverse() != null;
        var settings = WorldSettings.Load(out var messages, out var source);
        var scale = settings.CreateScale();

        _seedBox = new TextBox { Width = 260 };

        var items = new List<(string, Action)>();
        if (hasUniverse)
            items.Add(("Continuar", () =>
            {
                var session = UniverseSession.TryContinue(settings);
                if (session != null) Game.Scenes.Reset(session.ContinueScene());
            }));
        items.Add(("Nuevo universo", () =>
        {
            // Una semilla de universo: de ella salen, de forma determinista, sus galaxias, sistemas solares y planetas. Empiezas en el primer
            // planeta habitable. (Empezar uno nuevo borra el universo guardado.)
            SaveSystem.DeleteUniverse();
            var session = UniverseSession.Create(UniverseSession.ParseSeed(_seedBox.Text), settings);
            Game.Scenes.Reset(session.ContinueScene());
        }));
        items.Add(("Salir", () => Game.Exit()));

        var info = $"Configuracion: {source}\n" +
                   $"Cada planeta habitable: {scale.WidthBlocks} x {scale.HeightBlocks} bloques, {settings.WorldHeight} de alto " +
                   $"({settings.VerticalChunks} chunks), vision {settings.ViewDistance} chunks";
        if (messages.Count > 0) info += "\nAvisos:\n  " + string.Join("\n  ", messages.Take(6));

        var seedRow = new HorizontalStackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 90),
        };
        seedRow.Widgets.Add(new Label { Text = "Semilla del universo (vacío = aleatoria):", VerticalAlignment = VerticalAlignment.Center });
        seedRow.Widgets.Add(_seedBox);

        var root = new Panel();
        root.Widgets.Add(Ui.CenteredMenu("MiniCraft", items.ToArray()));
        root.Widgets.Add(seedRow);
        root.Widgets.Add(new Label
        {
            Text = info,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8),
        });
        return root;
    }
}
