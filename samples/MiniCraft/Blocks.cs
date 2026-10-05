using GF.Core;
using GF.World.Voxel;

namespace MiniCraft;

public static class Blocks
{
    // Índices de tile en el atlas procedural (AtlasFactory).
    public const int TileGrassTop = 0, TileGrassSide = 1, TileDirt = 2, TileStone = 3, TileSand = 4, TileWater = 5,
                     TileLogTop = 6, TileLogSide = 7, TileLeaves = 8, TileSnow = 9;

    public static readonly BlockRegistry Registry = new();

    // Prototipo base de los bloques sólidos: valores por defecto COMPARTIDOS. Una celda solo guarda lo que cambia
    // (p. ej. la durabilidad de un bloque golpeado); los demás millones de bloques no ocupan nada por esto.
    public static readonly Prototype BlockProto = new Prototype("bloque").Float("durability", 1f);
    public static readonly ushort Grass, Dirt, Stone, Sand, Water, Log, Leaves, Snow, GrassTuft, FlowerRed, FlowerYellow, Rock, TallGrass, Bush;
    public const ushort Air = BlockRegistry.Air;

    // IMPORTANTE: el orden de registro define los ids guardados en disco. Añade bloques nuevos al final.
    static Blocks()
    {
        Grass = Registry.Register("grass", BlockDef.TopSideBottom("grass", TileGrassTop, TileGrassSide, TileDirt) with { Props = BlockProto });
        Dirt = Registry.Register("dirt", BlockDef.Cube("dirt", TileDirt) with { Props = BlockProto });
        Stone = Registry.Register("stone", BlockDef.Cube("stone", TileStone) with { Props = BlockProto.Derive("piedra").Float("durability", 3f) });
        Sand = Registry.Register("sand", BlockDef.Cube("sand", TileSand) with { Props = BlockProto });
        Water = Registry.Register("water", BlockDef.Cube("water", TileWater, solid: false, opaque: false, translucent: true));
        Log = Registry.Register("log", BlockDef.TopSideBottom("log", TileLogTop, TileLogSide, TileLogTop) with { Props = BlockProto.Derive("tronco").Float("durability", 2f) });
        Leaves = Registry.Register("leaves", BlockDef.Cube("leaves", TileLeaves) with { Props = BlockProto });
        Snow = Registry.Register("snow", BlockDef.Cube("snow", TileSnow) with { Props = BlockProto });

        // Sprites 2D importados de samples/MiniCraft/sprites/*.png. El segundo nombre es el del archivo, sin extensión.
        // Ancho/alto 0 = el sprite mide lo que mide su imagen (en bloques), así que pueden ser mayores que una celda.
        // Añade bloques siempre AL FINAL: el orden son los ids guardados en disco.
        GrassTuft = Registry.Register("grass_tuft", BlockDef.Sprite("grass_tuft", "grass_tuft", BlockRender.Cross));
        FlowerRed = Registry.Register("flower_red", BlockDef.Sprite("flower_red", "flower_red", BlockRender.Cross));
        FlowerYellow = Registry.Register("flower_yellow", BlockDef.Sprite("flower_yellow", "flower_yellow", BlockRender.Cross));
        Rock = Registry.Register("rock", BlockDef.Sprite("rock", "rock", BlockRender.Billboard));
        TallGrass = Registry.Register("tall_grass", BlockDef.Sprite("tall_grass", "tall_grass", BlockRender.Cross));       // 1 x 2 bloques
        Bush = Registry.Register("bush", BlockDef.Sprite("bush", "bush_large", BlockRender.Cross));                          // 3 x 2,25 bloques
    }
}
