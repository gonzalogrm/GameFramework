using GF.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace MiniCraft;

public sealed class MiniCraftGame : FrameworkGame
{
    /// <summary>Color del cielo en el horizonte (fondo y niebla): lo actualiza el ciclo día/noche cada frame.</summary>
    public static Color SkyColor { get; set; } = new(135, 180, 235);
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
        Input.Bind("Talk", Keys.T);                         // emitir un evento de conversación al objetivo
        Input.Bind("Fireball", Keys.B);                     // emitir una bola de fuego sobre el objetivo
        Input.Bind("Night", Keys.N);                        // velocidad del ciclo día/noche: normal, rápido, parado
        Input.Bind("Inventory", Keys.E);                   // inventario y equipamiento
        Input.Bind("Inspect", Keys.I);                      // panel de propiedades del bloque o la entidad apuntados

        base.Initialize();
        Scenes.Push(new MainMenuScene());
    }
}
