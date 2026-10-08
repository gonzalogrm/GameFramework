using Microsoft.Xna.Framework;

namespace GF.Engine;

/// <summary>Clase base para los juegos: ventana, input y pila de escenas.</summary>
public abstract class FrameworkGame : Game
{
    public GraphicsDeviceManager Graphics { get; }
    public InputService Input { get; }
    public SceneManager Scenes { get; }

    protected virtual Color ClearColor => Color.CornflowerBlue;

    protected FrameworkGame()
    {
        Graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
        };
        IsMouseVisible = true;
        Content.RootDirectory = "Content";   // contenido compilado por el pipeline de MonoGame (MGCB)
        Window.AllowUserResizing = true;
        Input = new InputService(this);
        Scenes = new SceneManager(this);
    }

    protected override void Update(GameTime gameTime)
    {
        Input.Update();
        Scenes.Update(gameTime);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(ClearColor);
        Scenes.Draw(gameTime);
        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        Scenes.Clear();
        base.UnloadContent();
    }
}
