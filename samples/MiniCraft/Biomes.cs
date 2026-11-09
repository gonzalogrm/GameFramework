using GF.Core;
using GF.World.Map;

namespace MiniCraft;

public static class Biomes
{
    public static readonly Registry<BiomeDef> Registry = new();
    public static readonly ushort Ocean, Beach, Desert, Savanna, Plains, Forest, Jungle, Taiga, Tundra, Mountains, SnowPeaks;

    /// <summary>Bloque de superficie, bloque de relleno (3 capas) y densidad de árboles, por bioma. Se reasignan por planeta (ApplyPlanet).</summary>
    public static ushort[] Surface = Array.Empty<ushort>(), Filler = Array.Empty<ushort>();
    public static float[] TreeDensity = Array.Empty<float>();
    public const float MaxTreeDensity = 0.06f;

    /// <summary>Roca profunda, tronco y copa de los árboles del planeta actual.</summary>
    public static ushort DeepStone = Blocks.Stone, Trunk = Blocks.Log, Canopy = Blocks.Leaves;

    static Biomes()
    {
        Ocean = Registry.Register("ocean", new BiomeDef("Oceano", 30, 70, 160, IsWater: true));
        Beach = Registry.Register("beach", new BiomeDef("Playa", 222, 208, 150));
        Desert = Registry.Register("desert", new BiomeDef("Desierto", 230, 200, 110));
        Savanna = Registry.Register("savanna", new BiomeDef("Sabana", 170, 170, 70));
        Plains = Registry.Register("plains", new BiomeDef("Llanura", 120, 190, 80));
        Forest = Registry.Register("forest", new BiomeDef("Bosque", 50, 130, 50));
        Jungle = Registry.Register("jungle", new BiomeDef("Jungla", 20, 110, 40));
        Taiga = Registry.Register("taiga", new BiomeDef("Taiga", 60, 110, 90));
        Tundra = Registry.Register("tundra", new BiomeDef("Tundra", 200, 215, 225));
        Mountains = Registry.Register("mountains", new BiomeDef("Montanas", 130, 125, 120));
        SnowPeaks = Registry.Register("snowpeaks", new BiomeDef("Cumbres nevadas", 245, 248, 255));

        Build(Blocks.Grass, Blocks.Grass, Blocks.Dirt, Blocks.Sand, Blocks.Sand, Blocks.Stone, Blocks.Snow, Blocks.Log, Blocks.Leaves);   // terrestre
    }

    /// <summary>
    /// Materiales del planeta: césped, suelo, arena, roca, suelo frío, madera y copa salen de su semilla (mismo planeta, mismos materiales). El primero
    /// de cada lista (el terrestre) sale con más probabilidad, así los mundos son extraños pero reconocibles.
    /// </summary>
    public static void ApplyPlanet(ulong seed)
    {
        var rng = new SplitMixRandom(Hashing.Mix(seed ^ 0xB10E5UL));
        ushort grass = Pick(rng, Blocks.Grass, Blocks.Moss, Blocks.Lichen);
        ushort wild = rng.Chance(0.5f) ? grass : Blocks.Moss;
        ushort soil = Pick(rng, Blocks.Dirt, Blocks.Clay, Blocks.DarkSoil);
        ushort sand = Pick(rng, Blocks.Sand, Blocks.SandRed, Blocks.SandBlack);
        ushort desert = Pick(rng, Blocks.Sand, Blocks.SandRed, Blocks.SandBlack);   // nada de arena blanca: tonos tierra
        ushort rock = Pick(rng, Blocks.Stone, Blocks.Basalt, Blocks.Strata);
        ushort cold = Pick(rng, Blocks.Snow, Blocks.Ash, Blocks.Chalk);
        ushort trunk = Pick(rng, Blocks.Log, Blocks.StemWood, Blocks.Petrified);
        ushort canopy = Pick(rng, Blocks.Leaves, Blocks.Fronds);   // las setas son otra cosa: solo se ven en los hongos
        Build(grass, wild, soil, sand, desert, rock, cold, trunk, canopy);
    }

    private static ushort Pick(SplitMixRandom rng, params ushort[] options) =>
        options[rng.Chance(0.4f) ? 0 : 1 + rng.Next(options.Length - 1)];

    private static void Build(ushort grass, ushort wild, ushort soil, ushort sand, ushort desert, ushort rock, ushort cold, ushort trunk, ushort canopy)
    {
        int n = Registry.Count;
        var surface = new ushort[n];
        var filler = new ushort[n];
        var trees = new float[n];

        void Set(ushort biome, ushort top, ushort fill, float density)
        {
            surface[biome] = top; filler[biome] = fill; trees[biome] = density;
        }
        Set(Ocean, sand, sand, 0f);
        Set(Beach, sand, sand, 0f);
        Set(Desert, desert, sand, 0f);
        Set(Savanna, grass, soil, 0.004f);
        Set(Plains, grass, soil, 0.006f);
        Set(Forest, wild, soil, 0.035f);
        Set(Jungle, wild, soil, MaxTreeDensity);
        Set(Taiga, wild, soil, 0.03f);
        Set(Tundra, cold, soil, 0f);
        Set(Mountains, rock, rock, 0f);
        Set(SnowPeaks, Blocks.Snow, rock, 0f);

        Surface = surface; Filler = filler; TreeDensity = trees;
        DeepStone = rock; Trunk = trunk; Canopy = canopy;
    }
}
