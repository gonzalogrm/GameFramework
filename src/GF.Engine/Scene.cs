using Microsoft.Xna.Framework;

namespace GF.Engine;

public abstract class Scene
{
    public FrameworkGame Game { get; private set; } = null!;

    /// <summary>Si es true, la escena de debajo también se dibuja (menú de pausa, diálogos).</summary>
    public virtual bool IsOverlay => false;
    /// <summary>Si es true, las escenas de debajo no se actualizan.</summary>
    public virtual bool BlocksUpdateBelow => true;

    internal void Attach(FrameworkGame game) => Game = game;

    public virtual void Load() { }
    /// <summary>Se llama cuando la escena vuelve a ser la superior tras un Pop.</summary>
    public virtual void OnResumed() { }
    public virtual void Update(GameTime gameTime) { }
    public virtual void Draw(GameTime gameTime) { }
    public virtual void Unload() { }
}

/// <summary>Pila de escenas. Los cambios se aplican al inicio del siguiente Update.</summary>
public sealed class SceneManager
{
    private readonly FrameworkGame _game;
    private readonly List<Scene> _stack = new();
    private readonly Queue<Action> _pending = new();

    public SceneManager(FrameworkGame game) => _game = game;

    public Scene? Top => _stack.Count > 0 ? _stack[^1] : null;

    public void Push(Scene scene) => _pending.Enqueue(() => Add(scene));

    public void Pop() => _pending.Enqueue(() =>
    {
        if (_stack.Count == 0) return;
        RemoveTop();
        Top?.OnResumed();
    });

    /// <summary>Sustituye la escena superior.</summary>
    public void Replace(Scene scene) => _pending.Enqueue(() =>
    {
        if (_stack.Count > 0) RemoveTop();
        Add(scene);
    });

    /// <summary>Descarta toda la pila y deja solo esta escena.</summary>
    public void Reset(Scene scene) => _pending.Enqueue(() =>
    {
        while (_stack.Count > 0) RemoveTop();
        Add(scene);
    });

    public void Clear()
    {
        _pending.Clear();
        while (_stack.Count > 0) RemoveTop();
    }

    public void Update(GameTime gameTime)
    {
        while (_pending.Count > 0) _pending.Dequeue().Invoke();
        if (_stack.Count == 0) return;

        int start = _stack.Count - 1;
        while (start > 0 && !_stack[start].BlocksUpdateBelow) start--;
        for (int i = start; i < _stack.Count; i++) _stack[i].Update(gameTime);
    }

    public void Draw(GameTime gameTime)
    {
        if (_stack.Count == 0) return;
        int start = _stack.Count - 1;
        while (start > 0 && _stack[start].IsOverlay) start--;
        for (int i = start; i < _stack.Count; i++) _stack[i].Draw(gameTime);
    }

    private void Add(Scene scene)
    {
        scene.Attach(_game);
        scene.Load();
        _stack.Add(scene);
    }

    private void RemoveTop()
    {
        var top = _stack[_stack.Count - 1];
        _stack.RemoveAt(_stack.Count - 1);
        top.Unload();
    }
}
