using System.Globalization;
using GF.Core;
using GF.World.Entities;
using GF.World.Events;
using GF.World.Map;

namespace MiniCraft;

/// <summary>
/// Cerebro de los guardas de ciudad (entidades con el componente "city_guard"). Si hay un enemigo a su alcance (propiedad "aggro") van a por el más
/// cercano y lo golpean con un evento de daño; si no, patrullan cerca de su ciudad (su Tag "home:x:z"). NpcSystem les cede el control mientras
/// Think devuelve true. Coordenadas LOCALES al jugador (las de los chunks cargados); solo se usan diferencias, así que no hay que envolver nada.
/// </summary>
internal sealed class GuardAi
{
    private const double MaxSpeed = 10.0, PatrolRadius = 28.0;
    private static readonly Random Rng = new(4321);

    private readonly EntityStore _store;
    private readonly WorldScale _scale;
    private readonly List<Entity> _scratch = new();

    public GuardAi(EntityStore store, WorldScale scale)
    {
        _store = store; _scale = scale;
    }

    public static bool IsGuard(Entity e) => e.Prototype?.HasComponent("city_guard") == true;

    /// <summary>Ciudad a la que pertenece el guarda (su Tag "home:x:z"); si no la tiene, patrulla donde está.</summary>
    private static (double X, double Z) Home(Entity e)
    {
        var parts = e.Tag?.Split(':');
        if (parts is { Length: 3 } && parts[0] == "home" &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) &&
            int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int z))
            return (x, z);
        return (e.Position.X, e.Position.Z);
    }

    /// <summary>Próximo punto de patrulla: al azar, a menos de ~30 bloques del centro de su ciudad.</summary>
    public static Vec3d PatrolTarget(Entity e, Func<double, double, double> groundY)
    {
        var (hx, hz) = Home(e);
        double angle = Rng.NextDouble() * Math.PI * 2, r = 4 + Rng.NextDouble() * PatrolRadius;
        double x = hx + Math.Cos(angle) * r, z = hz + Math.Sin(angle) * r;
        return new Vec3d(x, groundY(x, z), z);
    }

    /// <returns>true si el guarda está combatiendo (persiguiendo o golpeando): entonces manda wishX/wishZ, no la patrulla.</returns>
    public bool Think(Entity e, NpcSystem.NpcState st, Vec3d local, EventHub hub, float dt, out float wishX, out float wishZ)
    {
        wishX = wishZ = 0f;
        if (!IsGuard(e)) return false;

        st.AttackCooldown -= dt;
        _scratch.Clear();
        _store.QueryRadius(e.Position, e.GetFloat("aggro"), _scratch);
        Entity? target = null;
        double best = double.MaxValue;
        foreach (var t in _scratch)
        {
            if (ReferenceEquals(t, e) || t.Tier != SimulationTier.Active) continue;
            if (t.Prototype?.Is(EntityTypes.EnemyProto) != true) continue;
            double dx = _scale.DeltaX(e.Position.X, t.Position.X), dy = t.Position.Y - e.Position.Y, dz = t.Position.Z - e.Position.Z;
            double d2 = dx * dx + dy * dy + dz * dz;
            if (d2 < best) { best = d2; target = t; }
        }
        _scratch.Clear();
        if (target == null) return false;

        double tx = _scale.DeltaX(e.Position.X, target.Position.X), tz = target.Position.Z - e.Position.Z, ty = target.Position.Y - e.Position.Y;
        double dist = Math.Sqrt(tx * tx + tz * tz);
        if (dist <= 1.6 && Math.Abs(ty) < 2.0)
        {
            if (st.AttackCooldown <= 0f)
            {
                st.AttackCooldown = e.GetFloat("attack_cooldown");
                // Golpe base + el ataque de lo que lleva equipado. No es el jugador quien lo da: no hay botín para él.
                hub.Send(target, new GameEvent(GameEvents.Melee, e, e.GetFloat("damage") + Combat.Attack(e)));
            }
            return true;
        }

        if (dist > 1e-6)
        {
            double speed = Math.Min(e.Speed * 1.4, MaxSpeed);   // corren hacia el enemigo
            wishX = (float)(tx / dist * speed);
            wishZ = (float)(tz / dist * speed);
        }
        return true;
    }
}
