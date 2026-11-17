namespace GF.Core;

public readonly record struct ChunkCoord(int X, int Y, int Z)
{
    public static ChunkCoord operator +(ChunkCoord a, ChunkCoord b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
}

public readonly record struct CellCoord(int X, int Y, int Z)
{
    public CellCoord Offset(int dx, int dy, int dz) => new(X + dx, Y + dy, Z + dz);
}

public static class IntMath
{
    /// <summary>División entera con redondeo hacia -infinito (correcta para negativos).</summary>
    public static int FloorDiv(int a, int b)
    {
        int q = a / b;
        if (a % b != 0 && (a < 0) != (b < 0)) q--;
        return q;
    }

    /// <summary>Módulo siempre en [0, b) (el % de C# da negativos con a negativo). Sustituye a los Mod privados repetidos.</summary>
    public static int Mod(int a, int b) => ((a % b) + b) % b;
    public static int FloorToInt(float v) => (int)MathF.Floor(v);
    public static int FloorToInt(double v) => (int)Math.Floor(v);
}
