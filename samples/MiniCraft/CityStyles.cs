using GF.Core;

namespace MiniCraft;

/// <summary>Aspecto de una ciudad: bloques de sus calles, plaza, paredes, columnas y tejados, y si tiene muralla.</summary>
public sealed record CityStyle(string Name, bool Walls, ushort Road, ushort Plaza, ushort Wall, ushort Pillar, ushort Roof, ushort WallBlock);

public static class CityStyles
{
    /// <summary>Estilo según el bioma dominante donde se levanta la ciudad. Determinista: sale del RNG de la celda.</summary>
    public static CityStyle For(ushort biome, SplitMixRandom rng)
    {
        bool coin = rng.Chance(0.5f);

        if (biome == Biomes.Desert || biome == Biomes.Beach)
            return new CityStyle("desierto", rng.Chance(0.7f), Blocks.Stone, Blocks.Stone, Blocks.Sand, Blocks.Log, Blocks.Sand, Blocks.Sand);

        if (biome == Biomes.Tundra || biome == Biomes.SnowPeaks || biome == Biomes.Taiga)
            return new CityStyle("nevada", rng.Chance(0.4f), Blocks.Stone, Blocks.Stone, Blocks.Stone, Blocks.Log, Blocks.Snow, Blocks.Stone);

        if (biome == Biomes.Forest || biome == Biomes.Jungle)
            return new CityStyle("bosque", rng.Chance(0.25f), Blocks.Dirt, Blocks.Stone,
                coin ? Blocks.Log : Blocks.Stone, coin ? Blocks.Stone : Blocks.Log, Blocks.Leaves, Blocks.Log);

        if (biome == Biomes.Mountains)
            return new CityStyle("montaña", rng.Chance(0.6f), Blocks.Stone, Blocks.Stone, Blocks.Stone, Blocks.Log, Blocks.Stone, Blocks.Stone);

        // Llanura y sabana (y cualquier otro).
        return new CityStyle("llanura", rng.Chance(0.4f), coin ? Blocks.Dirt : Blocks.Stone, Blocks.Stone, Blocks.Stone, Blocks.Log, Blocks.Log, Blocks.Stone);
    }
}
