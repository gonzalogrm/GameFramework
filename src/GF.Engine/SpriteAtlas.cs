using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GF.Engine;

/// <param name="Pixels">Rectángulo del sprite dentro de la textura.</param>
/// <param name="UvMin">UV de la esquina superior izquierda (ya recortada medio texel hacia dentro para no sangrar).</param>
/// <param name="UvMax">UV de la esquina inferior derecha (idem).</param>
/// <param name="Size">Tamaño por defecto en bloques (ancho, alto), según sus píxeles o sprites.json.</param>
public readonly record struct SpriteRegion(string Name, Rectangle Pixels, Vector2 UvMin, Vector2 UvMax, Vector2 Size);

/// <summary>
/// Una textura con sprites de cualquier tamaño empaquetados, consultables por nombre. Es inmutable una vez construida, así que se
/// puede leer desde hilos de fondo (el mallador lo hace). Un nombre desconocido devuelve un sprite de reemplazo magenta, para que el
/// error se vea en el juego sin romperlo.
/// </summary>
public sealed class SpriteAtlas : IDisposable
{
    private readonly Dictionary<string, SpriteRegion> _regions;

    public Texture2D Texture { get; }
    public SpriteRegion Missing { get; }
    /// <summary>Número de sprites cargados (sin contar el de reemplazo).</summary>
    public int Count => _regions.Count;
    public IEnumerable<string> Names => _regions.Keys;

    internal SpriteAtlas(Texture2D texture, Dictionary<string, SpriteRegion> regions, SpriteRegion missing)
    {
        Texture = texture;
        _regions = regions;
        Missing = missing;
    }

    public bool TryGet(string name, out SpriteRegion region) => _regions.TryGetValue(name, out region);

    public SpriteRegion Get(string? name) =>
        name != null && _regions.TryGetValue(name, out var region) ? region : Missing;

    /// <summary>UV dentro del sprite; local en [0,1]x[0,1] con (0,0) arriba a la izquierda.</summary>
    public Vector2 GetUV(SpriteRegion region, Vector2 local) => new(
        MathHelper.Lerp(region.UvMin.X, region.UvMax.X, local.X),
        MathHelper.Lerp(region.UvMin.Y, region.UvMax.Y, local.Y));

    public void Dispose() => Texture.Dispose();
}

/// <summary>Reúne imágenes sueltas (cada una con su nombre y tamaño) y las empaqueta en un único SpriteAtlas.</summary>
public sealed class SpriteAtlasBuilder
{
    private const string MissingName = "\0missing";

    private readonly List<(string Name, int Width, int Height, Color[] Pixels, Vector2 Size)> _items = new();
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    public int Padding { get; set; } = 1;
    public int MaxSize { get; set; } = 4096;

    public bool Contains(string name) => _names.Contains(name);

    /// <param name="pixels">width * height colores, fila a fila desde arriba.</param>
    /// <param name="sizeInBlocks">Tamaño por defecto en bloques; si es null, píxeles / 16.</param>
    public void Add(string name, int width, int height, Color[] pixels, Vector2? sizeInBlocks = null)
    {
        if (pixels.Length != width * height) throw new ArgumentException($"'{name}': {pixels.Length} píxeles no encajan en {width}x{height}.");
        if (!_names.Add(name)) throw new ArgumentException($"Sprite duplicado: '{name}'.");
        _items.Add((name, width, height, pixels, sizeInBlocks ?? new Vector2(width / 16f, height / 16f)));
    }

    public SpriteAtlas Build(GraphicsDevice device)
    {
        var all = new List<(string Name, int Width, int Height, Color[] Pixels, Vector2 Size)>(_items)
        {
            (MissingName, 16, 16, MakeMissingPixels(), Vector2.One),
        };

        var (placements, atlasW, atlasH) = SpritePacker.Pack(
            all.Select(i => new PackItem(i.Name, i.Width, i.Height)).ToList(), Padding, MaxSize);

        var data = new Color[atlasW * atlasH];   // transparente
        var byName = all.ToDictionary(i => i.Name, StringComparer.Ordinal);
        var regions = new Dictionary<string, SpriteRegion>(StringComparer.OrdinalIgnoreCase);
        SpriteRegion missing = default;
        float halfU = 0.5f / atlasW, halfV = 0.5f / atlasH;

        foreach (var p in placements)
        {
            var item = byName[p.Name];
            for (int row = 0; row < p.Height; row++)
                Array.Copy(item.Pixels, row * p.Width, data, (p.Y + row) * atlasW + p.X, p.Width);

            var region = new SpriteRegion(
                p.Name, new Rectangle(p.X, p.Y, p.Width, p.Height),
                new Vector2(p.X / (float)atlasW + halfU, p.Y / (float)atlasH + halfV),
                new Vector2((p.X + p.Width) / (float)atlasW - halfU, (p.Y + p.Height) / (float)atlasH - halfV),
                item.Size);
            if (p.Name == MissingName) missing = region;
            else regions[p.Name] = region;
        }

        var texture = new Texture2D(device, atlasW, atlasH);
        texture.SetData(data);
        return new SpriteAtlas(texture, regions, missing);
    }

    /// <summary>Tablero magenta y negro, el clásico "falta la textura".</summary>
    private static Color[] MakeMissingPixels()
    {
        var px = new Color[16 * 16];
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
            px[y * 16 + x] = ((x / 4 + y / 4) & 1) == 0 ? new Color(255, 0, 255) : Color.Black;
        return px;
    }
}
