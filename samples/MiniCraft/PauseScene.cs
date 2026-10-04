using GF.UI;
using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;

namespace MiniCraft;

public sealed class PauseScene : UiScene
{
    public override bool IsOverlay => true;

    protected override Widget BuildUi() => Ui.Dim(Ui.CenteredMenu("Pausa",
        ("Continuar", () => Game.Scenes.Pop()),
        ("Guardar y menu principal", () => Game.Scenes.Reset(new MainMenuScene())),
        ("Salir", () => Game.Exit())));

    public override void Load()
    {
        base.Load();
        Game.Input.SetMouseCaptured(false);
    }

    public override void Update(GameTime gameTime)
    {
        if (Game.Input.Pressed("Pause")) Game.Scenes.Pop();
    }
}
