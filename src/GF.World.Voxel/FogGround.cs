using GF.Core;

namespace GF.World.Voxel;

/// <summary>Búsqueda del suelo para la niebla sobre un mundo de bloques.</summary>
public static class FogGround
{
    private const int MaxUp = 24, MaxDown = 64;

    /// <summary>
    /// Suelo = bloque opaco o líquido (agua). Se parte de la altura de la cámara: si esa celda es aire se baja hasta el primer suelo (superficie del terreno, suelo de
    /// una cueva...); si es suelo (ladera, pared) se sube hasta el aire. Las copas de los árboles no cuentan como suelo. Falla si el chunk no está cargado.
    /// </summary>
    public static FogGroundQuery For(IWorld<ushort> world, BlockRegistry blocks, int minY, int maxY)
    {
        var table = blocks.GetTable();
        var shape = world.Shape;

        bool IsGround(int x, int y, int z)
        {
            var cell = new CellCoord(x, y, z);
            if (world.GetChunk(shape.ToChunk(cell)) == null) return false;
            ushort id = world.GetCell(cell);
            if (id == BlockRegistry.Air) return false;
            var def = table.Defs[id];
            return table.Opaque[id] || (def.Translucent && !def.Solid);
        }

        return (int wx, int wz, int hintY, out int groundY) =>
        {
            groundY = 0;
            int y = Math.Clamp(hintY, minY, maxY);
            if (IsGround(wx, y, wz))
            {
                for (int k = 0; k < MaxUp && y < maxY; k++)
                    if (!IsGround(wx, ++y, wz)) { groundY = y; return true; }
                return false;
            }
            for (int k = 0; k < MaxDown && y > minY; k++)
                if (IsGround(wx, --y, wz)) { groundY = y + 1; return true; }
            return false;
        };
    }
}
