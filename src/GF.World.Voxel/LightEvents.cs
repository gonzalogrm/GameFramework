using GF.Core;
using GF.World.Entities;
using GF.World.Events;
using GF.World.Map;

namespace GF.World.Voxel;

/// <summary>
/// La luz como disparador: cada 'Interval' segundos mide el nivel de luz de las entidades activas y les envía un evento al cruzar un
/// umbral (con histéresis): 'dark' al entrar en la oscuridad (nivel &lt; DarkBelow) y 'lit' al volver a la luz (nivel &gt; LitAbove).
/// El evento lleva el nivel medido en Amount. Cada tipo decide si lo procesa (EventHub.On), como con cualquier otro evento.
/// </summary>
public sealed class LightWatcher
{
    private readonly EventHub _hub;
    private readonly LightWorld _light;
    private readonly WorldScale _scale;
    private readonly EventKind _dark, _lit;
    private readonly Dictionary<long, bool> _isDark = new();
    private readonly List<Entity> _scratch = new();
    private readonly HashSet<long> _present = new();
    private double _timer;

    public LightWatcher(EventHub hub, LightWorld light, WorldScale scale, EventKind dark, EventKind lit)
    {
        _hub = hub; _light = light; _scale = scale; _dark = dark; _lit = lit;
    }

    public float DarkBelow { get; set; } = 3.5f;
    public float LitAbove { get; set; } = 6f;
    public double Interval { get; set; } = 0.5;

    /// <param name="focus">Posición del jugador: las entidades son canónicas (X envuelta) y la luz está en las coordenadas de los chunks cargados.</param>
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
            var lv = _light.Sample(new CellCoord(IntMath.FloorToInt(x), IntMath.FloorToInt(e.Position.Y + 0.5), IntMath.FloorToInt(e.Position.Z)));
            if (!lv.Known) continue;

            float level = lv.Level;
            _isDark.TryGetValue(e.Id, out bool dark);
            if (!dark && level < DarkBelow)
            {
                _isDark[e.Id] = true;
                _hub.Send(e, new GameEvent(_dark, Amount: level));
            }
            else if (dark && level > LitAbove)
            {
                _isDark[e.Id] = false;
                _hub.Send(e, new GameEvent(_lit, Amount: level));
            }
        }

        if (_isDark.Count > _present.Count + 64)   // olvidar las entidades que ya no están activas
            foreach (var id in _isDark.Keys.Where(id => !_present.Contains(id)).ToList()) _isDark.Remove(id);
    }
}
