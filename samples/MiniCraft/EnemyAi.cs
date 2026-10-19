using GF.Core;
using GF.World.Entities;
using GF.World.Events;
using GF.World.Voxel;
using GF.World;

namespace MiniCraft;

/// <summary>
/// Cerebro de los enemigos (entidades con el prototipo "enemigo"). Cerca del jugador (aggro) planifican un camino con A* sobre el terreno
/// voxel y lo siguen; si ya están a su alcance, atacan. Lejos no hacen nada aquí: NpcSystem los deja vagar (wandering).
/// El A* es caro, así que cada enemigo replanifica como mucho cada ~1 s y solo se hacen unas pocas búsquedas por frame.
/// Coordenadas LOCALES al jugador (las de los chunks cargados).
/// </summary>
internal sealed class EnemyAi
{
    private const int PathsPerFrame = 2;
    private const double MaxSpeed = 10.0;

    private readonly World<ushort> _world;
    private readonly Random _rng = new(1234);
    private int _budget = PathsPerFrame;

    public EnemyAi(World<ushort> world) => _world = world;

    public void BeginFrame() => _budget = PathsPerFrame;

    /// <returns>true si el enemigo está comprometido con el jugador (persiguiendo o atacando): entonces manda wishX/wishZ, no el wandering.</returns>
    public bool Think(Entity e, NpcSystem.NpcState st, Vec3d local, Vec3d player, PlayerState target, EventHub hub, float dt,
        out float wishX, out float wishZ)
    {
        wishX = wishZ = 0f;
        // La IA es un COMPONENTE del tipo ("chase_player" en entitytypes.json), no una comprobación de tipo: cualquier tipo o variante puede tenerla.
        if (e.Prototype is not { } proto || !proto.HasComponent("chase_player")) return false;

        st.AttackCooldown -= dt;
        double dx = player.X - local.X, dz = player.Z - local.Z, dy = player.Y - local.Y;
        double dist = Math.Sqrt(dx * dx + dz * dz);
        if (target.Hp <= 0f || dist > e.GetFloat("aggro") || Math.Abs(dy) > 10)
        {
            st.Path = null;   // fuera de alcance: vuelve a vagar
            return false;
        }

        // A su alcance: se queda frente al jugador y golpea cada 'attack_cooldown' segundos.
        if (dist <= 1.2 && Math.Abs(dy) < 1.8)
        {
            if (st.AttackCooldown <= 0f)
            {
                st.AttackCooldown = e.GetFloat("attack_cooldown");
                // Golpe base + el ataque de TODO lo que lleva equipado: un enemigo de tres brazos con tres armas hace el daño de las tres.
                hub.Send(target, new GameEvent(GameEvents.Melee, e, e.GetFloat("damage") + Combat.Attack(e)));
            }
            return true;
        }

        // Camino: se replanifica cada ~1 s (con un poco de azar para que no coincidan todos) y mientras quede presupuesto de búsquedas.
        st.RepathTimer -= dt;
        if ((st.Path == null || st.RepathTimer <= 0f) && _budget > 0)
        {
            _budget--;
            st.RepathTimer = 0.8f + (float)_rng.NextDouble() * 0.4f;
            st.Path = VoxelPathfinder.FindPath(_world, Blocks.Registry, StandCell(local), StandCell(player), maxNodes: 400);
            st.PathIndex = 0;
        }

        // Objetivo: el siguiente punto del camino; sin camino (o al acabarlo), directo hacia el jugador.
        double tx = player.X, tz = player.Z;
        if (st.Path != null)
        {
            while (st.PathIndex < st.Path.Count)
            {
                var w = st.Path[st.PathIndex];
                double wx = w.X + 0.5 - local.X, wz = w.Z + 0.5 - local.Z;
                if (wx * wx + wz * wz < 0.2) { st.PathIndex++; continue; }
                tx = w.X + 0.5; tz = w.Z + 0.5;
                break;
            }
        }

        double mx = tx - local.X, mz = tz - local.Z, len = Math.Sqrt(mx * mx + mz * mz);
        if (len > 1e-6)
        {
            double speed = Math.Min(e.Speed, MaxSpeed);
            wishX = (float)(mx / len * speed);
            wishZ = (float)(mz / len * speed);
        }
        return true;
    }

    private static CellCoord StandCell(Vec3d p) =>
        new(IntMath.FloorToInt(p.X), IntMath.FloorToInt(p.Y + 0.1), IntMath.FloorToInt(p.Z));
}
