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

/// <summary>
/// Una capa de luz de un chunk comprimida por TANDAS (valor repetido): el aire abierto es casi todo el mismo valor, y el subsuelo, cero. Ocupa unas decenas
/// de KB en vez de los 64 KB del array completo; leer una celda es una búsqueda binaria entre las tandas.
/// </summary>
public sealed class RleU16
{
    private readonly ushort[] _values;
    private readonly int[] _ends;   // _ends[k] = índice (exclusivo) donde termina la tanda k

    private RleU16(ushort[] values, int[] ends) { _values = values; _ends = ends; }

    public int RunCount => _ends.Length;

    public static RleU16 Encode(ushort[] data)
    {
        var values = new List<ushort>();
        var ends = new List<int>();
        int i = 0;
        while (i < data.Length)
        {
            ushort v = data[i];
            int j = i + 1;
            while (j < data.Length && data[j] == v) j++;
            values.Add(v); ends.Add(j);
            i = j;
        }
        return new RleU16(values.ToArray(), ends.ToArray());
    }

    public ushort Get(int index)
    {
        int lo = 0, hi = _ends.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (index < _ends[mid]) hi = mid; else lo = mid + 1;
        }
        return _values[lo];
    }
}

/// <summary>Luz de las celdas de UN chunk (índice = ChunkShape.Index), comprimida. La publica el mallador; se consulta desde el hilo principal.</summary>
public sealed class LightChunk
{
    private readonly RleU16 _sky, _block;

    internal LightChunk(RleU16 sky, RleU16 block) { _sky = sky; _block = block; }

    internal ushort GetSky(int index) => _sky.Get(index);
    internal ushort GetBlock(int index) => _block.Get(index);
}
