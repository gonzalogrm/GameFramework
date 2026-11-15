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
/// Gas tóxico rojo: bancos de FogSystem (Perlin que evoluciona) con efecto físico en el juego. Usa todas las mecánicas de la niebla:
///  - DensityAt: cada segundo mide el gas en el cuerpo del jugador y de cada entidad activa (pies y torso) y les envía un evento "gas" con daño = DamagePerSecond x densidad.
///    El gas es una toxina: ignora la armadura. Las criaturas mueren si se quedan sin vida; el jugador reaparece como siempre.
///  - Entrar / salir (FogWatcher): las criaturas se asustan al entrar en el gas y se calman al salir; al jugador se le avisa.
///  - Sight: el gas también esconde de los enemigos (se combina con la niebla normal en EnemyAi.Visibility).
/// Los contenedores (cofres, alijos) y los mojones no se dañan. Solo hilo principal.
/// </summary>
public sealed class GasZone : IDisposable
{
    /// <summary>Daño por gas: hereda de "dano", así que quien acepta daño lo recibe a menos que tenga manejador propio para "gas" (como aquí).</summary>
    public static readonly EventKind Gas = GameEvents.Damage.Derive("gas");
    public static readonly EventKind Enter = new("entra_gas"), Leave = new("sale_gas");

    private const double Interval = 1.0;

    private readonly GasSettings _s;
    private readonly FogSystem _system;
    private readonly FogRenderer _view;
    private readonly FogWatcher _watcher;
    private readonly EventHub _hub;
    private readonly WorldScale _scale;
    private readonly PlayerState _player;
    private readonly List<Entity> _scratch = new();
    private double _timer, _life;
    private bool _playerIn;

    private GasZone(GasSettings s, FogSystem system, FogRenderer view, FogWatcher watcher, EventHub hub, WorldScale scale, PlayerState player)
    {
        _s = s; _system = system; _view = view; _watcher = watcher; _hub = hub; _scale = scale; _player = player;
    }

    public FogSystem System => _system;
    public string DebugText => _system.DebugText.Replace("Niebla", "Gas");

    /// <summary>Registra los manejadores (siempre) y crea el gas si está activado. Llamar después de GameEvents.Register y de crear el EventHub y el mapa de luz.</summary>
    public static GasZone? Create(GasSettings s, int seed, World<ushort> world, LightWorld light, EventHub hub, WorldScale scale, PlayerState player,
        GraphicsDevice device, int worldHeight)
    {
        Register(hub);
        if (!s.Enabled) return null;
        var system = new FogSystem(s.ToOptions(), new PerlinNoise(seed ^ 0x6A5), FogGround.For(world, Blocks.Registry, 0, worldHeight - 1))
        {
            LightQuery = FogLight.For(light),
        };
        var view = new FogRenderer(device) { BaseColor = new Vector3(s.Tint[0], s.Tint[1], s.Tint[2]), MinLight = 0.45f };
        var watcher = new FogWatcher(hub, system, scale, Enter, Leave) { EnterAbove = s.DamageThreshold, LeaveBelow = s.DamageThreshold * 0.5f };
        return new GasZone(s, system, view, watcher, hub, scale, player);
    }

    internal static void Register(EventHub hub)
    {
        hub.On<PlayerState>(EntityTypes.PlayerProto, Gas, PlayerBreathes);
        hub.On<Entity>(EntityTypes.Creature, Gas, CreatureBreathes);
        hub.On<Entity>(EntityTypes.Creature, Enter, (e, ev, h) => e.Set("fear", e.GetFloat("fear") + 1f));
        hub.On<Entity>(EntityTypes.Creature, Leave, (e, ev, h) => e.Set("fear", MathF.Max(0f, e.GetFloat("fear") - 1f)));
    }

    private static void PlayerBreathes(PlayerState p, GameEvent ev, EventHub hub)
    {
        if (p.GraceSeconds > 0f || p.Hp <= 0f) return;
        p.Hp = MathF.Max(0f, p.Hp - ev.Amount);   // la toxina ignora la armadura
        p.SecondsSinceHurt = 0f;
        hub.Notify($"Gas toxico: -{ev.Amount:0.#} HP");
    }

    private static void CreatureBreathes(Entity e, GameEvent ev, EventHub hub)
    {
        float hp = e.GetFloat("hp") - ev.Amount;
        if (hp > 0f) { e.Set("hp", hp); return; }
        hub.Notify($"{e.Label} muere intoxicado");
        hub.Entities.Remove(e);
    }

    internal static bool Susceptible(Entity e) =>
        e.Prototype?.HasComponent("container") != true && e.Def?.Name != "ruin_marker";

    /// <param name="camera">Posición de la cámara (la que usa el renderer, en coordenadas de los chunks cargados).</param>
    /// <param name="playerPos">Pies del jugador, en las mismas coordenadas.</param>
    public void Update(float dt, Vec3d camera, double time, Vec3d playerPos)
    {
        _system.Update(dt, camera, time);
        _watcher.Update(dt, playerPos);

        _life += dt;
        _timer += dt;
        if (_timer < Interval) return;
        _timer = 0;
        if (_life < _s.GraceSeconds) return;

        // Jugador: el cuerpo entero (pies y torso).
        float dp = MathF.Max(_system.DensityAt(playerPos + new Vec3d(0, 0.2, 0)), _system.DensityAt(playerPos + new Vec3d(0, 1.3, 0)));
        if (!_playerIn && dp > _s.DamageThreshold) { _playerIn = true; _hub.Notify("Entras en una nube de gas toxico"); }
        else if (_playerIn && dp < _s.DamageThreshold * 0.5f) { _playerIn = false; _hub.Notify("Sales del gas"); }
        if (dp > _s.DamageThreshold) _hub.Send(_player, new GameEvent(Gas, null, (float)(_s.DamagePerSecond * dp * Interval)));

        // Entidades activas (sus posiciones son canónicas: X relativa al jugador).
        _scratch.Clear();
        _scratch.AddRange(_hub.Entities.Active);   // copia: el daño puede eliminar entidades
        foreach (var e in _scratch)
        {
            if (!_hub.Entities.Contains(e) || !Susceptible(e)) continue;
            double x = playerPos.X + _scale.DeltaX(playerPos.X, e.Position.X);
            double h = e.Def?.Height ?? 1.8;
            float d = MathF.Max(_system.DensityAt(new Vec3d(x, e.Position.Y + 0.2, e.Position.Z)),
                                _system.DensityAt(new Vec3d(x, e.Position.Y + h * 0.7, e.Position.Z)));
            if (d > _s.DamageThreshold) _hub.Send(e, new GameEvent(Gas, null, (float)(_s.DamagePerSecond * d * Interval)));
        }
        _scratch.Clear();
    }

    public void Draw(ICamera3D camera) => _view.Draw(camera, _system);

    /// <summary>El terreno cambió en esa celda: el suelo de su columna de gas se vuelve a buscar.</summary>
    public void Invalidate(int wx, int wz) => _system.Invalidate(wx, wz);

    /// <summary>Visibilidad (alcance de visión) entre dos puntos a través del gas: se multiplica con la de la niebla.</summary>
    public float Sight(Vec3d from, Vec3d to) => _system.Sight(from, to);

    public void Dispose() => _view.Dispose();
}
