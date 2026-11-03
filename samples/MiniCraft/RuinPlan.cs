using GF.Core;

namespace MiniCraft;

/// <summary>
/// Qué hay en una ruina y dónde: un mojón, cofres con tesoro, un NPC especial y los enemigos que la guardan, todo propio de su clase y variante (técnico del
/// reactor y mutantes en la planta nuclear, sacristán y frailes espectrales en la catedral, cronista y guardias reales en la avenida de reyes...). Determinista
/// (sale de la ciudad y de una semilla fija), así que RuinSpawner puede volver a calcularlo cada vez que se carga la ruina. Coordenadas relativas al centro; Y
/// absoluta (donde se suelta la entidad: al activarse se asienta en el suelo).
/// </summary>
internal static class RuinPlan
{
    public readonly record struct Spot(int Rx, int Rz, int Y);
    public sealed record ChestSpot(Spot Spot, string Type);

    public sealed class Plan
    {
        public Spot Marker;
        public readonly List<ChestSpot> Chests = new();
        public Spot Npc;
        public string NpcType = "";
        public string[] EnemyTypes = Array.Empty<string>();
        public int EnemyCount;
    }

    public static Plan Make(City c, SplitMixRandom rng)
    {
        var p = new Plan { Marker = new Spot(2, 2, c.GroundY + 3) };
        switch (c.Site)
        {
            case SiteKind.Temple: Temple(c, p, rng); break;
            case SiteKind.Statues: Statues(c, p, rng); break;
            case SiteKind.Tech: Tech(c, p, rng); break;
            default: Town(c, p, rng); break;
        }
        return p;
    }

    private static string RandomChest(SplitMixRandom rng)
    {
        int r = rng.Next(100);
        return r < 35 ? "chest_supplies" : r < 60 ? "chest_potions" : r < 80 ? "chest_arms" : "chest_armor";
    }

    private static int Rank(string role) => role switch { "hall" => 0, "tower" => 1, "workshop" => 2, _ => 3 };

    /// <summary>Ciudad abandonada: cofres en los edificios mejores (salones y torres primero), un ermitaño en la plaza y bandidos y aparecidos por las calles.</summary>
    private static void Town(City c, Plan p, SplitMixRandom rng)
    {
        var order = CityStage.Landmarks(c).OrderBy(m => Rank(m.Role) + rng.NextFloat() * 1.5f).ToList();
        int n = Math.Clamp(1 + c.Radius / 40, 1, 4);
        for (int i = 0; i < Math.Min(n, order.Count); i++)
        {
            var m = order[i];
            string type = m.Role == "hall" ? (rng.Chance(0.5f) ? "chest_royal" : "chest_arms") : RandomChest(rng);
            p.Chests.Add(new ChestSpot(new Spot(m.Cx, m.Cz, m.Base + 3), type));
        }
        if (p.Chests.Count == 0) p.Chests.Add(new ChestSpot(new Spot(-4, 4, c.GroundY + 3), "chest_supplies"));
        int a = Math.Max(3, c.PlazaHalf - 2);
        p.Npc = new Spot(a, -a, c.GroundY + 3);
        p.NpcType = "hermit";
        p.EnemyTypes = new[] { "bandit", "revenant" };
        p.EnemyCount = Math.Clamp(3 + c.Radius / 18 + (int)(c.Decay * 2), 3, 11);
    }

