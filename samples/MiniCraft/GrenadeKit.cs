using GF.Core;
using GF.Engine;
using GF.World;
using GF.World.Entities;
using GF.World.Events;
using GF.World.Map;
using GF.World.Voxel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MiniCraft;

/// <summary>
/// Granadas: humo, toxica y esporas. Cada tipo es una nube de GasCloudField (autómata celular 3D) con su tinte y sus ajustes. Se LANZAN como proyectiles (gravedad,
/// rebote contra los bloques con VoxelPhysics, mecha) y al explotar sueltan el gas, que se expande y se disipa. Mecánicas de cada nube:
///  - Esconde: Sight reduce el alcance de visión de los enemigos (se combina con la niebla y el gas en EnemyAi.Visibility).
///  - Daño (DamagePerSecond > 0): cada segundo se mide DensityAt en el cuerpo del jugador y de cada entidad activa y se envía el evento de gas (ignora la armadura).
///  - Entrar / salir: las criaturas reciben los eventos de entrada y salida (se asustan); al jugador se le avisa.
/// Solo hilo principal.
/// </summary>
public sealed class GrenadeKit : IDisposable
{
    private const double Interval = 1.0;
    private static readonly Vector3 ShotSize = new(0.3f, 0.3f, 0.3f);

    private sealed class Cloud
    {
        public string Name = "", Label = "", ItemName = "";
        public SmokeSettings S = null!;
        public GasCloudField Field = null!;
        public GasCloudRenderer View = null!;
        public Vector3 Color;
        public bool PlayerIn;
        public readonly HashSet<long> Inside = new();
    }

    private struct Shot { public Vec3d Pos; public Vector3 Vel; public float Fuse; public int Cloud; }

    private readonly World<ushort> _world;
    private readonly BlockRegistry _blocks;
    private readonly EventHub _hub;
    private readonly WorldScale _scale;
    private readonly PlayerState _player;
    private readonly List<Cloud> _clouds;
    private readonly List<Shot> _shots = new();
    private readonly List<Entity> _scratch = new();
    private readonly HashSet<long> _present = new();
    private readonly BasicEffect _fx;
    private readonly VertexPositionColor[] _cube = new VertexPositionColor[8];
    private static readonly short[] CubeIdx = { 0,1,2, 0,2,3, 4,6,5, 4,7,6, 0,4,5, 0,5,1, 3,2,6, 3,6,7, 0,3,7, 0,7,4, 1,5,6, 1,6,2 };
    private int _selected;
    private double _timer;

    private GrenadeKit(World<ushort> world, BlockRegistry blocks, EventHub hub, WorldScale scale, PlayerState player, GraphicsDevice device, List<Cloud> clouds)
    {
        _world = world; _blocks = blocks; _hub = hub; _scale = scale; _player = player; _clouds = clouds;
        _fx = new BasicEffect(device) { VertexColorEnabled = true, TextureEnabled = false, LightingEnabled = false, FogEnabled = false };
    }

    /// <summary>Nombre de la granada seleccionada.</summary>
    public string SelectedName => _clouds[_selected].Name;
    public string DebugText => string.Join("\n", _clouds.Select(c => $"{c.Name}: {c.Field.ActiveCount} celdas, {c.Field.TotalAmount:0} de gas")) + $"\nGranadas en vuelo: {_shots.Count} (seleccionada: {SelectedName})";

    public static GrenadeKit? Create(SmokeSettings smoke, SmokeSettings toxic, SmokeSettings spores, World<ushort> world, BlockRegistry blocks, EventHub hub,
        WorldScale scale, PlayerState player, GraphicsDevice device)
    {
        GasZone.Register(hub);   // manejadores del daño de gas y de entrar / salir (los mismos que el gas de Perlin)
        var table = blocks.GetTable();
        var shape = world.Shape;
        bool Open(int x, int y, int z)
        {
            var cell = new CellCoord(x, y, z);
            return world.GetChunk(shape.ToChunk(cell)) != null && !table.Opaque[world.GetCell(cell)];
        }

        var clouds = new List<Cloud>();
        void Add(string name, string label, string item, SmokeSettings s)
        {
            if (!s.Enabled) return;
            var color = new Vector3(s.Tint[0], s.Tint[1], s.Tint[2]);
            clouds.Add(new Cloud
            {
                Name = name, Label = label, ItemName = item, S = s, Color = color,
                Field = new GasCloudField(s.ToOptions(), Open),
                View = new GasCloudRenderer(device) { BaseColor = color },
            });
        }
        Add("Humo", "una nube de humo", "smoke_grenade", smoke);
        Add("Granada toxica", "gas toxico", "toxic_grenade", toxic);
        Add("Esporas", "una nube de esporas", "spore_grenade", spores);
        return clouds.Count == 0 ? null : new GrenadeKit(world, blocks, hub, scale, player, device, clouds);
    }

