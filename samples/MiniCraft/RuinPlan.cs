using GF.Core;

namespace MiniCraft;

/// <summary>
/// Qué hay en una ruina y dónde: un mojón, cofres con tesoro, un NPC especial y los enemigos que la guardan. Determinista (sale de la ciudad y de una semilla fija),
/// así que RuinSpawner puede volver a calcularlo cada vez que se carga la ruina. Coordenadas relativas al centro; Y absoluta (donde se suelta la entidad: al
/// activarse se asienta en el suelo).
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

    /// <summary>Templo: el tesoro sobre el altar, provisiones entre las columnas, un arqueólogo fuera de la entrada y guardianes y aparecidos.</summary>
    private static void Temple(City c, Plan p, SplitMixRandom rng)
    {
        int w = RuinSites.HalfSize(c);
        var altar = RuinSites.FromLocal(c, 0, 0);
        var side = RuinSites.FromLocal(c, 3, -3);
        var npc = RuinSites.FromLocal(c, 0, -(w + 4));
        p.Chests.Add(new ChestSpot(new Spot(altar.Rx, altar.Rz, c.GroundY + 8), "chest_royal"));
        p.Chests.Add(new ChestSpot(new Spot(side.Rx, side.Rz, c.GroundY + 7), rng.Chance(0.5f) ? "chest_potions" : "chest_supplies"));
        p.Npc = new Spot(npc.Rx, npc.Rz, c.GroundY + 3);
        p.NpcType = "archaeologist";
        p.EnemyTypes = new[] { "temple_guardian", "revenant", "revenant" };
        p.EnemyCount = 4 + (int)(c.Decay * 2);
    }

    /// <summary>Campo de estatuas: ofrendas al pie del coloso, un ermitaño y gólems.</summary>
    private static void Statues(City c, Plan p, SplitMixRandom rng)
    {
        p.Chests.Add(new ChestSpot(new Spot(0, -6, c.GroundY + 3), rng.Chance(0.3f) ? "chest_royal" : "chest_potions"));
        p.Chests.Add(new ChestSpot(new Spot(7, -2, c.GroundY + 3), "chest_arms"));
        p.Npc = new Spot(6, 6, c.GroundY + 3);
        p.NpcType = "hermit";
        p.EnemyTypes = new[] { "golem", "golem", "revenant" };
        p.EnemyCount = 3 + c.Radius / 12;
    }

    /// <summary>Instalación tecnológica: armas y armaduras en el hangar, pociones junto a la antena, una superviviente tras la puerta y centinelas.</summary>
    private static void Tech(City c, Plan p, SplitMixRandom rng)
    {
        int s = RuinSites.HalfSize(c), z0 = s / 5, z1 = z0 + Math.Max(8, s / 2);
        var hangar = RuinSites.FromLocal(c, 0, (z0 + z1) / 2);
        var mast = RuinSites.FromLocal(c, -(s - 4) + 2, s - 4);
        var npc = RuinSites.FromLocal(c, 0, -(s - 3));
        p.Chests.Add(new ChestSpot(new Spot(hangar.Rx, hangar.Rz, c.GroundY + 3), rng.Chance(0.5f) ? "chest_arms" : "chest_armor"));
        p.Chests.Add(new ChestSpot(new Spot(mast.Rx, mast.Rz, c.GroundY + 4), "chest_potions"));
        p.Npc = new Spot(npc.Rx, npc.Rz, c.GroundY + 3);
        p.NpcType = "engineer";
        p.EnemyTypes = new[] { "sentinel" };
        p.EnemyCount = 3 + s / 10 + (int)(c.Decay * 2);
    }
}
