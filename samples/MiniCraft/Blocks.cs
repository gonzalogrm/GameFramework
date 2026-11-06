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

    // IMPORTANTE: el orden de registro define los ids guardados en disco. Añade bloques nuevos al final.
    static Blocks()
    {
        Grass = Registry.Register("grass", BlockDef.TopSideBottom("grass", TileGrassTop, TileGrassSide, TileDirt) with { Props = BlockProto });
        Dirt = Registry.Register("dirt", BlockDef.Cube("dirt", TileDirt) with { Props = BlockProto });
        Stone = Registry.Register("stone", BlockDef.Cube("stone", TileStone) with { Props = BlockProto.Derive("piedra").Float("durability", 3f) });
        Sand = Registry.Register("sand", BlockDef.Cube("sand", TileSand) with { Props = BlockProto });
        Water = Registry.Register("water", BlockDef.Cube("water", TileWater, solid: false, opaque: false, translucent: true));
        Log = Registry.Register("log", BlockDef.TopSideBottom("log", TileLogTop, TileLogSide, TileLogTop) with { Props = TrunkProto });
        Leaves = Registry.Register("leaves", BlockDef.Cube("leaves", TileLeaves) with { Props = LeafProto });
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

        // Lámparas: bloques emisores (nivel 15). Usan el tile BLANCO (nieve): la textura se tiñe con el color de su propia luz.
        Lamp = Registry.Register("lamp", BlockDef.Cube("lamp", TileSnow) with { Props = BlockProto, Emission = 15 });
        LampWarm = Registry.Register("lamp_warm", BlockDef.Cube("lamp_warm", TileSnow) with { Props = BlockProto, Emission = 15, LightColor = new Color(255, 205, 140) });
        LampBlue = Registry.Register("lamp_blue", BlockDef.Cube("lamp_blue", TileSnow) with { Props = BlockProto, Emission = 15, LightColor = new Color(150, 185, 255) });
        LampGreen = Registry.Register("lamp_green", BlockDef.Cube("lamp_green", TileSnow) with { Props = BlockProto, Emission = 15, LightColor = new Color(160, 255, 170) });
        GlassBlue = Registry.Register("glass_blue", BlockDef.Cube("glass_blue", TileWater, solid: true, opaque: false, translucent: true) with { LightFilter = new Color(40, 90, 255) });

        // ---- Bloques alienígenas (patch 2). Su color real lo pone la paleta del planeta (PlanetPalette) al recolorear el atlas.
        // Cristal y hongo emiten luz blanca: su color sale de la textura, que ya viene teñida por planeta.
        Crystal = Registry.Register("crystal", BlockDef.Cube("crystal", TileCrystal) with { Props = BlockProto, Emission = 10 });
        Fungus = Registry.Register("fungus", BlockDef.Cube("fungus", TileFungus) with { Props = BlockProto, Emission = 10 });
        RustMetal = Registry.Register("rust_metal", BlockDef.Cube("rust_metal", TileMetal) with { Props = BlockProto.Derive("metal").Float("durability", 4f) });
        Salt = Registry.Register("salt", BlockDef.Cube("salt", TileSalt) with { Props = BlockProto });
        Ash = Registry.Register("ash", BlockDef.Cube("ash", TileAsh) with { Props = BlockProto });
        Panel = Registry.Register("panel", BlockDef.Cube("panel", TilePanel) with { Props = BlockProto.Derive("metal").Float("durability", 4f) });
        SandRed = Registry.Register("sand_red", BlockDef.Cube("sand_red", TileSandRed) with { Props = BlockProto });
        SandBlack = Registry.Register("sand_black", BlockDef.Cube("sand_black", TileSandBlack) with { Props = BlockProto });
        Clay = Registry.Register("clay", BlockDef.Cube("clay", TileClay) with { Props = BlockProto });
        DarkSoil = Registry.Register("dark_soil", BlockDef.Cube("dark_soil", TileDarkSoil) with { Props = BlockProto });
        Moss = Registry.Register("moss", BlockDef.TopSideBottom("moss", TileMossTop, TileMossSide, TileDirt) with { Props = BlockProto });
        Lichen = Registry.Register("lichen", BlockDef.TopSideBottom("lichen", TileLichenTop, TileLichenSide, TileDirt) with { Props = BlockProto });
        Basalt = Registry.Register("basalt", BlockDef.Cube("basalt", TileBasalt) with { Props = BlockProto.Derive("piedra").Float("durability", 4f) });
        Strata = Registry.Register("strata", BlockDef.Cube("strata", TileStrata) with { Props = BlockProto.Derive("piedra").Float("durability", 3f) });
        Chalk = Registry.Register("chalk", BlockDef.Cube("chalk", TileChalk) with { Props = BlockProto.Derive("piedra").Float("durability", 2f) });
        StemWood = Registry.Register("stem_wood", BlockDef.TopSideBottom("stem_wood", TileStemTop, TileStemSide, TileStemTop) with { Props = TrunkProto.Derive("tallo").Float("durability", 2f) });
        Petrified = Registry.Register("petrified_wood", BlockDef.TopSideBottom("petrified_wood", TilePetrifiedTop, TilePetrifiedSide, TilePetrifiedTop) with { Props = TrunkProto.Derive("petrificada").Float("durability", 4f) });
        Fronds = Registry.Register("fronds", BlockDef.Cube("fronds", TileFronds) with { Props = LeafProto.Derive("fronda") });
        SporeCap = Registry.Register("spore_cap", BlockDef.Cube("spore_cap", TileSporeCap) with { Props = LeafProto.Derive("sombrero"), Emission = 10 });
        for (int i = 0; i < FloraSprites.Length; i++)   // sprites de flora: añadidos AL FINAL
            FloraSprites[i] = Registry.Register($"flora_sprite_{i}", BlockDef.Sprite($"flora_sprite_{i}", $"flora_{i}", BlockRender.Cross));
    }
}
