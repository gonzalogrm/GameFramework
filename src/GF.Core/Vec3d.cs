namespace GF.Core;

/// <summary>
/// Posición/vector en doble precisión. Las posiciones del mundo (jugador, cámara, entidades) usan Vec3d;
/// a la GPU solo llegan floats RELATIVOS a la cámara (origen flotante), que son siempre pequeños y precisos.
/// </summary>
public readonly record struct Vec3d(double X, double Y, double Z)
{
    public static readonly Vec3d Zero = new(0, 0, 0);

    public static Vec3d operator +(Vec3d a, Vec3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3d operator -(Vec3d a, Vec3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3d operator *(Vec3d a, double s) => new(a.X * s, a.Y * s, a.Z * s);

    public double LengthSquared => X * X + Y * Y + Z * Z;
}
