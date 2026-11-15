using GF.World.Entities;

namespace MiniCraft;

/// <summary>
/// Efectos de estado temporales. De momento, RALENTIZAR (esporas...): un factor de velocidad (0..1) con duración, para el jugador y para las entidades. Se renueva mientras
/// se sigue dentro de la nube y expira solo. No toca el dato guardado de la entidad (Speed): quien mueve multiplica por SpeedFactor al usar la velocidad.
/// </summary>
public static class StatusEffects
{
    private static readonly Dictionary<long, (float Factor, double Until)> Slows = new();
    private static double _now, _playerUntil;
    private static float _playerFactor = 1f;

    /// <summary>Multiplicador de la velocidad del jugador (1 = normal).</summary>
    public static float PlayerSpeedFactor => _now < _playerUntil ? _playerFactor : 1f;

    /// <summary>Multiplicador de la velocidad de una entidad (1 = normal).</summary>
    public static float SpeedFactor(Entity e) =>
        Slows.Count > 0 && Slows.TryGetValue(e.Id, out var s) && _now < s.Until ? s.Factor : 1f;

    public static void SlowPlayer(float factor, double seconds)
    {
        _playerFactor = _now < _playerUntil ? MathF.Min(_playerFactor, factor) : factor;   // manda el efecto más fuerte
        _playerUntil = Math.Max(_playerUntil, _now + seconds);
    }

    public static void SlowEntity(Entity e, float factor, double seconds)
    {
        if (Slows.TryGetValue(e.Id, out var s) && _now < s.Until) factor = MathF.Min(factor, s.Factor);
        Slows[e.Id] = (factor, Math.Max(_now + seconds, Slows.TryGetValue(e.Id, out var old) ? old.Until : 0.0));
    }

    public static void Update(double dt)
    {
        _now += dt;
        if (Slows.Count > 128)   // olvidar los que ya expiraron
            foreach (var id in Slows.Where(kv => kv.Value.Until <= _now).Select(kv => kv.Key).ToList()) Slows.Remove(id);
    }

    /// <summary>Al abrir un mundo: sin efectos.</summary>
    public static void Clear()
    {
        Slows.Clear();
        _playerUntil = 0;
        _playerFactor = 1f;
    }
}
