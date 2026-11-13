using GF.Core;
using GF.World.Entities;
using GF.World.Events;
using GF.World.Map;

namespace GF.World.Voxel;

/// <summary>
/// La niebla como disparador: cada 'Interval' segundos mide la densidad de niebla donde están las entidades activas y les envía un evento al cruzar un umbral (con
/// histéresis): 'enter' al entrar en la niebla (densidad &gt; EnterAbove) y 'leave' al salir (densidad &lt; LeaveBelow). El evento lleva la densidad en Amount. Cada tipo
/// decide si lo procesa (EventHub.On), como con cualquier otro evento: es el punto de partida de las mecánicas de niebla y gases (esconderse, toser, perder vida...).
/// </summary>
public sealed class FogWatcher
{
    private readonly EventHub _hub;
    private readonly FogSystem _fog;
    private readonly WorldScale _scale;
    private readonly EventKind _enter, _leave;
    private readonly Dictionary<long, bool> _inside = new();
    private readonly List<Entity> _scratch = new();
    private readonly HashSet<long> _present = new();
    private double _timer;

    public FogWatcher(EventHub hub, FogSystem fog, WorldScale scale, EventKind enter, EventKind leave)
    {
        _hub = hub; _fog = fog; _scale = scale; _enter = enter; _leave = leave;
    }

    public float EnterAbove { get; set; } = 0.25f;
    public float LeaveBelow { get; set; } = 0.10f;
    public double Interval { get; set; } = 0.5;

    /// <param name="focus">Posición del jugador: las entidades son canónicas (X envuelta) y la niebla está en las coordenadas de los chunks cargados.</param>
    public void Update(double dt, Vec3d focus)
    {
        _timer += dt;
        if (_timer < Interval) return;
        _timer = 0;

        _scratch.Clear();
        _scratch.AddRange(_hub.Entities.Active);   // copia: los manejadores pueden eliminar entidades
        _present.Clear();

        foreach (var e in _scratch)
        {
            if (!_hub.Entities.Contains(e)) continue;
            _present.Add(e.Id);

            double x = focus.X + _scale.DeltaX(focus.X, e.Position.X);
            float density = _fog.DensityAt(new Vec3d(x, e.Position.Y + 0.5, e.Position.Z));
            _inside.TryGetValue(e.Id, out bool inside);
            if (!inside && density > EnterAbove)
            {
                _inside[e.Id] = true;
                _hub.Send(e, new GameEvent(_enter, Amount: density));
            }
            else if (inside && density < LeaveBelow)
            {
                _inside[e.Id] = false;
                _hub.Send(e, new GameEvent(_leave, Amount: density));
            }
        }

        if (_inside.Count > _present.Count + 64)   // olvidar las entidades que ya no están activas
            foreach (var id in _inside.Keys.Where(id => !_present.Contains(id)).ToList()) _inside.Remove(id);
    }
}
