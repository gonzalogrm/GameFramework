using GF.Core;

namespace GF.World;

public interface IWorld<TCell> where TCell : unmanaged
{
    ChunkShape Shape { get; }
    /// <summary>Devuelve default(TCell) si el chunk no está cargado.</summary>
    TCell GetCell(CellCoord c);
    /// <summary>Devuelve false si el chunk no está cargado.</summary>
    bool SetCell(CellCoord c, TCell value);
    Chunk<TCell>? GetChunk(ChunkCoord c);

    event Action<ChunkCoord>? ChunkLoaded;
    event Action<ChunkCoord>? ChunkUnloaded;
    /// <summary>También se lanza para vecinos cuando cambia una celda del borde.</summary>
    event Action<ChunkCoord>? ChunkChanged;
}
