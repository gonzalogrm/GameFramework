using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GF.World.Voxel;

/// <summary>
/// Origen de datos del terreno lejano. Lo implementa el juego a partir de su función de terreno (no hace falta tener chunks):
/// MiniCraft lo calcula directamente del modelo climático. Debe ser puro y seguro desde varios hilos.
/// </summary>
public interface IFarTerrainSource
{
    /// <summary>
    /// Altura de la superficie visible (en bloques; para el agua, la de su superficie) y su color base en una columna del mundo.
    /// 'spacing' es la separación entre muestras del nivel que se está generando: el origen puede omitir el detalle más fino que
    /// ella (filtro paso bajo) para que el terreno lejano conserve montañas y valles sin ruido.
    /// </summary>
    void Sample(int wx, int wz, int spacing, out float height, out Color color);
}

/// <summary>Un tile del quadtree: nivel 0 = el más fino; cada nivel duplica el lado. X/Z en unidades de tile de ese nivel.</summary>
public readonly record struct FarTileKey(int Level, int X, int Z)
{
    public FarTileKey Parent => new(Level + 1, X >> 1, Z >> 1);

    public IEnumerable<FarTileKey> Children
    {
        get
        {
            for (int dz = 0; dz <= 1; dz++)
            for (int dx = 0; dx <= 1; dx++)
                yield return new FarTileKey(Level - 1, X * 2 + dx, Z * 2 + dz);
        }
    }
}

/// <summary>
/// Elige qué tiles hay que dibujar: un quadtree que se subdivide cuanto más cerca está la cámara. Con splitFactor = 1,6 un tile se
/// divide si la cámara está a menos de 1,6 veces su lado, así que la resolución angular es casi constante: mucho detalle cerca,
/// poco lejos. Sin dependencias de la GPU (se puede probar sin ventana).
/// </summary>
public static class FarTileSelector
{
    private const int MaxTopLevel = 9;

    /// <summary>Nivel de los tiles raíz: el menor cuyo lado alcanza la distancia máxima.</summary>
    public static int TopLevel(double farDistance, int baseTile)
    {
        int top = 0;
        while (((long)baseTile << top) < farDistance && top < MaxTopLevel) top++;
        return top;
    }

    /// <param name="zExtent">Extensión del mundo en Z (de 0 a zExtent): no se generan tiles fuera.</param>
    /// <param name="result">Tiles elegidos con su distancia a la cámara (se vacía antes).</param>
    public static void Select(double camX, double camZ, double farDistance, int baseTile, double splitFactor,
        double zExtent, List<(FarTileKey Key, double Distance)> result)
    {
        result.Clear();
        int top = TopLevel(farDistance, baseTile);
        long size = (long)baseTile << top;

        int x0 = (int)Math.Floor((camX - farDistance) / size), x1 = (int)Math.Floor((camX + farDistance) / size);
        int z0 = Math.Max(0, (int)Math.Floor((camZ - farDistance) / size));
        int z1 = Math.Min((int)Math.Floor((zExtent - 1) / size), (int)Math.Floor((camZ + farDistance) / size));

        for (int tz = z0; tz <= z1; tz++)
        for (int tx = x0; tx <= x1; tx++)
            Visit(new FarTileKey(top, tx, tz), camX, camZ, farDistance, baseTile, splitFactor, zExtent, result);
    }

    private static void Visit(FarTileKey key, double camX, double camZ, double farDistance, int baseTile,
        double splitFactor, double zExtent, List<(FarTileKey, double)> result)
    {
        long size = (long)baseTile << key.Level;
        double minX = (double)key.X * size, minZ = (double)key.Z * size;
        if (minZ >= zExtent || minZ + size <= 0) return;   // más allá de los polos

        double dx = Math.Max(Math.Max(minX - camX, camX - (minX + size)), 0.0);
        double dz = Math.Max(Math.Max(minZ - camZ, camZ - (minZ + size)), 0.0);
        double distance = Math.Sqrt(dx * dx + dz * dz);
        if (distance > farDistance) return;

        if (key.Level > 0 && distance < size * splitFactor)
        {
            foreach (var child in key.Children)
                Visit(child, camX, camZ, farDistance, baseTile, splitFactor, zExtent, result);
            return;
        }
        result.Add((key, distance));
    }
}

/// <summary>Geometría de un tile, lista para subir a la GPU. Vértices con X/Z locales al tile y Y absoluta.</summary>
public sealed class FarTileData
{
    public FarTileData(VertexPositionColor[] vertices, short[] indices, float minY, float maxY)
    {
        Vertices = vertices; Indices = indices; MinY = minY; MaxY = maxY;
    }

    public VertexPositionColor[] Vertices { get; }
    public short[] Indices { get; }
    public float MinY { get; }
    public float MaxY { get; }
    public int TriangleCount => Indices.Length / 3;
}