    /// <summary>Cambia a la siguiente granada y devuelve su nombre.</summary>
    public string CycleType()
    {
        _selected = (_selected + 1) % _clouds.Count;
        return Status;
    }

    /// <summary>Lo que lleva el jugador de la granada seleccionada: "Humo x3".</summary>
    public string Status
    {
        get
        {
            var c = _clouds[_selected];
            return ItemTypes.Registry.TryGetId(c.ItemName, out ushort id) ? $"{c.Name} x{_player.Inventory.CountOf(id)}" : $"{c.Name} (falta '{c.ItemName}' en items.json)";
        }
    }

    /// <summary>
    /// Lanza la granada seleccionada desde 'origin' (los ojos) en la dirección 'forward', gastando una del inventario del jugador. Devuelve un mensaje si no se pudo
    /// (no tiene ninguna o el objeto no existe en items.json) o null si salió.
    /// </summary>
    public string? Throw(Vec3d origin, Vector3 forward)
    {
        var c = _clouds[_selected];
        if (!ItemTypes.Registry.TryGetId(c.ItemName, out ushort id)) return $"Falta el objeto '{c.ItemName}' en items.json";
        if (_player.Inventory.Remove(id, 1) == 0) return $"No te quedan granadas: {c.Name}";
        _shots.Add(new Shot
        {
            Pos = origin.Add(forward * 0.6f) - new Vec3d(0, 0.15, 0),
            Vel = forward * c.S.ThrowSpeed + new Vector3(0f, 3f, 0f),
            Fuse = c.S.FuseSeconds,
            Cloud = _selected,
        });
        return null;
    }

    public void Update(float dt, Vec3d playerPos)
    {
        foreach (var c in _clouds) c.Field.Update(dt);
        UpdateShots(dt);
        _timer += dt;
        if (_timer >= Interval) { _timer = 0; Tick(playerPos); }
    }

