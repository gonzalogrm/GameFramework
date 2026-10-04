using Microsoft.Xna.Framework;

namespace GF.World.Voxel;

/// <summary>
/// Geometría de las 6 caras de un cubo unidad. Orden: +X, -X, +Y, -Y, +Z, -Z.
/// Las esquinas se generan antihorarias vistas desde fuera; el mesher invierte el orden
/// de los índices porque MonoGame considera "frontal" el sentido horario.
/// </summary>
public static class VoxelFaces
{
    public static readonly (int X, int Y, int Z)[] Normals =
        { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };

    /// <summary>Ejes tangentes u y v de cada cara (Corners = origen, +u, +u+v, +v). Usados por el AO.</summary>
    public static readonly (int X, int Y, int Z)[] TangentU =
        { (0, 1, 0), (0, 0, 1), (0, 0, 1), (1, 0, 0), (1, 0, 0), (0, 1, 0) };
    public static readonly (int X, int Y, int Z)[] TangentV =
        { (0, 0, 1), (0, 1, 0), (1, 0, 0), (0, 0, 1), (0, 1, 0), (1, 0, 0) };

    public static readonly Vector3[][] Corners = new Vector3[6][];
    public static readonly Vector2[][] UVs = new Vector2[6][];

    /// <summary>Sombreado fijo por cara (barato, sin iluminación).</summary>
    public static readonly Color[] Shade =
    {
        new(204, 204, 204), new(153, 153, 153), Color.White,
        new(128, 128, 128), new(179, 179, 179), new(230, 230, 230),
    };

    static VoxelFaces()
    {
        Vector3[] u = { Vector3.UnitY, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitX, Vector3.UnitX, Vector3.UnitY };
        Vector3[] v = { Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX };

        for (int f = 0; f < 6; f++)
        {
            var n = new Vector3(Normals[f].X, Normals[f].Y, Normals[f].Z);
            var o = (n.X + n.Y + n.Z) > 0 ? n : Vector3.Zero;
            Corners[f] = new[] { o, o + u[f], o + u[f] + v[f], o + v[f] };

            bool side = f is 0 or 1 or 4 or 5;
            // "Derecha" en pantalla al mirar la cara desde fuera (solo caras laterales).
            var r = side ? Vector3.Cross(-n, Vector3.UnitY) : Vector3.UnitX;
            float flip = (r.X + r.Y + r.Z) < 0 ? 1f : 0f;

            UVs[f] = new Vector2[4];
            for (int k = 0; k < 4; k++)
            {
                var c = Corners[f][k];
                UVs[f][k] = side
                    ? new Vector2(Vector3.Dot(r, c) + flip, 1f - c.Y)
                    : new Vector2(c.X, c.Z);
            }
        }
    }
}