    /// <summary>
    /// Templo: cada variante con su NPC (arqueólogo, sacristán, monje, ermitaño, peregrino), sus cofres (reliquias en los altares, ofrendas en el calvario, funerario en
    /// el cementerio) y sus enemigos (guardianes, frailes espectrales, bandidos, aparecidos, gólems).
    /// </summary>
    private static void Temple(City c, Plan p, SplitMixRandom rng)
    {
        int w = RuinSites.HalfSize(c);
        Spot At(int qx, int qz, int dy) { var (rx, rz) = RuinSites.FromLocal(c, qx, qz); return new Spot(rx, rz, c.GroundY + dy); }
        p.NpcType = "archaeologist";
        p.EnemyTypes = new[] { "temple_guardian", "revenant", "revenant" };
        p.EnemyCount = 4 + (int)(c.Decay * 2);
        switch (c.Variant)
        {
            case 1:   // catedral: reliquia en el altar del ábside y cofre en el transepto
                p.Chests.Add(new ChestSpot(At(0, w - 10, 4), "chest_relic"));
                p.Chests.Add(new ChestSpot(At(-6, w - 15, 3), rng.Chance(0.5f) ? "chest_potions" : "chest_royal"));
                p.Npc = At(0, -(w - 2), 3);
                p.NpcType = "sacristan";
                p.EnemyTypes = new[] { "temple_guardian", "monk_ghost", "monk_ghost" };
                p.EnemyCount += 2;
                break;
            case 2:   // monasterio: reliquia en la iglesia y víveres en el refectorio; los bandidos se han instalado
                p.Chests.Add(new ChestSpot(At(0, w - 7, 3), "chest_relic"));
                p.Chests.Add(new ChestSpot(At(-(w - 8), 0, 3), "chest_offering"));
                p.Npc = At(0, -(w - 3), 3);
                p.NpcType = "monk";
                p.EnemyTypes = new[] { "monk_ghost", "bandit", "bandit" };
                break;
            case 3:   // ermita: dentro de la capilla y en una esquina del cementerio
                p.Chests.Add(new ChestSpot(At(0, 3, 3), "chest_potions"));
                p.Chests.Add(new ChestSpot(At(-(w - 4), -(w - 4), 3), "chest_urn"));
                p.Npc = At(0, -5, 3);
                p.NpcType = "hermit";
                p.EnemyTypes = new[] { "revenant", "urn_wraith" };
                break;
            case 4:   // calvario: tesoro en lo alto del montículo y ofrendas al pie del camino
                p.Chests.Add(new ChestSpot(At(2, 2, 7), "chest_royal"));
                p.Chests.Add(new ChestSpot(At(4, -(w - 4), 3), "chest_offering"));
                p.Npc = At(-3, -(w - 3), 3);
                p.NpcType = "pilgrim";
                p.EnemyTypes = new[] { "revenant", "golem" };
                break;
            default:  // templo clásico: sobre el altar, entre las columnas, y el arqueólogo fuera de la entrada
            {
                var altar = RuinSites.FromLocal(c, 0, 0);
                var side = RuinSites.FromLocal(c, 3, -3);
                var npc = RuinSites.FromLocal(c, 0, -(w + 4));
                p.Chests.Add(new ChestSpot(new Spot(altar.Rx, altar.Rz, c.GroundY + 8), "chest_royal"));
                p.Chests.Add(new ChestSpot(new Spot(side.Rx, side.Rz, c.GroundY + 7), rng.Chance(0.5f) ? "chest_potions" : "chest_supplies"));
                p.Npc = new Spot(npc.Rx, npc.Rz, c.GroundY + 3);
                break;
            }
        }
    }

    /// <summary>Campo de estatuas: según la variante, ofrendas al pie del coloso, al fondo de la avenida o en la rotonda; ermitaño, cronista o sepulturero; gólems, guardias reales y espectros.</summary>
    private static void Statues(City c, Plan p, SplitMixRandom rng)
    {
        int padR = c.Radius - 9;
        Spot At(int qx, int qz, int dy) { var (rx, rz) = RuinSites.FromLocal(c, qx, qz); return new Spot(rx, rz, c.GroundY + dy); }
        p.NpcType = "hermit";
        switch (c.Variant)
        {
            case 1:   // avenida de reyes: al pie del coloso del fondo y bajo el arco; la guardia real sigue en su puesto
            {
                int l = padR - 4;
                p.Chests.Add(new ChestSpot(At(0, l - 8, 3), "chest_royal"));
                p.Chests.Add(new ChestSpot(At(3, -l + 4, 3), "chest_offering"));
                p.Npc = At(6, -l + 8, 3);
                p.NpcType = "historian";
                p.EnemyTypes = new[] { "royal_guard", "royal_guard", "golem", "revenant" };
                p.EnemyCount = 4 + l / 10;
                break;
            }
            case 2:   // columbario: en la rotonda y junto a la puerta; los espectros de las urnas
                p.Chests.Add(new ChestSpot(At(0, 3, 3), "chest_urn"));
                p.Chests.Add(new ChestSpot(At(0, -(padR - 5), 3), "chest_potions"));
                p.Npc = At(4, -(padR - 8), 3);
                p.NpcType = "gravekeeper";
                p.EnemyTypes = new[] { "urn_wraith", "urn_wraith", "revenant", "golem" };
                p.EnemyCount = 4 + padR / 12;
                break;
            default:  // coloso y estatuas
                p.Chests.Add(new ChestSpot(new Spot(0, -6, c.GroundY + 3), rng.Chance(0.3f) ? "chest_royal" : "chest_offering"));
                p.Chests.Add(new ChestSpot(new Spot(7, -2, c.GroundY + 3), "chest_arms"));
                p.Npc = new Spot(6, 6, c.GroundY + 3);
                p.EnemyTypes = new[] { "golem", "golem", "revenant" };
                p.EnemyCount = 3 + c.Radius / 12;
                break;
        }
    }

