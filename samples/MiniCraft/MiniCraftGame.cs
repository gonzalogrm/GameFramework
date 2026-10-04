using GF.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace MiniCraft;

public sealed class MiniCraftGame : FrameworkGame
{
    public static readonly Color SkyColor = new(135, 180, 235);
    protected override Color ClearColor => SkyColor;

    protected override void Initialize()
    {
        Window.Title = "MiniCraft (GameFramework demo)";
        Input.Bind("Forward", Keys.W, Keys.Up);
        Input.Bind("Back", Keys.S, Keys.Down);
        Input.Bind("Left", Keys.A, Keys.Left);
        Input.Bind("Right", Keys.D, Keys.Right);
        Input.Bind("Jump", Keys.Space);
        Input.Bind("Sprint", Keys.LeftShift);
        Input.Bind("Pause", Keys.Escape);
        Input.Bind("Map", Keys.M);
        Input.Bind("Fly", Keys.F);                          // activar / desactivar el vuelo
        Input.Bind("AutoStep", Keys.G);                     // activar / desactivar el escalón automático
        Input.Bind("Descend", Keys.LeftControl, Keys.C);    // bajar mientras vuelas

        base.Initialize();
        Scenes.Push(new MainMenuScene());
    }
}
