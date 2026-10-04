using GF.Core;

namespace GF.World;

/// <summary>
/// Copia de un chunk más referencias a sus 26 vecinos (caras, aristas y esquinas), para poder leerlo
/// desde un hilo de fondo sin tocar el World (que solo se usa desde el hilo principal).
/// - Las celdas propias se COPIAN: la malla es coherente aunque el jugador edite mientras se malla.
/// - Los vecinos se guardan por referencia y solo se leen sus capas de borde. Si ese borde cambia,
///   World.SetCell vuelve a marcar este chunk como cambiado, así que el resultado acaba siendo correcto.
/// Captura siempre en el hilo principal.
/// </summary>
public sealed class ChunkSnapshot<TCell> where TCell : unmanaged
{
    private readonly TCell[][] _neighbors = new TCell[27][];   // null = vecino no cargado

    public ChunkCoord Coord { get; }
    public ChunkShape Shape { get; }
    public TCell[] Cells { get; }

    private ChunkSnapshot(ChunkCoord coord, ChunkShape shape, TCell[] cells)
    {
        Coord = coord; Shape = shape; Cells = cells;
    }

    public static ChunkSnapshot<TCell> Capture(IWorld<TCell> world, Chunk<TCell> chunk)
    {
        var snap = new ChunkSnapshot<TCell>(chunk.Coord, chunk.Shape, (TCell[])chunk.Cells.Clone());
        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0 && dz == 0) continue;
            var nc = new ChunkCoord(chunk.Coord.X + dx, chunk.Coord.Y + dy, chunk.Coord.Z + dz);
            snap._neighbors[Slot(dx, dy, dz)] = world.GetChunk(nc)?.Cells!;
        }
        return snap;
    }

    private static int Slot(int dx, int dy, int dz) => (dx + 1) + 3 * ((dy + 1) + 3 * (dz + 1));

    /// <summary>Celda en coordenadas locales. Admite estar hasta 1 chunk fuera en cualquier eje
    /// (vecinos de cara, arista o esquina). Vecino no cargado = default.</summary>
    public TCell Get(int x, int y, int z)
    {
        var s = Shape;
        int dx = x < 0 ? -1 : x >= s.SizeX ? 1 : 0;
        int dy = y < 0 ? -1 : y >= s.SizeY ? 1 : 0;
        int dz = z < 0 ? -1 : z >= s.SizeZ ? 1 : 0;
        if (dx == 0 && dy == 0 && dz == 0) return Cells[s.Index(x, y, z)];

        var arr = _neighbors[Slot(dx, dy, dz)];
        if (arr == null) return default;
        return arr[s.Index(x - dx * s.SizeX, y - dy * s.SizeY, z - dz * s.SizeZ)];
    }
}