    private void UpdateShots(float dt)
    {
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var s = _shots[i];
            s.Fuse -= dt;
            s.Vel.Y -= 22f * dt;
            s.Pos = VoxelPhysics.MoveAndCollide(_world, _blocks, s.Pos, ShotSize, new Vec3d(s.Vel.X * dt, s.Vel.Y * dt, s.Vel.Z * dt), out var flags);
            if ((flags & CollisionFlags.X) != 0) s.Vel.X *= -0.4f;
            if ((flags & CollisionFlags.Z) != 0) s.Vel.Z *= -0.4f;
            if ((flags & CollisionFlags.Y) != 0)
            {
                if ((flags & CollisionFlags.Ground) != 0)
                {
                    s.Vel.Y = MathF.Abs(s.Vel.Y) < 2f ? 0f : -s.Vel.Y * 0.3f;   // rebota y se frena
                    s.Vel.X *= 0.7f;
                    s.Vel.Z *= 0.7f;
                }
                else s.Vel.Y = 0f;
            }

            if (s.Fuse <= 0f || s.Pos.Y < -20.0)
            {
                var c = _clouds[s.Cloud];
                if (s.Pos.Y >= -20.0) c.Field.Add(s.Pos + new Vec3d(0, 0.15, 0), c.S.Amount, c.S.InitialRadius);
                _shots.RemoveAt(i);
            }
            else _shots[i] = s;
        }
    }

    /// <summary>Una vez por segundo: daño y eventos de entrar / salir de cada nube que daña.</summary>
    private void Tick(Vec3d playerPos)
    {
        foreach (var c in _clouds)
        {
            if (c.S.DamagePerSecond <= 0f) continue;
            float thr = c.S.DamageThreshold;
            if (c.Field.ActiveCount == 0) { c.PlayerIn = false; c.Inside.Clear(); continue; }

            float dp = MathF.Max(c.Field.DensityAt(playerPos + new Vec3d(0, 0.2, 0)), c.Field.DensityAt(playerPos + new Vec3d(0, 1.3, 0)));
            if (!c.PlayerIn && dp > thr) { c.PlayerIn = true; _hub.Notify($"Entras en {c.Label}"); }
            else if (c.PlayerIn && dp < thr * 0.5f) { c.PlayerIn = false; _hub.Notify($"Sales de {c.Label}"); }
            if (dp > thr) _hub.Send(_player, new GameEvent(GasZone.Gas, null, (float)(c.S.DamagePerSecond * dp * Interval)));

            _scratch.Clear();
            _scratch.AddRange(_hub.Entities.Active);   // copia: el daño puede eliminar entidades
            _present.Clear();
            foreach (var e in _scratch)
            {
                if (!_hub.Entities.Contains(e) || !GasZone.Susceptible(e)) continue;
                _present.Add(e.Id);
                double x = playerPos.X + _scale.DeltaX(playerPos.X, e.Position.X), h = e.Def?.Height ?? 1.8;
                float d = MathF.Max(c.Field.DensityAt(new Vec3d(x, e.Position.Y + 0.2, e.Position.Z)),
                                    c.Field.DensityAt(new Vec3d(x, e.Position.Y + h * 0.7, e.Position.Z)));
                bool inside = c.Inside.Contains(e.Id);
                if (d > thr)
                {
                    if (!inside)
                    {
                        c.Inside.Add(e.Id);
                        _hub.Send(e, new GameEvent(GasZone.Enter, Amount: d));
                        if (!_hub.Entities.Contains(e)) continue;
                    }
                    _hub.Send(e, new GameEvent(GasZone.Gas, null, (float)(c.S.DamagePerSecond * d * Interval)));
                }
                else if (inside && d < thr * 0.5f)
                {
                    c.Inside.Remove(e.Id);
                    _hub.Send(e, new GameEvent(GasZone.Leave, Amount: d));
                }
            }
            c.Inside.RemoveWhere(id => !_present.Contains(id));
            _scratch.Clear();
        }
    }

    public void Draw(ICamera3D camera, Vector3 tint)
    {
        foreach (var c in _clouds)
        {
            c.View.Tint = tint;
            c.View.Draw(camera, c.Field);
        }
        DrawShots(camera);
    }

    private void DrawShots(ICamera3D camera)
    {
        if (_shots.Count == 0) return;
        var origin = camera.Position;
        _fx.World = Matrix.Identity;
        _fx.View = camera.ViewRelativeTo(origin);
        _fx.Projection = camera.Projection;
        _fx.CurrentTechnique.Passes[0].Apply();
        var device = _fx.GraphicsDevice;
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        device.RasterizerState = RasterizerState.CullNone;

        foreach (var s in _shots)
        {
            var center = new Vector3((float)(s.Pos.X - origin.X), (float)(s.Pos.Y - origin.Y) + 0.15f, (float)(s.Pos.Z - origin.Z));
            var col = new Color(_clouds[s.Cloud].Color * 0.9f);
            for (int k = 0; k < 8; k++)
                _cube[k] = new VertexPositionColor(center + new Vector3((k & 1) == 0 ? -0.15f : 0.15f, (k & 2) == 0 ? -0.15f : 0.15f, (k & 4) == 0 ? -0.15f : 0.15f), col);
            device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, _cube, 0, 8, CubeIdx, 0, 12);
        }
        device.RasterizerState = RasterizerState.CullCounterClockwise;
    }

    /// <summary>Visibilidad (alcance de visión) entre dos puntos a través de todas las nubes.</summary>
    public float Sight(Vec3d from, Vec3d to)
    {
        float v = 1f;
        foreach (var c in _clouds) v *= c.Field.Sight(from, to);
        return v;
    }

    public void Dispose()
    {
        foreach (var c in _clouds) c.View.Dispose();
        _fx.Dispose();
    }
}
