using GF.UI;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;

namespace MiniCraft;

public sealed class MainMenuScene : UiScene
{
    protected override Widget BuildUi()
    {
        var save = SaveSystem.TryLoad();
        var settings = WorldSettings.Load(out var messages, out var source);
        var scale = settings.CreateScale();

        var items = new List<(string, Action)>();
        if (save != null)
            items.Add(("Continuar", () => Game.Scenes.Reset(new PlayScene(save.Seed, save.Settings, save))));
        items.Add(("Nuevo mundo", () =>
        {
            SaveSystem.Delete();
            Game.Scenes.Reset(new PlayScene(Environment.TickCount, settings));
        }));
        items.Add(("Salir", () => Game.Exit()));

        var info = $"Configuracion: {source}\n" +
                   $"Mundo nuevo: {scale.WidthBlocks} x {scale.HeightBlocks} bloques, {settings.WorldHeight} de alto " +
                   $"({settings.VerticalChunks} chunks), vision {settings.ViewDistance} chunks";
        if (messages.Count > 0) info += "\nAvisos:\n  " + string.Join("\n  ", messages.Take(6));

        var root = new Panel();
        root.Widgets.Add(Ui.CenteredMenu("MiniCraft", items.ToArray()));
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
