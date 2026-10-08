using GF.Core;
using GF.World.Events;

namespace GF.World.Voxel;

public static class BlockEvents
{
    /// <summary>
    /// Entrega el evento a todos los bloques (no aire) dentro de una esfera de 'radius' celdas. Cada bloque lo procesa solo si su
    /// tipo lo acepta; los demás lo reciben y lo ignoran. Devuelve cuántos lo procesaron.
    /// </summary>
    public static int Broadcast(EventHub hub, World<ushort> world, BlockRegistry blocks, CellCoord center, int radius, GameEvent ev)
    {
        int processed = 0, r2 = radius * radius;
        for (int dy = -radius; dy <= radius; dy++)
        for (int dz = -radius; dz <= radius; dz++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            if (dx * dx + dy * dy + dz * dz > r2) continue;
            var cell = center.Offset(dx, dy, dz);
            if (world.GetCell(cell) == BlockRegistry.Air) continue;
            if (hub.Send(new BlockRef(world, blocks, cell), ev) == DeliveryResult.Processed) processed++;
        }
        return processed;
    }
}
