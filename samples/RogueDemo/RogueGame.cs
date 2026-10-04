using GF.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace RogueDemo;

public sealed class RogueGame : FrameworkGame
{
    protected override Color ClearColor => Color.Black;

    protected override void Initialize()
    {
        Window.Title = "RogueDemo (GameFramework)";
        Input.Bind("Up", Keys.W, Keys.Up, Keys.NumPad8);
        Input.Bind("Down", Keys.S, Keys.Down, Keys.NumPad2);
        Input.Bind("Left", Keys.A, Keys.Left, Keys.NumPad4);
        Input.Bind("Right", Keys.D, Keys.Right, Keys.NumPad6);
        Input.Bind("UpLeft", Keys.Q, Keys.NumPad7);
        Input.Bind("UpRight", Keys.E, Keys.NumPad9);
        Input.Bind("DownLeft", Keys.Z, Keys.NumPad1);
        Input.Bind("DownRight", Keys.C, Keys.NumPad3);
        Input.Bind("Wait", Keys.Space, Keys.NumPad5);
        Input.Bind("Restart", Keys.R);
        Input.Bind("Quit", Keys.Escape);

        base.Initialize();
        Scenes.Push(new RogueScene(Environment.TickCount));
    }
}