/// <summary>
/// Genera la malla de un tile muestreando el origen en una rejilla de cells x cells. El relieve se hace visible con iluminación
/// calculada por vértice a partir de la pendiente (sol desde el noroeste), y los bordes llevan una "falda" que tapa las grietas
/// entre tiles de distinto nivel de detalle.
/// </summary>
public static class FarTileBuilder
{
    private static readonly Vector3 LightDir = Vector3.Normalize(new Vector3(-0.5f, 0.75f, -0.45f));

    /// <param name="baseTile">Lado en bloques de un tile de nivel 0. baseTile y cells deben ser potencias de dos con baseTile &gt;= cells.</param>
    public static FarTileData Build(IFarTerrainSource source, FarTileKey key, int baseTile, int cells)
    {
        int size = baseTile << key.Level;
        int spacing = size / cells;
        int originX = key.X * size, originZ = key.Z * size;

        // Muestras con un anillo de borde (para calcular las normales también en el perímetro).
        int n = cells + 3;
        var heights = new float[n * n];
        var colors = new Color[n * n];
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
            source.Sample(originX + (i - 1) * spacing, originZ + (j - 1) * spacing, spacing,
                out heights[j * n + i], out colors[j * n + i]);

        int vc = cells + 1;
        int skirtBase = vc * vc;
        var vertices = new VertexPositionColor[vc * vc + 4 * vc];
        float minY = float.MaxValue, maxY = float.MinValue;

        for (int j = 0; j < vc; j++)
        for (int i = 0; i < vc; i++)
        {
            int g = (j + 1) * n + (i + 1);
            float h = heights[g];
            float dhdx = (heights[g + 1] - heights[g - 1]) / (2f * spacing);
            float dhdz = (heights[g + n] - heights[g - n]) / (2f * spacing);
            var normal = Vector3.Normalize(new Vector3(-dhdx, 1f, -dhdz));
            float shade = 0.55f + 0.6f * MathHelper.Clamp(Vector3.Dot(normal, LightDir), 0f, 1f);

            var c = colors[g];
            vertices[j * vc + i] = new VertexPositionColor(
                new Vector3(i * spacing, h, j * spacing),
                new Color((int)Math.Min(255f, c.R * shade), (int)Math.Min(255f, c.G * shade), (int)Math.Min(255f, c.B * shade), 255));
            if (h < minY) minY = h;
            if (h > maxY) maxY = h;
        }

        // Faldas: una copia de cada vértice del borde, más baja, que cubre las grietas con los tiles vecinos.
        float skirt = spacing + 3f;
        for (int edge = 0; edge < 4; edge++)
        for (int t = 0; t < vc; t++)
        {
            var v = vertices[EdgeVertex(edge, t, cells, vc)];
            var dark = new Color((int)(v.Color.R * 0.8f), (int)(v.Color.G * 0.8f), (int)(v.Color.B * 0.8f), 255);
            vertices[skirtBase + edge * vc + t] = new VertexPositionColor(new Vector3(v.Position.X, v.Position.Y - skirt, v.Position.Z), dark);
        }
        minY -= skirt;

        var indices = new short[cells * cells * 6 + 4 * cells * 6];
        int k = 0;
        for (int j = 0; j < cells; j++)
        for (int i = 0; i < cells; i++)
        {
            int a = j * vc + i, b = a + 1, c = a + vc, d = c + 1;
            indices[k++] = (short)a; indices[k++] = (short)b; indices[k++] = (short)d;
            indices[k++] = (short)a; indices[k++] = (short)d; indices[k++] = (short)c;
        }
        // El terreno se dibuja sin culling (se ve por ambos lados), así que el sentido de giro da igual.
        for (int edge = 0; edge < 4; edge++)
        for (int t = 0; t < cells; t++)
        {
            int top0 = EdgeVertex(edge, t, cells, vc), top1 = EdgeVertex(edge, t + 1, cells, vc);
            int bottom0 = skirtBase + edge * vc + t, bottom1 = bottom0 + 1;
            indices[k++] = (short)top0; indices[k++] = (short)top1; indices[k++] = (short)bottom1;
            indices[k++] = (short)top0; indices[k++] = (short)bottom1; indices[k++] = (short)bottom0;
        }

        return new FarTileData(vertices, indices, minY, maxY);
    }

    /// <summary>Índice del vértice t-ésimo del borde: 0 norte (z=0), 1 sur, 2 oeste (x=0), 3 este.</summary>
    private static int EdgeVertex(int edge, int t, int cells, int vc) => edge switch
    {
        0 => t,
        1 => cells * vc + t,
        2 => t * vc,
        _ => t * vc + cells,
    };
}
