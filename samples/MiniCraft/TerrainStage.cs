using GF.World;

namespace MiniCraft;

/// <summary>
/// Terreno a partir de la caché de columnas (altura y bioma). Escribe directamente en el array del chunk y tiene salidas
/// rápidas: un chunk por encima del terreno se queda de aire sin tocarlo, y uno totalmente enterrado se rellena de roca de golpe.
/// La roca profunda es la del planeta (Biomes.DeepStone). Al final se excavan las cuevas (CaveCarver, según WorldSettings.CaveDensity).
/// </summary>
public sealed class TerrainStage : IGenerationStage<ushort>
{
    private readonly MiniCraftClimate _climate;
    private readonly TerrainColumnCache _columns;

    public TerrainStage(MiniCraftClimate climate, TerrainColumnCache columns)
    {
        _climate = climate;
        _columns = columns;
    }

    public void Apply(Chunk<ushort> chunk, GenerationContext ctx)
    {
        var shape = chunk.Shape;
        var cells = chunk.Cells;
        int sx = shape.SizeX, sy = shape.SizeY, sz = shape.SizeZ;
        int oy = shape.Origin(chunk.Coord).Y;
        int sea = _climate.SeaLevel;
        var col = _columns.GetColumn(chunk.Coord.X, chunk.Coord.Z);
        ushort deep = Biomes.DeepStone;
        var surface = Biomes.Surface;
        var filler = Biomes.Filler;
        var settings = _climate.Settings;

        // Por encima de todo el terreno y del mar: aire (el array ya viene a cero).
        if (oy > Math.Max(col.MaxHeight, sea)) return;

        // Por debajo de la capa de relleno de todas las columnas: roca maciza (y aquí es donde más cuevas hay).
        if (oy + sy - 1 <= col.MinHeight - 4)
        {
            Array.Fill(cells, deep);
            CaveCarver.Carve(chunk, ctx.Noise, ctx.Seed, col.Height, sea, settings.CaveDensity, settings.Vegetation.CaveCrystals, settings.Vegetation.CaveFungi, deep);
            return;
        }

        for (int z = 0; z < sz; z++)
        for (int x = 0; x < sx; x++)
        {
            int c = x + z * sx;
            int h = col.Height[c];
            ushort top = surface[col.Biome[c]], fill = filler[col.Biome[c]];

            // Por encima de max(h, mar) solo hay aire: no hace falta escribir.
            int yMax = Math.Min(sy - 1, Math.Max(h, sea) - oy);
            for (int y = 0; y <= yMax; y++)
            {
                int wy = oy + y;
                ushort id;
                if (wy > h) id = Blocks.Water;          // entre el suelo y el mar
                else if (wy == h) id = top;
                else if (wy >= h - 3) id = fill;
                else id = deep;
                cells[(y * sz + z) * sx + x] = id;
            }
        }
        CaveCarver.Carve(chunk, ctx.Noise, ctx.Seed, col.Height, sea, settings.CaveDensity, settings.Vegetation.CaveCrystals, settings.Vegetation.CaveFungi, deep);
    }
}
