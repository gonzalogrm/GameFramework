using GF.Core;
using Microsoft.Xna.Framework;

namespace GF.World.Voxel;

/// <summary>Cómo se dibuja un bloque.</summary>
public enum BlockRender
{
    /// <summary>Cubo con caras visibles (lo normal).</summary>
    Cube,
    /// <summary>Sprite 2D en cruz: dos planos cruzados en diagonal, fijos en la malla del chunk (hierba, flores). Muy barato.</summary>
    Cross,
    /// <summary>Sprite 2D que gira para mirar siempre a la cámara (eje vertical fijo). Se dibuja aparte, solo cerca (rocas, arbustos, árboles 2D).</summary>
    Billboard,
}

/// <param name="FaceTiles">Índice de tile del atlas por cara: +X, -X, +Y(arriba), -Y(abajo), +Z, -Z. Un sprite usa FaceTiles[0].</param>
/// <param name="Translucent">Se dibuja en una segunda pasada con mezcla alfa (agua, cristal).</param>
/// <param name="Render">Cube, Cross o Billboard. Los sprites usan recorte por alfa (los píxeles transparentes del atlas no se dibujan).</param>
/// <param name="SpriteWidth">Ancho del sprite en bloques; puede ser mayor que 1. 0 = el tamaño propio del sprite (píxeles / PixelsPerBlock o sprites.json).</param>
/// <param name="SpriteHeight">Alto del sprite en bloques; puede ser mayor que 1. 0 = el tamaño propio del sprite.</param>
/// <param name="SpriteName">Nombre del sprite (archivo .png sin extensión) en el SpriteAtlas. Solo para Cross y Billboard.</param>
public sealed record BlockDef(string Name, bool Solid, bool Opaque, int[] FaceTiles, bool Translucent = false,
    BlockRender Render = BlockRender.Cube, float SpriteWidth = 0f, float SpriteHeight = 0f, string? SpriteName = null)
{
    /// <summary>
    /// Propiedades del TIPO de bloque (prototipo): valores por defecto compartidos por todos los bloques de este tipo. Una celda
    /// concreta solo guarda las que cambie (ver BlockRef). null = este tipo no tiene propiedades. Se asigna con 'with':
    ///     BlockDef.Cube("piedra", 3) with { Props = bloque.Derive("piedra").Float("durabilidad", 3f) }
    /// </summary>
    public Prototype? Props { get; init; }

    /// <summary>Luz que emite el bloque, 0..15 (0 = no emite). Se asigna con 'with': BlockDef.Cube("lamp", t) with { Emission = 15 }.</summary>
    public byte Emission { get; init; }

    /// <summary>Color de la luz que emite (null = blanco). Intensidad = Emission. Ej.: with { Emission = 14, LightColor = new Color(255, 120, 40) }.</summary>
    public Color? LightColor { get; init; }

    /// <summary>
    /// Filtro de la luz que lo atraviesa, por canal (null = transparente a toda la luz). Un cristal azul: new Color(40, 90, 255) deja pasar el azul y
    /// apaga el rojo; la luz que sale al otro lado (cielo o bloques) sale teñida. Solo tiene sentido en bloques no opacos.
    /// </summary>
    public Color? LightFilter { get; init; }

    /// <summary>Se puede apuntar con el cursor y romper: los bloques sólidos y los sprites (aunque no tengan colisión).</summary>
    public bool Selectable => Solid || Render != BlockRender.Cube;

    public static BlockDef Cube(string name, int tile, bool solid = true, bool opaque = true, bool translucent = false) =>
        new(name, solid, opaque, new[] { tile, tile, tile, tile, tile, tile }, translucent);

    public static BlockDef TopSideBottom(string name, int top, int side, int bottom) =>
        new(name, true, true, new[] { side, side, top, bottom, side, side });

    /// <summary>
    /// Sprite 2D sin colisión, tomado de un SpriteAtlas por nombre. Con width/height = 0 mide lo que mide su imagen
    /// (en bloques), así que un sprite de 48x36 px a 16 px/bloque ocupa 3 x 2,25 bloques: puede ser mayor que una celda.
    /// El sprite se ancla en el centro de la base de su celda.
    /// </summary>
    public static BlockDef Sprite(string name, string spriteName, BlockRender render = BlockRender.Cross, float width = 0f, float height = 0f) =>
        new(name, false, false, new int[6], false, render, width, height, spriteName);
}

/// <summary>
/// Copia en arrays de las propiedades que el mallador consulta millones de veces (indexar un array es mucho más barato que
/// List + record). Es inmutable: se publica una versión nueva si se registran más bloques.
/// </summary>
public sealed class BlockTable
{
    public BlockDef[] Defs { get; }
    public bool[] Opaque { get; }
    public byte[] Emission { get; }
    /// <summary>Luz emitida por canal, empaquetada (Rgb4).</summary>
    public ushort[] EmitRgb { get; }
    /// <summary>Filtro de luz por canal, 0..255 (255 = transparente).</summary>
    public byte[] FilterR { get; }
    public byte[] FilterG { get; }
    public byte[] FilterB { get; }

    internal BlockTable(BlockDef[] defs)
    {
        Defs = defs;
        Opaque = new bool[defs.Length];
        Emission = new byte[defs.Length];
        EmitRgb = new ushort[defs.Length];
        FilterR = new byte[defs.Length]; FilterG = new byte[defs.Length]; FilterB = new byte[defs.Length];
        for (int i = 0; i < defs.Length; i++)
        {
            var d = defs[i];
            Opaque[i] = d.Opaque;
            Emission[i] = d.Emission;
            var c = d.LightColor ?? Color.White;
            EmitRgb[i] = Rgb4.Pack(Level(d.Emission, c.R), Level(d.Emission, c.G), Level(d.Emission, c.B));
            var f = d.LightFilter ?? Color.White;
            FilterR[i] = f.R; FilterG[i] = f.G; FilterB[i] = f.B;
        }
    }

    private static int Level(int emission, int channel) => Math.Min(15, (emission * channel + 127) / 255);
}

public sealed class BlockRegistry : Registry<BlockDef>
{
    public const ushort Air = 0;

    private readonly object _tableLock = new();
    private volatile BlockTable? _table;

    public BlockRegistry() => Register("air", new BlockDef("air", false, false, new int[6]));

    /// <summary>Tablas de consulta rápida. Seguro desde varios hilos; se reconstruyen solo si cambió el número de bloques.</summary>
    public BlockTable GetTable()
    {
        var t = _table;
        if (t != null && t.Defs.Length == Count) return t;
        lock (_tableLock)
        {
            t = _table;
            if (t != null && t.Defs.Length == Count) return t;
            var defs = new BlockDef[Count];
            for (int i = 0; i < defs.Length; i++) defs[i] = Get((ushort)i);
            return _table = new BlockTable(defs);
        }
    }
}
