using GF.Core;

namespace MiniCraft;

/// <summary>
/// Qué sprites de flora (bloques Blocks.FloraSprites, dibujados por PlanetSprites) crecen en cada bioma de un planeta, como ÁRBOL o como ARBUSTO. Salen de la
/// semilla del planeta: cada mundo conserva solo parte de las formas de cada bioma. TreeStage reparte las plantas a medias entre bloques (Flora) y estos sprites.
/// </summary>
public sealed class FloraSprites
{
    public static FloraSprites Current { get; private set; } = Create(0);
    public static void ApplyPlanet(ulong seed) => Current = Create(seed);

    private readonly ushort[][] _trees, _shrubs;

    private FloraSprites(ushort[][] trees, ushort[][] shrubs) { _trees = trees; _shrubs = shrubs; }

    /// <summary>Bloque-sprite para un ejemplar según el hash de su columna; 0 si el bioma no tiene.</summary>
    public ushort Pick(ushort biome, bool shrub, ulong hash)
    {
        var list = (shrub ? _shrubs : _trees)[biome];
        return list.Length == 0 ? (ushort)0 : list[(int)((hash >> 20) % (ulong)list.Length)];
    }

    // (bioma, árboles (slot, peso), arbustos (slot, peso)). Slots: 0 helecho, 1 junco, 2 abanico de hierba, 3 roseta, 4 flor, 5 macizo de flores, 6 fronda grande,
    // 7 arbusto redondo, 8 arbusto de pinchos, 9 árbol redondo, 10 abeto, 11 palmera, 12 árbol parasol, 13 seta, 14 árbol llorón, 15 cacto.
    private static (ushort Biome, (int, int)[] Trees, (int, int)[] Shrubs)[] Table() => new[]
    {
        (Biomes.Plains, new[] { (9, 3), (12, 2), (10, 1) }, new[] { (7, 3), (4, 2), (5, 3), (2, 3), (0, 1) }),
        (Biomes.Savanna, new[] { (12, 4), (9, 2) }, new[] { (2, 3), (1, 3), (7, 2), (15, 1) }),
        (Biomes.Forest, new[] { (9, 3), (10, 3), (14, 2), (13, 2) }, new[] { (0, 3), (7, 3), (13, 1), (6, 1), (4, 1) }),
        (Biomes.Jungle, new[] { (11, 4), (14, 3), (12, 2), (9, 2) }, new[] { (6, 4), (0, 3), (7, 2), (4, 2), (5, 2) }),
        (Biomes.Taiga, new[] { (10, 5), (9, 1) }, new[] { (0, 3), (2, 3), (7, 2) }),
        (Biomes.Tundra, new[] { (10, 3) }, new[] { (2, 4), (7, 2) }),
        (Biomes.Desert, new[] { (15, 4), (11, 1) }, new[] { (3, 4), (8, 3), (15, 2), (2, 1) }),
        (Biomes.Mountains, new[] { (10, 3), (9, 1) }, new[] { (8, 2), (2, 3), (7, 2), (3, 1) }),
        (Biomes.SnowPeaks, new[] { (10, 2) }, new[] { (2, 2) }),
    };

    private static FloraSprites Create(ulong seed)
    {
        var rng = new SplitMixRandom(Hashing.Mix(seed ^ 0xF5B17EUL));
        int n = Biomes.Registry.Count;
        var trees = new ushort[n][];
        var shrubs = new ushort[n][];
        for (int i = 0; i < n; i++) { trees[i] = Array.Empty<ushort>(); shrubs[i] = Array.Empty<ushort>(); }
        foreach (var (biome, t, s) in Table())
        {
            trees[biome] = Expand(rng, t);
            shrubs[biome] = Expand(rng, s);
        }
        return new FloraSprites(trees, shrubs);
    }

    /// <summary>Se queda con cada forma con un 75 % de probabilidad (al menos la primera) y repite cada una tantas veces como su peso.</summary>
    private static ushort[] Expand(SplitMixRandom rng, (int Slot, int Weight)[] list)
    {
        var result = new List<ushort>();
        for (int i = 0; i < list.Length; i++)
        {
            if (i > 0 && !rng.Chance(0.75f)) continue;
            for (int k = 0; k < list[i].Weight; k++) result.Add(Blocks.FloraSprites[list[i].Slot]);
        }
        return result.ToArray();
    }
}
