using GF.Core;
using GF.World.Voxel;
using Microsoft.Xna.Framework;

namespace MiniCraft;

public static class Blocks
{
    // Índices de tile en el atlas (AtlasFactory, rejilla 8x8). 0..9 vienen de blocks.png; 10..32 se pintan por código.
    public const int TileGrassTop = 0, TileGrassSide = 1, TileDirt = 2, TileStone = 3, TileSand = 4, TileWater = 5,
                     TileLogTop = 6, TileLogSide = 7, TileLeaves = 8, TileSnow = 9,
                     TileCrystal = 10, TileFungus = 11, TileMetal = 12, TileSalt = 13, TileAsh = 14, TilePanel = 15,
                     TileSandRed = 16, TileSandBlack = 17, TileClay = 18, TileDarkSoil = 19, TileMossTop = 20, TileMossSide = 21,
                     TileLichenTop = 22, TileLichenSide = 23, TileBasalt = 24, TileStrata = 25, TileChalk = 26,
                     TileStemTop = 27, TileStemSide = 28, TilePetrifiedTop = 29, TilePetrifiedSide = 30, TileFronds = 31, TileSporeCap = 32;

    public static readonly BlockRegistry Registry = new();

    // Prototipo base de los bloques sólidos: valores por defecto COMPARTIDOS. Una celda solo guarda lo que cambia
    // (p. ej. la durabilidad de un bloque golpeado); los demás millones de bloques no ocupan nada por esto.
    public static readonly Prototype BlockProto = new Prototype("bloque").Float("durability", 1f);
    // Maderas y hojas comparten prototipo raíz: los manejadores de fuego (GameEvents) se heredan a todas las variantes.
    public static readonly Prototype TrunkProto = BlockProto.Derive("tronco").Float("durability", 2f);
    public static readonly Prototype LeafProto = BlockProto.Derive("hojas");
    public static readonly ushort Grass, Dirt, Stone, Sand, Water, Log, Leaves, Snow, GrassTuft, FlowerRed, FlowerYellow, Rock, TallGrass, Bush, Lamp, LampWarm, LampBlue, LampGreen, GlassBlue;
    public static readonly ushort Crystal, Fungus, RustMetal, Salt, Ash, Panel, SandRed, SandBlack, Clay, DarkSoil, Moss, Lichen,
                                  Basalt, Strata, Chalk, StemWood, Petrified, Fronds, SporeCap;
    /// <summary>Bloques-sprite de flora del planeta (patch 4): sus imágenes las genera PlanetSprites con tamaño y color propios de cada planeta.</summary>
    public static readonly ushort[] FloraSprites = new ushort[16];
    public const ushort Air = BlockRegistry.Air;

    // Prototipos derivados con su durabilidad (golpes para romperlos). Se mantienen exactamente como estaban: los usan los manejadores de eventos.
    private static Prototype Hard(Prototype parent, string name, float hits) => parent.Derive(name).Float("durability", hits);

