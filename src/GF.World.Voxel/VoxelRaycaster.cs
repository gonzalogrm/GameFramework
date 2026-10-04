using GF.Core;
using Microsoft.Xna.Framework;

namespace GF.World.Voxel;

public readonly record struct VoxelHit(CellCoord Cell, CellCoord Previous, float Distance);

/// <summary>Raycast DDA (Amanatides &amp; Woo) en doble precisión. 1 celda = 1 unidad de mundo.</summary>
public static class VoxelRaycaster
{
    public static bool Raycast(IWorld<ushort> world, BlockRegistry blocks, Vec3d origin, Vector3 direction,
        float maxDistance, out VoxelHit hit)
    {
        hit = default;
        if (direction.LengthSquared() < 1e-12f) return false;
        direction.Normalize();
        double dx = direction.X, dy = direction.Y, dz = direction.Z;

        int x = (int)Math.Floor(origin.X), y = (int)Math.Floor(origin.Y), z = (int)Math.Floor(origin.Z);
        int sx = Math.Sign(dx), sy = Math.Sign(dy), sz = Math.Sign(dz);

        double tdx = sx != 0 ? Math.Abs(1.0 / dx) : double.PositiveInfinity;
        double tdy = sy != 0 ? Math.Abs(1.0 / dy) : double.PositiveInfinity;
        double tdz = sz != 0 ? Math.Abs(1.0 / dz) : double.PositiveInfinity;

        double tmx = sx > 0 ? (x + 1 - origin.X) * tdx : sx < 0 ? (origin.X - x) * tdx : double.PositiveInfinity;
        double tmy = sy > 0 ? (y + 1 - origin.Y) * tdy : sy < 0 ? (origin.Y - y) * tdy : double.PositiveInfinity;
        double tmz = sz > 0 ? (z + 1 - origin.Z) * tdz : sz < 0 ? (origin.Z - z) * tdz : double.PositiveInfinity;

        var prev = new CellCoord(x, y, z);
        double t = 0;
        while (true)
        {
            var cell = new CellCoord(x, y, z);
            if (blocks.Get(world.GetCell(cell)).Selectable)
            {
                hit = new VoxelHit(cell, prev, (float)t);
                return true;
            }

            prev = cell;
            if (tmx <= tmy && tmx <= tmz) { t = tmx; if (t > maxDistance) return false; x += sx; tmx += tdx; }
            else if (tmy <= tmz)          { t = tmy; if (t > maxDistance) return false; y += sy; tmy += tdy; }
            else                          { t = tmz; if (t > maxDistance) return false; z += sz; tmz += tdz; }
        }
    }
}
