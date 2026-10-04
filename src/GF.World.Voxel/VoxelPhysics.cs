using GF.Core;
using Microsoft.Xna.Framework;

namespace GF.World.Voxel;

[Flags]
public enum CollisionFlags { None = 0, X = 1, Y = 2, Z = 4, Ground = 8 }

/// <summary>
/// Colisión AABB contra celdas sólidas, eje por eje, en DOBLE precisión (las holguras de 0,001 bloques
/// se pierden en float lejos del origen). Mantén delta &lt; 1 celda por eje y llamada.
/// </summary>
public static class VoxelPhysics
{
    private const double Skin = 0.001;
    private const double Eps = 0.0001;

    /// <param name="position">Centro de los pies.</param>
    /// <param name="size">Ancho (X), alto (Y), fondo (Z).</param>
    /// <param name="stepHeight">
    /// Altura máxima de escalón que se sube solo al chocar andando (0 = desactivado). Con 1, un bloque un nivel por encima se sube
    /// sin saltar, siempre que haya hueco sobre él. Pásalo solo si el cuerpo está apoyado en el suelo.
    /// </param>
    public static Vec3d MoveAndCollide(IWorld<ushort> world, BlockRegistry blocks, Vec3d position,
        Vector3 size, Vec3d delta, out CollisionFlags flags, double stepHeight = 0)
    {
        var result = Move(world, blocks, position, size, delta, out flags);
        if (stepHeight <= 0 || (flags & (CollisionFlags.X | CollisionFlags.Z)) == 0) return result;

        // Chocó en horizontal: ¿es un escalón? Subir, avanzar y volver a apoyarse arriba.
        var raised = new Vec3d(position.X, position.Y + stepHeight, position.Z);
        if (Overlaps(world, blocks, raised, size)) return result;   // techo bajo: no cabe

        var moved = Move(world, blocks, raised, size, new Vec3d(delta.X, 0, delta.Z), out var horizontal);
        if ((horizontal & (CollisionFlags.X | CollisionFlags.Z)) != 0) return result;   // pared más alta que el escalón

        var landed = Move(world, blocks, moved, size, new Vec3d(0, -stepHeight, 0), out var vertical);
        if ((vertical & CollisionFlags.Ground) == 0 || landed.Y <= position.Y + 0.01) return result;   // nada donde apoyarse

        flags = vertical;
        return landed;
    }

    private static Vec3d Move(IWorld<ushort> world, BlockRegistry blocks, Vec3d position,
        Vector3 size, Vec3d delta, out CollisionFlags flags)
    {
        flags = CollisionFlags.None;
        double hx = size.X * 0.5, hz = size.Z * 0.5, h = size.Y;
        double x = position.X, y = position.Y, z = position.Z;

        x += delta.X;
        if (Overlaps(world, blocks, x, y, z, hx, hz, h))
        {
            x = delta.X > 0 ? Math.Floor(x + hx) - hx - Skin : Math.Floor(x - hx) + 1 + hx + Skin;
            flags |= CollisionFlags.X;
        }

        y += delta.Y;
        if (Overlaps(world, blocks, x, y, z, hx, hz, h))
        {
            if (delta.Y > 0) y = Math.Floor(y + h) - h - Skin;
            else { y = Math.Floor(y) + 1 + Skin; flags |= CollisionFlags.Ground; }
            flags |= CollisionFlags.Y;
        }

        z += delta.Z;
        if (Overlaps(world, blocks, x, y, z, hx, hz, h))
        {
            z = delta.Z > 0 ? Math.Floor(z + hz) - hz - Skin : Math.Floor(z - hz) + 1 + hz + Skin;
            flags |= CollisionFlags.Z;
        }
        return new Vec3d(x, y, z);
    }

    public static bool Overlaps(IWorld<ushort> world, BlockRegistry blocks, Vec3d pos, Vector3 size) =>
        Overlaps(world, blocks, pos.X, pos.Y, pos.Z, size.X * 0.5, size.Z * 0.5, size.Y);

    private static bool Overlaps(IWorld<ushort> world, BlockRegistry blocks, double x, double y, double z,
        double hx, double hz, double h)
    {
        int x0 = (int)Math.Floor(x - hx), x1 = (int)Math.Floor(x + hx - Eps);
        int y0 = (int)Math.Floor(y), y1 = (int)Math.Floor(y + h - Eps);
        int z0 = (int)Math.Floor(z - hz), z1 = (int)Math.Floor(z + hz - Eps);

        for (int yy = y0; yy <= y1; yy++)
        for (int zz = z0; zz <= z1; zz++)
        for (int xx = x0; xx <= x1; xx++)
            if (blocks.Get(world.GetCell(new CellCoord(xx, yy, zz))).Solid) return true;
        return false;
    }
}
