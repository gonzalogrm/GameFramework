namespace GF.Core;

/// <summary>
/// Dimensiones de un chunk. Minecraft: 16x16x16. Roguelike 2D: p.ej. 64x64x1.
/// Índice lineal: (y * SizeZ + z) * SizeX + x.
/// </summary>
public sealed record ChunkShape(int SizeX, int SizeY, int SizeZ)
{
    public int Volume => SizeX * SizeY * SizeZ;
    public int Index(int x, int y, int z) => (y * SizeZ + z) * SizeX + x;
    public CellCoord Origin(ChunkCoord c) => new(c.X * SizeX, c.Y * SizeY, c.Z * SizeZ);
    public ChunkCoord ToChunk(CellCoord c) => ToChunk(c, out _, out _, out _);

    public ChunkCoord ToChunk(CellCoord c, out int lx, out int ly, out int lz)
    {
        int cx = IntMath.FloorDiv(c.X, SizeX);
        int cy = IntMath.FloorDiv(c.Y, SizeY);
        int cz = IntMath.FloorDiv(c.Z, SizeZ);
        lx = c.X - cx * SizeX;
        ly = c.Y - cy * SizeY;
        lz = c.Z - cz * SizeZ;
        return new ChunkCoord(cx, cy, cz);
    }
}
