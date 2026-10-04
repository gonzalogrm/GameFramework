using GF.Core;
using GF.World.Map;

namespace MiniCraft;

public static class Biomes
{
    public static readonly Registry<BiomeDef> Registry = new();
    public static readonly ushort Ocean, Beach, Desert, Savanna, Plains, Forest, Jungle, Taiga, Tundra, Mountains, SnowPeaks;

    /// <summary>Bloque de superficie, bloque de relleno (3 capas) y densidad de árboles, por bioma.</summary>
    public static readonly ushort[] Surface, Filler;
    public static readonly float[] TreeDensity;
    public const float MaxTreeDensity = 0.06f;

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

        int n = Registry.Count;
        Surface = new ushort[n];
        Filler = new ushort[n];
        TreeDensity = new float[n];

        void Set(ushort biome, ushort top, ushort fill, float trees)
        {
            Surface[biome] = top; Filler[biome] = fill; TreeDensity[biome] = trees;
        }
        Set(Ocean, Blocks.Sand, Blocks.Sand, 0f);
        Set(Beach, Blocks.Sand, Blocks.Sand, 0f);
        Set(Desert, Blocks.Sand, Blocks.Sand, 0f);
        Set(Savanna, Blocks.Grass, Blocks.Dirt, 0.004f);
        Set(Plains, Blocks.Grass, Blocks.Dirt, 0.006f);
        Set(Forest, Blocks.Grass, Blocks.Dirt, 0.035f);
        Set(Jungle, Blocks.Grass, Blocks.Dirt, MaxTreeDensity);
        Set(Taiga, Blocks.Grass, Blocks.Dirt, 0.03f);
        Set(Tundra, Blocks.Snow, Blocks.Dirt, 0f);
        Set(Mountains, Blocks.Stone, Blocks.Stone, 0f);
        Set(SnowPeaks, Blocks.Snow, Blocks.Stone, 0f);
    }
}
