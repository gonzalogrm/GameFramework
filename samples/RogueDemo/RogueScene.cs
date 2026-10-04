using GF.Core;
using GF.Engine;
using GF.UI;
using GF.World;
using GF.World.Tiles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;

namespace RogueDemo;

public sealed class RogueScene : UiScene
{
    private const int FovRadius = 10;
    private const int MaxHp = 10;
    private const int InitialChunks = 25;   // 5x5 chunks alrededor del origen

    private sealed class Enemy { public CellCoord Pos; public int Hp = 3; }

    private readonly int _seed;
    private readonly Random _rng;
    private readonly Camera2D _camera = new() { Zoom = 2f };
    private readonly HashSet<CellCoord> _visible = new();
    private readonly HashSet<CellCoord> _explored = new();
    private readonly List<Enemy> _enemies = new();
    private readonly List<string> _log = new();

    private World<RogueCell> _world = null!;
    private ChunkManager<RogueCell> _chunks = null!;
    private TilemapRenderer<RogueCell> _tiles = null!;
    private TextureAtlas _atlas = null!;
    private SpriteBatch _sb = null!;
    private Label _hud = null!, _logLabel = null!;

    private bool _ready, _logDirty = true;
    private CellCoord _player;
    private int _hp = MaxHp, _turn;
    private float _cooldown;
    private bool _held;

    public RogueScene(int seed)
    {
        _seed = seed;
        _rng = new Random(seed);
    }

