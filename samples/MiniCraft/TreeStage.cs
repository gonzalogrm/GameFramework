using GF.Core;
using GF.World;
using GF.World.Map;

namespace MiniCraft;

/// <summary>
/// Árboles y arbustos procedurales (ver Flora y FloraShapes) y sprites de flora del planeta (FloraSprites). La posición de cada planta depende solo de
/// (semilla, x envuelta, z) y de su bioma, nunca del chunk: cada chunk examina las columnas de su zona ampliada por Flora.MaxReach y escribe únicamente las
/// celdas que caen dentro de sí mismo, así no hay costuras entre chunks ni en la costura este-oeste.
/// Ajustes (worldsettings.json): TreeDensityMultiplier y VegetationDensity (cantidad general), FloraSpriteShare (fracción de plantas que son sprites),
/// FungusDensityMultiplier (setas), CrystalDensityMultiplier (cristales) y RockDensityMultiplier (formaciones rocosas).
/// No aparecen plantas en las zonas reservadas a ciudades (CityPlanner).
/// </summary>
public sealed class TreeStage : IGenerationStage<ushort>
{
    private readonly MiniCraftClimate _climate;
    private readonly WorldScale _scale;
    private readonly TerrainColumnCache _columns;
    private readonly CityPlanner _planner;

    public TreeStage(MiniCraftClimate climate, WorldScale scale, TerrainColumnCache columns, CityPlanner planner)
    {
        _climate = climate;
        _scale = scale;
        _columns = columns;
        _planner = planner;
    }

    public void Apply(Chunk<ushort> chunk, GenerationContext ctx)
    {
        var shape = chunk.Shape;
        var o = shape.Origin(chunk.Coord);
        var flora = Flora.Current;
        var settings = _climate.Settings;
        float mult = settings.TreeDensityMultiplier * settings.VegetationDensity;
        float spriteShare = Math.Clamp(settings.FloraSpriteShare, 0f, 1f);
        float fungusMult = settings.FungusDensityMultiplier, rockMult = settings.RockDensityMultiplier, crystalMult = settings.CrystalDensityMultiplier;
        float boost = Math.Max(1f, Math.Max(fungusMult, Math.Max(rockMult, crystalMult)));   // las categorías por encima de 1 necesitan más candidatas
        int minGround = _climate.SeaLevel + settings.BeachHeight;
        int worldHeight = settings.WorldHeight;
        ushort fungusSprite = Blocks.FloraSprites[13];
        const int Reach = Flora.MaxReach;

        // Sin suelo posible en este chunk: todo por debajo del nivel mínimo, o por encima de la cima más la planta más alta.
        if (o.Y + shape.SizeY <= minGround || o.Y > _climate.MaxHeightBlocks + 28) return;

        float maxTree = flora.MaxTreeDensity * mult * boost, maxShrub = flora.MaxShrubDensity * mult * boost;
        FloraWriter? writer = null;

        for (int wz = o.Z - Reach; wz < o.Z + shape.SizeZ + Reach; wz++)
        for (int wx = o.X - Reach; wx < o.X + shape.SizeX + Reach; wx++)
        {
            if (wz < 0 || wz >= _scale.HeightBlocks) continue;

            int wrapped = _scale.WrapX(wx);
            ulong ht = Hashing.Combine(ctx.Seed, wrapped, 0x7EE, wz);
            ulong hs = Hashing.Combine(ctx.Seed, wrapped, 0x5B7, wz);
            float ut = (ht >> 40) * (1f / (1 << 24)), us = (hs >> 40) * (1f / (1 << 24));
            bool treeCandidate = ut < maxTree, shrubCandidate = us < maxShrub;
            if (!treeCandidate && !shrubCandidate) continue;   // descarte barato antes de mirar el terreno

            if (_planner.IsReserved(wx, wz)) continue;         // zona de ciudad

            int ground = _columns.HeightAt(wx, wz);
            if (ground <= minGround) continue;
            ushort biome = _columns.BiomeAt(wx, wz);

            // Primero se intenta un árbol; si no, un arbusto (cada uno con su tirada y su densidad).
            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool shrub = attempt == 1;
                if (!(shrub ? shrubCandidate : treeCandidate)) continue;
                ulong h = shrub ? hs : ht;
                float u = shrub ? us : ut;
                float density = (shrub ? flora.ShrubDensity(biome) : flora.TreeDensity(biome)) * mult;

                float roll = ((h >> 24) & 0xFFFF) / 65536f;
                if (roll < spriteShare)   // sprite 2D del planeta
                {
                    ushort spriteBlock = FloraSprites.Current.Pick(biome, shrub, h);
                    if (spriteBlock != 0)
                    {
                        if (u >= density * (spriteBlock == fungusSprite ? fungusMult : 1f)) continue;
                        writer ??= new FloraWriter(chunk, o);
                        writer.SetBase(wx, ground + 1, wz);
                        writer.Put(0, 0, 0, spriteBlock, false);
                        break;
                    }
                }

                var species = flora.Pick(biome, shrub, h);
                if (species == null) continue;
                float category = species.Shape switch
                {
                    FloraShape.Mushroom => fungusMult,
                    FloraShape.Spire => species.Trunk == Blocks.Crystal ? crystalMult : rockMult,
                    _ => 1f,
                };
                if (u >= density * category) continue;
                if (ground + 1 + species.MaxH + 4 > worldHeight - 1) continue;   // no cabe bajo el techo del mundo

                int height = species.MinH + (int)((h >> 8) % (ulong)(species.MaxH - species.MinH + 1));
                int radius = species.MinR + (int)((h >> 12) % (ulong)(species.MaxR - species.MinR + 1));
                writer ??= new FloraWriter(chunk, o);
                writer.SetBase(wx, ground + 1, wz);
                FloraShapes.Build(writer, species, height, radius, new SplitMixRandom(h ^ 0xF10CAUL));
                break;
            }
        }
    }
}
