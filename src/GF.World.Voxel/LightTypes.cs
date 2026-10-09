using Microsoft.Xna.Framework;

namespace GF.World.Voxel;

/// <summary>Color de luz empaquetado: 4 bits por canal (R, G, B), niveles 0..15.</summary>
public static class Rgb4
{
    public static ushort Pack(int r, int g, int b) => (ushort)((r << 8) | (g << 4) | b);
    public static int R(ushort v) => (v >> 8) & 15;
    public static int G(ushort v) => (v >> 4) & 15;
    public static int B(ushort v) => v & 15;
}

/// <summary>Parámetros de iluminación inmutables, seguros para los hilos de mallado.</summary>
public sealed class LightEnvironment
{
    public static readonly LightEnvironment Default = new(new Vector3(0.12f), Vector3.One);

    public LightEnvironment(Vector3 ambient, Vector3 skyColor)
    {
        Ambient = ambient; SkyColor = skyColor;
    }

    /// <summary>Brillo mínimo por canal (0..1) que nunca se baja, ni en la oscuridad total.</summary>
    public Vector3 Ambient { get; }
    /// <summary>Color de la luz del cielo (0..1 por canal). Blanco = día; azulado y tenue = noche.</summary>
    public Vector3 SkyColor { get; }
}

/// <summary>
/// Luz medida en una celda, por capas y canal (R, G, B), en niveles 0..15.
/// Ambient = luz mínima global; Sky y Block = capa estática (cielo y bloques emisores de color).
/// </summary>
public readonly record struct LightLevels(Vector3 Ambient, Vector3 Sky, Vector3 Block, bool Known)
{
    public Vector3 Static => Vector3.Max(Sky, Block);
    public Vector3 Total => Vector3.Max(Ambient, Static);
    /// <summary>Nivel de luz único 0..15 (luminancia del total). Es el valor para umbrales y eventos.</summary>
    public float Level { get { var t = Total; return 0.2126f * t.X + 0.7152f * t.Y + 0.0722f * t.Z; } }
}

/// <summary>Luz de las celdas de UN chunk (índice = ChunkShape.Index). La publica el mallador; se consulta desde el hilo principal.</summary>
public sealed class LightChunk
{
    internal LightChunk(ushort[] sky, ushort[] block) { Sky = sky; Block = block; }
    internal ushort[] Sky { get; }
    internal ushort[] Block { get; }
}
