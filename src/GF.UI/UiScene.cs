using GF.Engine;
using Microsoft.Xna.Framework;
using Myra;
using Myra.Graphics2D.UI;

namespace GF.UI;

/// <summary>Escena con un Desktop de Myra. Sobrescribe BuildUi y, si dibujas algo debajo, llama a base.Draw al final.</summary>
public abstract class UiScene : Scene
{
    public Desktop Desktop { get; private set; } = null!;

    protected abstract Widget BuildUi();

    public override void Load()
    {
        MyraEnvironment.Game = Game;
        Desktop = new Desktop { Root = BuildUi() };
    }

    public override void Draw(GameTime gameTime) => Desktop.Render();

    /// <summary>true si el ratón está sobre un widget (para no disparar clics al mundo).</summary>
    public bool IsPointerOverUi => Desktop.IsMouseOverGUI;
}
