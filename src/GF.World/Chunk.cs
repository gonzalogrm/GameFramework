using GF.Core;

namespace GF.World;

public sealed class Chunk<TCell> where TCell : unmanaged
{
    public ChunkCoord Coord { get; }
    public ChunkShape Shape { get; }
    public TCell[] Cells { get; }
    public bool IsDirty { get; set; }

    /// <summary>true si se editó después de generarse/cargarse (World.SetCell); solo estos chunks se guardan.</summary>
    public bool IsModified { get; set; }

    public Chunk(ChunkCoord coord, ChunkShape shape)
    {
        Coord = coord;
        Shape = shape;
        Cells = new TCell[shape.Volume];
    }

    public TCell this[int x, int y, int z]
    {
        get => Cells[Shape.Index(x, y, z)];
        set { Cells[Shape.Index(x, y, z)] = value; IsDirty = true; }
    }
}
