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

    /// <summary>
    /// Propiedades de instancia de las celdas que se han salido del prototipo de su tipo de bloque: índice local -> cambios.
    /// Casi siempre null; solo existen entradas para las celdas con algún cambio, el resto comparte los valores del prototipo.
    /// </summary>
    public Dictionary<int, PropertyOverrides>? CellProperties { get; set; }

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
