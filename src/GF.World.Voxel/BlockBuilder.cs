using GF.Core;
using Microsoft.Xna.Framework;

namespace GF.World.Voxel;

/// <summary>
/// Definir un bloque en una línea, sin construir BlockDef, FaceTiles ni prototipos a mano:
///     Registry.Define("lamp_warm").Tile(TileSnow).Emits(15, new Color(255, 205, 140)).Parent(BlockProto).Register();
///     Registry.Define("grass").Tiles(top: 0, side: 1, bottom: 2).Parent(BlockProto).Register();
///     Registry.Define("glass_blue").Tile(TileWater).Translucent().NotOpaque().FilterLight(new Color(40, 90, 255)).Register();
///     Registry.Define("flower_red").Sprite("flower_red").Register();
/// Por defecto un cubo es sólido y opaco. Recuerda: el ORDEN de registro son los ids guardados en disco (añade bloques siempre al final).
/// </summary>
public sealed class BlockBuilder
{
    private readonly BlockRegistry _registry;
    private readonly string _name;
    private int[]? _tiles;
    private bool _solid = true, _opaque = true, _translucent;
    private BlockRender _render = BlockRender.Cube;
    private float _spriteW, _spriteH;
    private string? _spriteName;
    private byte _emission;
    private Color? _lightColor, _filter;
    private Prototype? _parent, _props;
    private float? _durability;

    internal BlockBuilder(BlockRegistry registry, string name)
    {
        _registry = registry;
        _name = name;
    }

    // ------------------------------------------------------------------ aspecto

    /// <summary>El mismo tile en las 6 caras.</summary>
    public BlockBuilder Tile(int tile)
    {
        _tiles = new[] { tile, tile, tile, tile, tile, tile };
        return this;
    }

    /// <summary>Tile de arriba, de los 4 lados y de abajo (hierba, troncos...).</summary>
    public BlockBuilder Tiles(int top, int side, int bottom)
    {
        _tiles = new[] { side, side, top, bottom, side, side };
        return this;
    }

    /// <summary>Un tile por cara, en el orden +X, -X, +Y (arriba), -Y (abajo), +Z, -Z.</summary>
    public BlockBuilder FaceTiles(params int[] tiles)
    {
        if (tiles.Length != 6) throw new ArgumentException($"'{_name}': FaceTiles necesita 6 tiles (+X, -X, +Y, -Y, +Z, -Z).");
        _tiles = (int[])tiles.Clone();
        return this;
    }

    /// <summary>Sprite 2D sin colisión (hierba, flores, rocas). Ancho y alto en bloques; 0 = el tamaño propio del sprite.</summary>
    public BlockBuilder Sprite(string spriteName, BlockRender render = BlockRender.Cross, float width = 0f, float height = 0f)
    {
        _render = render;
        _spriteName = spriteName;
        _spriteW = width;
        _spriteH = height;
        _solid = false;
        _opaque = false;
        return this;
    }

    // ------------------------------------------------------------------ física y luz

    /// <summary>Con false se atraviesa (como el aire o el agua).</summary>
    public BlockBuilder Solid(bool solid = true) { _solid = solid; return this; }
    public BlockBuilder Passable() { _solid = false; return this; }

    /// <summary>Con false deja pasar la luz y se ven las caras de detrás (hojas, cristal).</summary>
    public BlockBuilder Opaque(bool opaque = true) { _opaque = opaque; return this; }
    public BlockBuilder NotOpaque() { _opaque = false; return this; }

    /// <summary>Se dibuja con mezcla alfa en la pasada del agua.</summary>
    public BlockBuilder Translucent(bool translucent = true) { _translucent = translucent; return this; }

    /// <summary>Emite luz de nivel 0..15, blanca o del color dado.</summary>
    public BlockBuilder Emits(int level, Color? color = null)
    {
        _emission = (byte)Math.Clamp(level, 0, 15);
        _lightColor = color;
        return this;
    }

    /// <summary>Filtro de la luz que lo atraviesa, por canal (cristal de color). Solo tiene sentido en bloques no opacos.</summary>
    public BlockBuilder FilterLight(Color filter) { _filter = filter; return this; }

    // ------------------------------------------------------------------ propiedades

    /// <summary>Prototipo del que hereda (p. ej. el de piedra o madera: así comparte sus manejadores de eventos).</summary>
    public BlockBuilder Parent(Prototype parent) { _parent = parent; return this; }

    /// <summary>Golpes para romperlo (propiedad "durability"). Si hay Parent, crea un prototipo hijo con ese valor.</summary>
    public BlockBuilder Durability(float hits) { _durability = hits; return this; }

    /// <summary>Prototipo propio ya construido (sustituye a Parent y Durability).</summary>
    public BlockBuilder Props(Prototype props) { _props = props; return this; }

    // ------------------------------------------------------------------ resultado

    /// <summary>Construye la definición sin registrarla.</summary>
    public BlockDef Build()
    {
        if (_render == BlockRender.Cube && _tiles == null)
            throw new InvalidOperationException($"El bloque '{_name}' es un cubo y no tiene tiles: usa Tile(), Tiles() o FaceTiles().");

        var def = new BlockDef(_name, _solid, _opaque, _tiles ?? new int[6], _translucent, _render, _spriteW, _spriteH, _spriteName)
        {
            Emission = _emission,
            LightColor = _lightColor,
            LightFilter = _filter,
            Props = ResolveProps(),
        };
        return def;
    }

    /// <summary>Construye y registra el bloque. Devuelve su id.</summary>
    public ushort Register() => _registry.Register(_name, Build());

    private Prototype? ResolveProps()
    {
        if (_props != null) return _props;
        if (_durability is not { } hits) return _parent;
        var proto = _parent != null ? _parent.Derive(_name) : new Prototype(_name);
        return proto.Float("durability", hits);
    }
}

public static class BlockRegistryExtensions
{
    /// <summary>Empieza la definición de un bloque nuevo (termina con .Register()).</summary>
    public static BlockBuilder Define(this BlockRegistry registry, string name) => new(registry, name);
}