    // IMPORTANTE: el orden de registro define los ids guardados en disco. Añade bloques nuevos al final.
    static Blocks()
    {
        Grass = Registry.Define("grass").Tiles(TileGrassTop, TileGrassSide, TileDirt).Parent(BlockProto).Register();
        Dirt = Registry.Define("dirt").Tile(TileDirt).Parent(BlockProto).Register();
        Stone = Registry.Define("stone").Tile(TileStone).Props(Hard(BlockProto, "piedra", 3f)).Register();
        Sand = Registry.Define("sand").Tile(TileSand).Parent(BlockProto).Register();
        Water = Registry.Define("water").Tile(TileWater).Passable().NotOpaque().Translucent().Register();
        Log = Registry.Define("log").Tiles(TileLogTop, TileLogSide, TileLogTop).Parent(TrunkProto).Register();
        Leaves = Registry.Define("leaves").Tile(TileLeaves).NotOpaque().Parent(LeafProto).FilterLight(new Color(150, 150, 150)).Register();   // con huecos: se ve a través, y dan sombra a medias
        Snow = Registry.Define("snow").Tile(TileSnow).Parent(BlockProto).Register();

        // Sprites 2D importados de samples/MiniCraft/sprites/*.png. El primer argumento es el nombre del archivo, sin extensión.
        // Ancho/alto 0 = el sprite mide lo que mide su imagen (en bloques), así que pueden ser mayores que una celda.
        // Añade bloques siempre AL FINAL: el orden son los ids guardados en disco.
        GrassTuft = Registry.Define("grass_tuft").Sprite("grass_tuft").Register();
        FlowerRed = Registry.Define("flower_red").Sprite("flower_red").Register();
        FlowerYellow = Registry.Define("flower_yellow").Sprite("flower_yellow").Register();
        Rock = Registry.Define("rock").Sprite("rock", BlockRender.Billboard).Register();
        TallGrass = Registry.Define("tall_grass").Sprite("tall_grass").Register();       // 1 x 2 bloques
        Bush = Registry.Define("bush").Sprite("bush_large").Register();                   // 3 x 2,25 bloques

        // Lámparas: bloques emisores (nivel 15). Usan el tile BLANCO (nieve): la textura se tiñe con el color de su propia luz.
        Lamp = Registry.Define("lamp").Tile(TileSnow).Parent(BlockProto).Emits(15).Register();
        LampWarm = Registry.Define("lamp_warm").Tile(TileSnow).Parent(BlockProto).Emits(15, new Color(255, 205, 140)).Register();
        LampBlue = Registry.Define("lamp_blue").Tile(TileSnow).Parent(BlockProto).Emits(15, new Color(150, 185, 255)).Register();
        LampGreen = Registry.Define("lamp_green").Tile(TileSnow).Parent(BlockProto).Emits(15, new Color(160, 255, 170)).Register();
        GlassBlue = Registry.Define("glass_blue").Tile(TileWater).NotOpaque().Translucent().FilterLight(new Color(40, 90, 255)).Register();

        // ---- Bloques alienígenas (patch 2). Su color real lo pone la paleta del planeta (PlanetPalette) al recolorear el atlas.
        // Cristal y hongo emiten luz blanca: su color sale de la textura, que ya viene teñida por planeta.
        Crystal = Registry.Define("crystal").Tile(TileCrystal).NotOpaque().Translucent().Parent(BlockProto).Emits(12).Register();   // cuarzo: blanquecino y parcialmente transparente
        Fungus = Registry.Define("fungus").Tile(TileFungus).Parent(BlockProto).Emits(5).Register();
        RustMetal = Registry.Define("rust_metal").Tile(TileMetal).Props(Hard(BlockProto, "metal", 4f)).Register();
        Salt = Registry.Define("salt").Tile(TileSalt).Parent(BlockProto).Register();
        Ash = Registry.Define("ash").Tile(TileAsh).Parent(BlockProto).Register();
        Panel = Registry.Define("panel").Tile(TilePanel).Props(Hard(BlockProto, "metal", 4f)).Register();
        SandRed = Registry.Define("sand_red").Tile(TileSandRed).Parent(BlockProto).Register();
        SandBlack = Registry.Define("sand_black").Tile(TileSandBlack).Parent(BlockProto).Register();
        Clay = Registry.Define("clay").Tile(TileClay).Parent(BlockProto).Register();
        DarkSoil = Registry.Define("dark_soil").Tile(TileDarkSoil).Parent(BlockProto).Register();
        Moss = Registry.Define("moss").Tiles(TileMossTop, TileMossSide, TileDirt).Parent(BlockProto).Register();
        Lichen = Registry.Define("lichen").Tiles(TileLichenTop, TileLichenSide, TileDirt).Parent(BlockProto).Register();
        Basalt = Registry.Define("basalt").Tile(TileBasalt).Props(Hard(BlockProto, "piedra", 4f)).Register();
        Strata = Registry.Define("strata").Tile(TileStrata).Props(Hard(BlockProto, "piedra", 3f)).Register();
        Chalk = Registry.Define("chalk").Tile(TileChalk).Props(Hard(BlockProto, "piedra", 2f)).Register();
        StemWood = Registry.Define("stem_wood").Tiles(TileStemTop, TileStemSide, TileStemTop).Props(Hard(TrunkProto, "tallo", 2f)).Register();
        Petrified = Registry.Define("petrified_wood").Tiles(TilePetrifiedTop, TilePetrifiedSide, TilePetrifiedTop).Props(Hard(TrunkProto, "petrificada", 4f)).Register();
        Fronds = Registry.Define("fronds").Tile(TileFronds).NotOpaque().Parent(LeafProto.Derive("fronda")).FilterLight(new Color(150, 150, 150)).Register();
        SporeCap = Registry.Define("spore_cap").Tile(TileSporeCap).Parent(LeafProto.Derive("sombrero")).Emits(3).Register();
        for (int i = 0; i < FloraSprites.Length; i++)   // sprites de flora: añadidos AL FINAL
            FloraSprites[i] = Registry.Define($"flora_sprite_{i}").Sprite($"flora_{i}").Register();
    }
}
