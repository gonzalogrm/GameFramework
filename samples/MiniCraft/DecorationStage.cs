using GF.Core;
using GF.World;
using GF.World.Map;

namespace MiniCraft;

/// <summary>
/// Hierba, hierba alta, flores, matorrales grandes y rocas (bloques-sprite) sobre la superficie. Igual que los árboles, la
/// decisión depende solo de (semilla, x envuelta, z) y del bioma de la columna, así que no hay costuras entre chunks. Va después
/// de los árboles: no pisa nada que ya ocupe la celda. Un sprite grande (el matorral mide 3 x 2,25 bloques) solo necesita su
/// celda de anclaje libre; el resto del dibujo sobresale por encima y a los lados.
/// </summary>
public sealed class DecorationStage : IGenerationStage<ushort>
{
    private const float MaxProbability = 0.36f;   // suma máxima de las densidades de cualquier bioma

    /// <summary>Probabilidad por columna de cada tipo de decoración.</summary>
    private readonly record struct Props(float Grass, float Tall, float Flower, float Bush, float Rock);

    private static readonly Props[] Density = BuildDensity();

    private readonly MiniCraftClimate _climate;
    private readonly WorldScale _scale;
    private readonly TerrainColumnCache _columns;

    public DecorationStage(MiniCraftClimate climate, WorldScale scale, TerrainColumnCache columns)
    {
        _climate = climate;
        _scale = scale;
        _columns = columns;
    }

    private static Props[] BuildDensity()
    {
        var d = new Props[Biomes.Registry.Count];
        d[Biomes.Plains] = new Props(0.20f, 0.060f, 0.030f, 0.004f, 0.004f);
        d[Biomes.Forest] = new Props(0.16f, 0.030f, 0.020f, 0.012f, 0.004f);
        d[Biomes.Savanna] = new Props(0.10f, 0.080f, 0.005f, 0.003f, 0.006f);
        d[Biomes.Jungle] = new Props(0.24f, 0.060f, 0.030f, 0.020f, 0.002f);
        d[Biomes.Taiga] = new Props(0.09f, 0.010f, 0.004f, 0.006f, 0.006f);
        d[Biomes.Tundra] = new Props(0f, 0f, 0f, 0f, 0.006f);
        d[Biomes.Desert] = new Props(0f, 0f, 0f, 0f, 0.008f);
        d[Biomes.Mountains] = new Props(0f, 0f, 0f, 0f, 0.010f);
        d[Biomes.SnowPeaks] = new Props(0f, 0f, 0f, 0f, 0.004f);
        return d;
    }

    public void Apply(Chunk<ushort> chunk, GenerationContext ctx)
    {
        var shape = chunk.Shape;
        var o = shape.Origin(chunk.Coord);
        float mult = _climate.Settings.PropDensityMultiplier;
        if (mult <= 0f) return;

        for (int z = 0; z < shape.SizeZ; z++)
        for (int x = 0; x < shape.SizeX; x++)
        {
            int wx = o.X + x, wz = o.Z + z;
            ulong h = Hashing.Combine(ctx.Seed, _scale.WrapX(wx), 0xDEC0, wz);
            float u = (h >> 40) * (1f / (1 << 24));
            if (u >= MaxProbability * mult) continue;   // descarte barato antes de mirar el terreno

            int ground = _columns.HeightAt(wx, wz);
            if (ground <= _climate.SeaLevel) continue;   // bajo el agua no hay nada

            var d = Density[_columns.BiomeAt(wx, wz)];
            float acc = d.Grass * mult;
            ushort id;
            if (u < acc) id = Blocks.GrassTuft;
            else if (u < (acc += d.Tall * mult)) id = Blocks.TallGrass;
            else if (u < (acc += d.Flower * mult)) id = ((h >> 4) & 1) == 0 ? Blocks.FlowerRed : Blocks.FlowerYellow;
            else if (u < (acc += d.Bush * mult)) id = Blocks.Bush;
            else if (u < (acc += d.Rock * mult)) id = Blocks.Rock;
            else continue;

            int y = ground + 1 - o.Y;                                   // la celda justo encima del suelo
            if ((uint)y >= (uint)shape.SizeY) continue;                 // pertenece a otro chunk (el de arriba)
            if (chunk[x, y, z] != Blocks.Air) continue;                 // un árbol u otra cosa ya la ocupa
            chunk[x, y, z] = id;
        }
    }
}