    /// <summary>
    /// Instalación tecnológica: cada variante con su NPC (técnico del reactor, capataz, piloto, cosmonauta, superviviente), taquillas técnicas, armas o pociones en el
    /// edificio principal y junto a lo más llamativo, y sus enemigos (mutantes, saqueadores, drones, autómatas, centinelas).
    /// </summary>
    private static void Tech(City c, Plan p, SplitMixRandom rng)
    {
        int s = RuinSites.HalfSize(c);
        Spot At(int qx, int qz, int dy) { var (rx, rz) = RuinSites.FromLocal(c, qx, qz); return new Spot(rx, rz, c.GroundY + dy); }
        p.Npc = At(0, -(s - 3), 3);
        p.NpcType = "engineer";
        p.EnemyTypes = new[] { "sentinel" };
        p.EnemyCount = 3 + s / 10 + (int)(c.Decay * 2);
        switch (c.Variant)
        {
            case 1:   // planta nuclear: sala de turbinas y frente al reactor; mutantes
                p.Chests.Add(new ChestSpot(At(-1, 0, 3), rng.Chance(0.5f) ? "chest_tech" : "chest_arms"));
                p.Chests.Add(new ChestSpot(At(s - 11, -8, 3), "chest_potions"));
                p.NpcType = "reactor_tech";
                p.EnemyTypes = new[] { "mutant", "mutant", "sentinel" };
                break;
            case 2:   // refinería: caseta de bombas y junto a la antorcha; saqueadores
                p.Chests.Add(new ChestSpot(At(7, -8, 3), rng.Chance(0.5f) ? "chest_tech" : "chest_armor"));
                p.Chests.Add(new ChestSpot(At(s - 8, -(s - 8), 3), "chest_potions"));
                p.NpcType = "foreman";
                p.EnemyTypes = new[] { "raider", "raider", "sentinel" };
                break;
            case 3:   // aeropuerto: terminal y hangar; drones y saqueadores
                p.Chests.Add(new ChestSpot(At(0, 15, 3), "chest_arms"));
                p.Chests.Add(new ChestSpot(At(-(s - 10), 17, 3), "chest_tech"));
                p.NpcType = "pilot";
                p.EnemyTypes = new[] { "drone_guard", "drone_guard", "raider" };
                break;
            case 4:   // cosmopuerto: búnker de control y edificio de ensamblaje; autómatas y drones
                p.Chests.Add(new ChestSpot(At(-(s - 8), 0, 3), "chest_arms"));
                p.Chests.Add(new ChestSpot(At(0, 19, 3), rng.Chance(0.4f) ? "chest_royal" : "chest_tech"));
                p.NpcType = "astronaut";
                p.EnemyTypes = new[] { "robot", "drone_guard", "sentinel" };
                break;
            default:  // complejo de investigación: hangar y pie de la antena
            {
                int z0 = s / 5, z1 = z0 + Math.Max(8, s / 2);
                p.Chests.Add(new ChestSpot(At(0, (z0 + z1) / 2, 3), rng.Chance(0.5f) ? "chest_tech" : "chest_arms"));
                p.Chests.Add(new ChestSpot(At(-(s - 4) + 2, s - 4, 4), "chest_potions"));
                break;
            }
        }
    }
}
