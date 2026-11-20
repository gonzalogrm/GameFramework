using GF.Core;
using GF.World;
using GF.World.Map;

namespace MiniCraft;

/// <summary>Dónde empieza el jugador en un mundo nuevo.</summary>
public static class SpawnFinder
{
    /// <summary>Busca en espiral, desde el centro del mapa, una región templada, de tierra y sin montañas.</summary>
    public static bool TryFindLandRegion(World<MapCell> map, WorldScale scale, out int wx, out int wz)
    {
        int cx = scale.MapWidth / 2, cz = scale.MapHeight / 2;
        for (int r = 0; r < scale.MapWidth; r++)
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                    int rz = cz + dz;
                    if (rz < 0 || rz >= scale.MapHeight) continue;
                    int rx = IntMath.Mod(cx + dx, scale.MapWidth);

                    var c = map.GetCell(new CellCoord(rx, rz, 0));
                    if (c.WaterFraction < 0.2f && c.Temperature > 0.3f && c.Biome != Biomes.Mountains && c.Biome != Biomes.SnowPeaks)
                    {
                        var center = scale.RegionCenter(rx, rz);
                        wx = center.X; wz = center.Z;
                        return true;
                    }
                }
        wx = wz = 0;
        return false;
    }
}
