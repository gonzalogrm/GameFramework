using GF.Core;
using GF.World;
using GF.World.Map;

namespace MiniCraft;

/// <summary>
/// Campos de estatuas y urnas de varios kilómetros (ver StatueFields). Sobre una malla de 16 x 16 con desplazamiento al azar hay, con una probabilidad que baja hacia
/// el borde de la región, una figura de piedra: estatua (entera, sin cabeza, rota o caída), obelisco, estela, grupo de urnas o mausoleo. Solo en terreno llano sobre
/// el agua y fuera de ciudades y ruinas. Cada figura se calcula entera a partir de (semilla, celda de la malla), así que no hay costuras entre chunks. Va después de
/// CityStage y antes de los árboles.
/// </summary>
public sealed class StatueFieldStage : IGenerationStage<ushort>
{
    private const int Spacing = 16, Reach = 6;

    private readonly int _seed, _sea;
    private readonly double _density;
    private readonly CityPlanner _planner;
    private readonly WorldScale _scale;
    private readonly TerrainColumnCache _columns;

    public StatueFieldStage(int seed, CityPlanner planner, WorldScale scale, TerrainColumnCache columns, int seaLevel, float density = 0.65f)
    {
        _density = density;
        _seed = seed; _planner = planner; _scale = scale; _columns = columns; _sea = seaLevel;
    }

    public void Apply(Chunk<ushort> chunk, GenerationContext ctx)
    {
        var shape = chunk.Shape;
        var o = shape.Origin(chunk.Coord);
        int sx = shape.SizeX, sy = shape.SizeY, sz = shape.SizeZ;

        bool any = false;   // ¿toca alguna región este chunk? (9 puntos)
        for (int i = 0; i <= 2 && !any; i++)
        for (int j = 0; j <= 2 && !any; j++)
            any = StatueFields.RegionAt(_seed, _scale, o.X + sx * i / 2, o.Z + sz * j / 2, out _) != null;
        if (!any) return;

        int cellsX = Math.Max(1, _scale.WidthBlocks / Spacing);
        int gx0 = IntMath.FloorDiv(o.X - Reach - 5, Spacing), gx1 = IntMath.FloorDiv(o.X + sx + Reach + 5, Spacing);
        int gz0 = IntMath.FloorDiv(o.Z - Reach - 5, Spacing), gz1 = IntMath.FloorDiv(o.Z + sz + Reach + 5, Spacing);

        for (int gz = gz0; gz <= gz1; gz++)
        for (int gx = gx0; gx <= gx1; gx++)
        {
            int gw = ((gx % cellsX) + cellsX) % cellsX;   // la malla da la vuelta con el mundo
            ulong h = Hashing.Combine(_seed, gw, 0xF1E1E, gz);
            int cx = gx * Spacing + (int)(h % 11UL) - 5, cz = gz * Spacing + (int)((h >> 8) % 11UL) - 5;
            if (cx + Reach < o.X || cx - Reach >= o.X + sx || cz + Reach < o.Z || cz - Reach >= o.Z + sz) continue;
            if ((uint)cz >= (uint)_scale.HeightBlocks) continue;

            var region = StatueFields.RegionAt(_seed, _scale, cx, cz, out double f);
            if (region == null) continue;
            if ((h >> 40) * (1.0 / (1 << 24)) >= _density * Math.Clamp(f / 0.4, 0.0, 1.0)) continue;   // más escasas hacia el borde
            if (_planner.IsReserved(cx, cz)) continue;

            int g0 = _columns.HeightAt(cx, cz);
            if (g0 <= _sea + 1 || g0 + 1 > o.Y + sy - 1 || g0 + 12 < o.Y) continue;
            if (Math.Abs(_columns.HeightAt(cx - 3, cz) - g0) > 2 || Math.Abs(_columns.HeightAt(cx + 3, cz) - g0) > 2 ||
                Math.Abs(_columns.HeightAt(cx, cz - 3) - g0) > 2 || Math.Abs(_columns.HeightAt(cx, cz + 3) - g0) > 2) continue;   // solo terreno llano

            int kind = region.Kind == 2 ? (int)((h >> 36) & 1UL) : region.Kind;
            double tp = ((h >> 20) & 0xFFFF) / 65536.0;
            // 0 estatua, 1 obelisco, 2 estela, 3 grupo de urnas, 4 mausoleo
            int type = kind == 0 ? (tp < 0.72 ? 0 : tp < 0.84 ? 1 : 2)
                                 : (tp < 0.70 ? 3 : tp < 0.85 ? 2 : tp < 0.95 ? 4 : 1);
            double u = ((h >> 44) & 0xFFFFF) / (double)0x100000;
            int state = u < 0.35 ? 0 : u < 0.6 ? 1 : u < 0.8 ? 2 : 3;
            Place(chunk, o, cx, cz, g0, type, state, h);
        }
    }

    /// <summary>Grupo de urnas: una malla de 5 x 5 con una urna cada 2 bloques (el 70 % de los puntos).</summary>
    private static ushort UrnCluster(int dx, int dz, int r, ushort P, ulong h)
    {
        if (((dx % 2) + 2) % 2 != 0 || ((dz % 2) + 2) % 2 != 0 || Math.Abs(dx) > 4 || Math.Abs(dz) > 4) return Blocks.Air;
        ulong ch = Hashing.Combine(unchecked((int)h), dx, 0x5E9, dz);
        return (ch >> 40) * (1.0 / (1 << 24)) < 0.7 ? StatueShapes.Urn(0, 0, r, P) : Blocks.Air;
    }

    private void Place(Chunk<ushort> chunk, CellCoord o, int cx, int cz, int g0, int type, int state, ulong h)
    {
        var shape = chunk.Shape;
        int sx = shape.SizeX, sy = shape.SizeY, sz = shape.SizeZ;
        var cells = chunk.Cells;
        ushort P = Blocks.Stone;
        int maxR = type == 1 ? 10 : type == 0 ? 6 : 4;

        for (int dz = -5; dz <= 5; dz++)
        for (int dx = -5; dx <= 5; dx++)
        {
            int wx = cx + dx, wz = cz + dz, lx = wx - o.X, lz = wz - o.Z;
            if ((uint)lx >= (uint)sx || (uint)lz >= (uint)sz) continue;
            for (int r = 1; r <= maxR; r++)
            {
                ushort id = type switch
                {
                    0 => StatueShapes.Statue(state, dx, dz, r, P),
                    1 => StatueShapes.Obelisk(dx, dz, r, P),
                    2 => StatueShapes.Stele(dx, dz, r, P),
                    3 => UrnCluster(dx, dz, r, P, h),
                    _ => StatueShapes.Mausoleum(dx, dz, r, P),
                };
                if (id == Blocks.Air) continue;

                if (r == 1)   // cimientos: si el suelo de esta columna queda más bajo que la base, se rellena (hasta 3 bloques)
                {
                    int gc = _columns.HeightAt(wx, wz);
                    if (gc < g0 && g0 - gc <= 3)
                        for (int yy = gc + 1; yy <= g0; yy++)
                        {
                            int y2 = yy - o.Y;
                            if ((uint)y2 >= (uint)sy) continue;
                            int i2 = (y2 * sz + lz) * sx + lx;
                            if (cells[i2] == Blocks.Air) cells[i2] = Blocks.Dirt;
                        }
                }

                int y = g0 + r - o.Y;
                if ((uint)y >= (uint)sy) continue;
                int idx = (y * sz + lz) * sx + lx;
                if (cells[idx] == Blocks.Air) cells[idx] = id;
            }
        }
    }
}