    protected override Widget BuildUi()
    {
        var root = new Panel();
        _hud = new Label { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8) };
        _logLabel = new Label { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8) };
        root.Widgets.Add(_hud);
        root.Widgets.Add(_logLabel);
        return root;
    }

    public override void Load()
    {
        var generator = new WorldGenerator<RogueCell>(_seed)
            .AddStage(new CellularAutomataCaveStage<RogueCell>(RogueTiles.FloorCell, RogueTiles.WallCell, 0.45f, 4));

        // Mapa 2D: chunks de 32x32 con profundidad 1 (Z = 0).
        _world = new World<RogueCell>(new ChunkShape(32, 32, 1));
        _chunks = new ChunkManager<RogueCell>(_world, generator) { RadiusX = 2, RadiusY = 2, RadiusZ = 0 };

        _atlas = RogueTiles.CreateAtlas(Game.GraphicsDevice);
        _sb = new SpriteBatch(Game.GraphicsDevice);
        var dim = new Color(70, 70, 95);
        _tiles = new TilemapRenderer<RogueCell>(_atlas, (coord, cell) =>
            !_ready ? TileVisual.None
            : _visible.Contains(coord) ? new TileVisual(cell.Tile, Color.White)
            : _explored.Contains(coord) ? new TileVisual(cell.Tile, dim)
            : TileVisual.None);

        base.Load();
        AddLog("Generando mapa...");
    }

    public override void Unload()
    {
        _chunks.Dispose();
        _sb.Dispose();
    }

    // ------------------------------------------------------------------ update

    public override void Update(GameTime gameTime)
    {
        var input = Game.Input;
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        if (input.Pressed("Quit")) { Game.Exit(); return; }
        if (input.Pressed("Restart")) { Game.Scenes.Reset(new RogueScene(Environment.TickCount)); return; }

        _chunks.Update(_ready ? _player : new CellCoord(16, 16, 0));
        if (!_ready)
        {
            if (_world.LoadedChunkCount >= InitialChunks) StartGame();
            RefreshUi();
            return;
        }

        _camera.SetViewport(Game.GraphicsDevice.Viewport);
        _camera.FollowTarget = new Vector2(_player.X * _atlas.TileSize + 8, _player.Y * _atlas.TileSize + 8);
        _camera.Update(gameTime);

        if (_hp > 0) HandleTurn(input, dt);
        RefreshUi();
    }

    private void HandleTurn(InputService input, float dt)
    {
        int dx = 0, dy = 0;
        void Add(string action, int ax, int ay) { if (input.IsDown(action)) { dx += ax; dy += ay; } }
        Add("Up", 0, -1); Add("Down", 0, 1); Add("Left", -1, 0); Add("Right", 1, 0);
        Add("UpLeft", -1, -1); Add("UpRight", 1, -1); Add("DownLeft", -1, 1); Add("DownRight", 1, 1);
        dx = Math.Clamp(dx, -1, 1); dy = Math.Clamp(dy, -1, 1);

        bool wait = input.Pressed("Wait");
        bool moving = dx != 0 || dy != 0;
        _cooldown -= dt;
        if (!moving && !wait) { _held = false; _cooldown = 0; return; }
        if (_cooldown > 0 && !wait) return;

        bool acted = wait || TryPlayerMove(dx, dy);
        _cooldown = _held ? 0.08f : 0.2f;   // retardo inicial, luego repetición rápida
        _held = true;
        if (!acted) return;

        _turn++;
        ComputeFov();
        EnemiesAct();
        if (_hp <= 0) AddLog("Has muerto. Pulsa R para un mapa nuevo.");
    }

    private bool TryPlayerMove(int dx, int dy)
    {
        var target = new CellCoord(_player.X + dx, _player.Y + dy, _player.Z);

        var enemy = _enemies.FirstOrDefault(e => e.Pos == target);
        if (enemy != null)
        {
            int dmg = _rng.Next(1, 4);
            enemy.Hp -= dmg;
            if (enemy.Hp <= 0) { _enemies.Remove(enemy); AddLog($"Matas al goblin ({dmg} de daño)."); }
            else AddLog($"Golpeas al goblin ({dmg} de daño).");
            return true;
        }

        if (!IsFloor(_world.GetCell(target))) return false;
        // Sin cortar esquinas en diagonal.
        if (dx != 0 && dy != 0 &&
            (!IsFloor(_world.GetCell(new CellCoord(_player.X + dx, _player.Y, 0))) ||
             !IsFloor(_world.GetCell(new CellCoord(_player.X, _player.Y + dy, 0))))) return false;

        _player = target;
        return true;
    }

    private void EnemiesAct()
    {
        foreach (var e in _enemies.ToList())
        {
            int dist = Math.Max(Math.Abs(e.Pos.X - _player.X), Math.Abs(e.Pos.Y - _player.Y));
            if (dist > 20) continue;

            if (dist <= 1)
            {
                if (_rng.NextDouble() < 0.6) { _hp--; AddLog("Un goblin te golpea."); }
                else AddLog("Un goblin falla.");
                continue;
            }

            if (!_visible.Contains(e.Pos) && dist > 6) continue;   // solo persigue si lo ve o está cerca
            var path = AStar.FindPath(_world, e.Pos, _player, IsFloor, 600);
            if (path is { Count: > 1 })
            {
                var next = path[1];
                if (next != _player && !_enemies.Any(o => o.Pos == next)) e.Pos = next;
            }
        }
    }

    private void ComputeFov()
    {
        _visible.Clear();
        Fov.Compute(_world, _player, FovRadius, c => c.Tile == RogueTiles.Wall, cell =>
        {
            _visible.Add(cell);
            _explored.Add(cell);
        });
    }

    // ------------------------------------------------------------------ inicio

    private void StartGame()
    {
        if (!FindSpawn(out _player))
        {
            Game.Scenes.Reset(new RogueScene(_seed + 1));   // mapa sin zona amplia: probamos otra semilla
            return;
        }

        for (int attempts = 0; attempts < 600 && _enemies.Count < 14; attempts++)
        {
            var c = new CellCoord(_rng.Next(-40, 72), _rng.Next(-40, 72), 0);
            if (!IsFloor(_world.GetCell(c))) continue;
            if (Math.Max(Math.Abs(c.X - _player.X), Math.Abs(c.Y - _player.Y)) < 10) continue;
            if (_enemies.Any(e => e.Pos == c)) continue;
            _enemies.Add(new Enemy { Pos = c });
        }

        _camera.Position = new Vector2(_player.X * _atlas.TileSize + 8, _player.Y * _atlas.TileSize + 8);
        _ready = true;
        ComputeFov();
        _log.Clear();
        AddLog("Explora la cueva. Mueve contra un goblin para atacar.");
    }

    /// <summary>Busca en espiral una celda de suelo dentro de una zona conectada amplia.</summary>
    private bool FindSpawn(out CellCoord spawn)
    {
        for (int r = 0; r < 40; r++)
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
            var c = new CellCoord(16 + dx, 16 + dy, 0);
            if (IsFloor(_world.GetCell(c)) && ReachableCount(c, 150) >= 150) { spawn = c; return true; }
        }
        spawn = default;
        return false;
    }

    private int ReachableCount(CellCoord start, int limit)
    {
        var seen = new HashSet<CellCoord> { start };
        var queue = new Queue<CellCoord>();
        queue.Enqueue(start);
        (int X, int Y)[] dirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };
        while (queue.Count > 0 && seen.Count < limit)
        {
            var c = queue.Dequeue();
            foreach (var (dx, dy) in dirs)
            {
                var n = new CellCoord(c.X + dx, c.Y + dy, 0);
                if (IsFloor(_world.GetCell(n)) && seen.Add(n)) queue.Enqueue(n);
            }
        }
        return seen.Count;
    }

    // ------------------------------------------------------------------ draw / ui

    public override void Draw(GameTime gameTime)
    {
        _tiles.Draw(_sb, _world, _camera);

        if (_ready)
        {
            int ts = _atlas.TileSize;
            _sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: _camera.View);
            foreach (var e in _enemies)
                if (_visible.Contains(e.Pos))
                    _sb.Draw(_atlas.Texture, new Rectangle(e.Pos.X * ts, e.Pos.Y * ts, ts, ts),
                        _atlas.GetSourceRect(RogueTiles.SpriteEnemy), Color.White);
            _sb.Draw(_atlas.Texture, new Rectangle(_player.X * ts, _player.Y * ts, ts, ts),
                _atlas.GetSourceRect(RogueTiles.SpritePlayer), Color.White);
            _sb.End();
        }

        base.Draw(gameTime);   // HUD de Myra encima
    }

    private void AddLog(string message)
    {
        _log.Add(message);
        if (_log.Count > 4) _log.RemoveAt(0);
        _logDirty = true;
    }

    private void RefreshUi()
    {
        _hud.Text = $"HP {Math.Max(_hp, 0)}/{MaxHp}   Turno {_turn}   Goblins {_enemies.Count}   Semilla {_seed}\n" +
                    "Mover: WASD/flechas/QEZC   Esperar: Espacio   R: mapa nuevo   Esc: salir";
        if (_logDirty)
        {
            _logLabel.Text = string.Join("\n", _log);
            _logDirty = false;
        }
    }

    private static bool IsFloor(RogueCell c) => c.Tile != RogueTiles.Wall;
}
