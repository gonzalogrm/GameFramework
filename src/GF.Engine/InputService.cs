using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace GF.Engine;

public sealed class InputService
{
    private readonly Game _game;
    private readonly Dictionary<string, Keys[]> _actions = new();
    private KeyboardState _prevKb, _kb;
    private MouseState _prevMouse, _mouse;
    private bool _skipNextDelta;

    public InputService(Game game) => _game = game;

    public Point MouseDelta { get; private set; }
    public bool MouseCaptured { get; private set; }
    public Point MousePosition => _mouse.Position;
    public int ScrollDelta => _mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
    public bool LeftDown => _mouse.LeftButton == ButtonState.Pressed;
    public bool RightDown => _mouse.RightButton == ButtonState.Pressed;
    public bool MiddleDown => _mouse.MiddleButton == ButtonState.Pressed;
    /// <summary>Desplazamiento del cursor respecto al frame anterior (con el ratón sin capturar).</summary>
    public Point MouseMove => _mouse.Position - _prevMouse.Position;
    public bool LeftClicked => _mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
    public bool RightClicked => _mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released;

    public void Bind(string action, params Keys[] keys) => _actions[action] = keys;

    public bool IsDown(string action) =>
        _actions.TryGetValue(action, out var ks) && ks.Any(k => _kb.IsKeyDown(k));

    public bool Pressed(string action) =>
        _actions.TryGetValue(action, out var ks) && ks.Any(k => _kb.IsKeyDown(k) && _prevKb.IsKeyUp(k));

    public bool KeyDown(Keys key) => _kb.IsKeyDown(key);
    public bool KeyPressed(Keys key) => _kb.IsKeyDown(key) && _prevKb.IsKeyUp(key);

    /// <summary>Oculta el cursor y lo mantiene centrado; MouseDelta da el movimiento relativo.</summary>
    public void SetMouseCaptured(bool captured)
    {
        if (MouseCaptured == captured) return;
        MouseCaptured = captured;
        _game.IsMouseVisible = !captured;
        _skipNextDelta = true;
    }

    public void Update()
    {
        _prevKb = _kb; _kb = Keyboard.GetState();
        _prevMouse = _mouse; _mouse = Mouse.GetState();
        MouseDelta = Point.Zero;

        if (MouseCaptured && _game.IsActive)
        {
            var vp = _game.GraphicsDevice.Viewport;
            var center = new Point(vp.Width / 2, vp.Height / 2);
            if (_skipNextDelta) _skipNextDelta = false;
            else MouseDelta = _mouse.Position - center;
            Mouse.SetPosition(center.X, center.Y);
        }
    }
}
