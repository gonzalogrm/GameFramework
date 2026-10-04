using GF.Core;

namespace GF.World.Map;

/// <summary>
/// Escala del mundo: un mapa de MapWidth x MapHeight regiones, cada región de RegionChunksX x RegionChunksZ chunks.
/// Ejemplo: 200 x 50 regiones de 32 x 32 chunks de 16 bloques = 102.400 x 25.600 bloques.
/// El mundo es cilíndrico: X se envuelve (este y oeste se conectan) y Z tiene límites (los polos).
/// Convención de ejes del terreno: X y Z son el suelo. En el IWorld del mapa, la región (rx, rz) se
/// direcciona como CellCoord(rx, rz, 0).
/// </summary>
public sealed record WorldScale(int MapWidth, int MapHeight, int RegionChunksX, int RegionChunksZ, int ChunkSizeX, int ChunkSizeZ)
{
    public int RegionBlocksX => RegionChunksX * ChunkSizeX;
    public int RegionBlocksZ => RegionChunksZ * ChunkSizeZ;
    public int WidthBlocks => MapWidth * RegionBlocksX;
    public int HeightBlocks => MapHeight * RegionBlocksZ;
    public int ChunkCountX => MapWidth * RegionChunksX;
    public int ChunkCountZ => MapHeight * RegionChunksZ;

    public int WrapX(int wx) => ((wx % WidthBlocks) + WidthBlocks) % WidthBlocks;

    public float WrapX(float wx)
    {
        float w = WidthBlocks;
        float r = wx - MathF.Floor(wx / w) * w;
        return r >= w ? 0f : r;
    }

    public double WrapX(double wx)
    {
        double w = WidthBlocks;
        double r = wx - Math.Floor(wx / w) * w;
        return r >= w ? 0.0 : r;
    }

    /// <summary>Diferencia en X de 'from' a 'to' por el camino más corto (el mundo se envuelve). Rango [-W/2, W/2].</summary>
    public double DeltaX(double from, double to)
    {
        double w = WidthBlocks;
        double d = to - from;
        return d - w * Math.Round(d / w);
    }

    public int WrapChunkX(int cx) => ((cx % ChunkCountX) + ChunkCountX) % ChunkCountX;

    /// <summary>Región que contiene la columna (wx, wz). X se envuelve y Z se limita a los polos.</summary>
    public (int X, int Z) RegionOf(int wx, int wz) =>
        (WrapX(wx) / RegionBlocksX, Math.Clamp(wz, 0, HeightBlocks - 1) / RegionBlocksZ);

    /// <summary>Columna central de una región, como CellCoord(wx, 0, wz).</summary>
    public CellCoord RegionCenter(int rx, int rz) =>
        new(rx * RegionBlocksX + RegionBlocksX / 2, 0, rz * RegionBlocksZ + RegionBlocksZ / 2);

    /// <summary>Chunk (en X y Z; Y = 0) que contiene la columna (wx, wz).</summary>
    public ChunkCoord ChunkOf(int wx, int wz) =>
        new(IntMath.FloorDiv(WrapX(wx), ChunkSizeX), 0, IntMath.FloorDiv(Math.Clamp(wz, 0, HeightBlocks - 1), ChunkSizeZ));
}
