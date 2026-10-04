using GF.Core;
using GF.World;
using GF.World.Map;

namespace MiniCraft;

/// <summary>
/// Árboles que cruzan los bordes de chunk (y la costura este-oeste) sin costuras. La posición de cada árbol
/// depende solo de (semilla, x envuelta, z), del clima de esa columna y de su bioma, nunca del chunk.
/// Cada chunk examina las columnas de su zona ampliada por el radio de la copa y escribe únicamente las
/// celdas que caen dentro de sí mismo. Precedencia fija (tronco > hojas > aire).
/// </summary>
public sealed class TreeStage : IGenerationStage<ushort>
{
    private const int CanopyRadius = 2;

    private readonly MiniCraftClimate _climate;
    private readonly WorldScale _scale;
    private readonly TerrainColumnCache _columns;

    public TreeStage(MiniCraftClimate climate, WorldScale scale, TerrainColumnCache columns)
    {
        _climate = climate;
        _scale = scale;
        _columns = columns;
    }

    public void Apply(Chunk<ushort> chunk, GenerationContext ctx)
    {
        var shape = chunk.Shape;
        var o = shape.Origin(chunk.Coord);
        float density = _climate.Settings.TreeDensityMultiplier;
        int minGround = _climate.SeaLevel + _climate.Settings.BeachHeight;

        for (int wz = o.Z - CanopyRadius; wz < o.Z + shape.SizeZ + CanopyRadius; wz++)
        for (int wx = o.X - CanopyRadius; wx < o.X + shape.SizeX + CanopyRadius; wx++)
        {
            if (wz < 0 || wz >= _scale.HeightBlocks) continue;

            ulong h = Hashing.Combine(ctx.Seed, _scale.WrapX(wx), 0x7EE, wz);
            float u = (h >> 40) * (1f / (1 << 24));
            if (u >= Biomes.MaxTreeDensity * density) continue;   // descarte barato antes de calcular el clima

            if (u >= Biomes.TreeDensity[_columns.BiomeAt(wx, wz)] * density) continue;

            int ground = _columns.HeightAt(wx, wz);
            if (ground <= minGround || ground > _climate.MaxTreeGround) continue;

            int trunk = 4 + (int)((h >> 8) % 3);   // 4..6
            PlaceTree(chunk, o, wx, ground + 1, wz, trunk);
        }
    }

    private static void PlaceTree(Chunk<ushort> chunk, CellCoord o, int tx, int ty, int tz, int trunk)
    {
        // Copa: capas de trunk-2 a trunk+1 (radio 2, luego 1, y punta).
        for (int dy = trunk - 2; dy <= trunk + 1; dy++)
        {
            int r = dy >= trunk ? 1 : 2;
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (r == 2 && Math.Abs(dx) == 2 && Math.Abs(dz) == 2) continue;      // sin esquinas
                if (dy == trunk + 1 && Math.Abs(dx) + Math.Abs(dz) > 1) continue;    // punta en cruz
                Set(chunk, o, tx + dx, ty + dy, tz + dz, Blocks.Leaves, overwriteLeaves: false);
            }
        }
        for (int dy = 0; dy < trunk; dy++)
            Set(chunk, o, tx, ty + dy, tz, Blocks.Log, overwriteLeaves: true);
    }

    private static void Set(Chunk<ushort> chunk, CellCoord o, int wx, int wy, int wz, ushort id, bool overwriteLeaves)
    {
        var s = chunk.Shape;
        int x = wx - o.X, y = wy - o.Y, z = wz - o.Z;
        if ((uint)x >= (uint)s.SizeX || (uint)y >= (uint)s.SizeY || (uint)z >= (uint)s.SizeZ) return;

        ushort cur = chunk[x, y, z];
        if (cur == Blocks.Air || (overwriteLeaves && cur == Blocks.Leaves)) chunk[x, y, z] = id;
    }
}
