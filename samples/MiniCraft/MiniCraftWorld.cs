using GF.World.Map;

namespace MiniCraft;

public static class MiniCraftWorld
{
    public const int SeaLevel = 4;

    // Altura de cada chunk en bloques (Y)
    public const int ChunkSizeY = 16;

    // Rango de chunks en Y (inclusive). Ajusta HighestChunkY para cambiar la altura total.
    public const int LowestChunkY = 0;
    public const int HighestChunkY = 3;

    /// <summary>200 x 50 regiones de 32 x 32 chunks de 16 bloques = 102.400 x 25.600 bloques (XZ).</summary>
    public static readonly WorldScale Scale = new(MapWidth: 100, MapHeight: 50, RegionChunksX: 32, RegionChunksZ: 32,
                                                  ChunkSizeX: 16, ChunkSizeZ: 16);

    // Altura total del mundo en bloques (Y)
    public static int TotalHeightBlocks => (HighestChunkY - LowestChunkY + 1) * ChunkSizeY;
}
