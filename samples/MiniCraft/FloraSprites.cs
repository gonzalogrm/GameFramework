using GF.Core;

namespace MiniCraft;

/// <summary>
/// Qué sprites de matorral y plantas bajas (bloques Blocks.FloraSprites, dibujados por PlanetSprites) crecen en cada bioma de un planeta. Salen de la semilla
/// del planeta: cada mundo conserva solo parte de las formas de cada bioma. Ya no hay sprites de árboles: solo arbustos, hierbas y matorrales; TreeStage los
/// mezcla con los arbustos de bloques según Vegetation.SpriteShare.
/// </summary>
public sealed class FloraSprites
{
    public static FloraSprites Current { get; private set; } = Create(0);
    public static void ApplyPlanet(ulong seed) => Current = Create(seed);

    private readonly ushort[][] _shrubs;

    private FloraSprites(ushort[][] shrubs) { _shrubs = shrubs; }

    /// <summary>Bloque-sprite para un ejemplar según el hash de su columna; 0 si el bioma no tiene (o si se pide un árbol: no hay sprites de árboles).</summary>
    public ushort Pick(ushort biome, bool shrub, ulong hash)
    {
        if (!shrub) return 0;
        var list = _shrubs[biome];
        return list.Length == 0 ? (ushort)0 : list[(int)((hash >> 20) % (ulong)list.Length)];
    }

    // (bioma, arbustos (slot, peso)). Slots: 0 helecho, 1 junco, 2 abanico de hierba, 3 roseta, 4 flor, 5 macizo de flores, 6 fronda grande, 7 matorral ramoso,
    // 8 arbusto de pinchos, 9 enredo espinoso, 10 retama, 11 brezo con flores, 12 tapiz bajo, 13 setas, 14 zarza, 15 cacto bajo.
    private static (ushort Biome, (int, int)[] Shrubs)[] Table() => new[]
    {
        (Biomes.Plains, new[] { (7, 3), (11, 2), (4, 2), (5, 3), (2, 3), (0, 1), (10, 2), (12, 2), (9, 1) }),
        (Biomes.Savanna, new[] { (2, 3), (1, 3), (10, 2), (9, 2), (8, 1), (12, 1) }),
        (Biomes.Forest, new[] { (0, 3), (7, 3), (13, 1), (6, 1), (4, 1), (14, 2), (10, 2), (12, 2) }),
        (Biomes.Jungle, new[] { (6, 4), (0, 3), (14, 2), (4, 2), (5, 2), (7, 2), (13, 1) }),
        (Biomes.Taiga, new[] { (0, 3), (2, 3), (11, 2), (12, 3), (7, 2), (14, 1) }),
        (Biomes.Tundra, new[] { (2, 3), (12, 4), (11, 1), (9, 2) }),
        (Biomes.Desert, new[] { (3, 4), (8, 3), (15, 3), (9, 3), (2, 1), (1, 1) }),
        (Biomes.Mountains, new[] { (8, 2), (2, 3), (12, 3), (9, 3), (7, 2), (3, 1) }),
        (Biomes.SnowPeaks, new[] { (2, 2), (12, 3), (9, 1) }),
    };

    private static FloraSprites Create(ulong seed)
    {
        var rng = new SplitMixRandom(Hashing.Mix(seed ^ 0xF5B17EUL));
        int n = Biomes.Registry.Count;
        var shrubs = new ushort[n][];
        for (int i = 0; i < n; i++) shrubs[i] = Array.Empty<ushort>();
        foreach (var (biome, s) in Table()) shrubs[biome] = Expand(rng, s);
        return new FloraSprites(shrubs);
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
