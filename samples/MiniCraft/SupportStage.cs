using GF.Core;
using GF.World;
using GF.World.Voxel;

namespace MiniCraft;

/// <summary>
/// Última etapa de la generación: garantiza que nada queda en el aire. Todo lo sólido de un chunk tiene que estar conectado (26 vecinos) con una cara del chunk
/// (lateral, inferior o superior: por ahí sigue en el chunk vecino, que hace lo mismo con su parte) o con el suelo del propio chunk; lo que queda aislado (espiras,
/// urnas, estatuas, copas sin tronco, restos de excavar cuevas...) se elimina. Después, los sprites (hierba, rocas, flores...) necesitan un bloque sólido debajo.
/// Solo trabaja en chunks que cruzan la superficie; los de pura roca o pura aire se saltan. Es determinista y no depende del orden de generación.
/// </summary>
public sealed class SupportStage : IGenerationStage<ushort>
{
    private static readonly BlockTable Table = Blocks.Registry.GetTable();

    [ThreadStatic] private static bool[]? t_mark;
    [ThreadStatic] private static int[]? t_stack;

    private readonly TerrainColumnCache _columns;

    public SupportStage(TerrainColumnCache columns) => _columns = columns;

    private static bool IsSolid(ushort id) =>
        id != Blocks.Air && id != Blocks.Water && Table.Defs[id].Render == BlockRender.Cube;

    public void Apply(Chunk<ushort> chunk, GenerationContext ctx)
    {
        var shape = chunk.Shape;
        var cells = chunk.Cells;
        int sx = shape.SizeX, sy = shape.SizeY, sz = shape.SizeZ, stride = sx * sz;
        int oy = shape.Origin(chunk.Coord).Y;

        var col = _columns.GetColumn(chunk.Coord.X, chunk.Coord.Z);
        if (oy + sy - 1 <= col.MinHeight - 4 || oy > col.MaxHeight + 48) return;   // roca maciza o aire: nada que comprobar
        if (cells.AsSpan().IndexOfAnyExcept(Blocks.Air) < 0) return;

        var mark = t_mark;
        if (mark == null || mark.Length != cells.Length) t_mark = mark = new bool[cells.Length];
        else Array.Clear(mark);
        var stack = t_stack;
        if (stack == null || stack.Length != cells.Length) t_stack = stack = new int[cells.Length];
        int top = 0;

        // Semillas: sólidos en las caras del chunk.
        void Seed(int i)
        {
            if (mark[i] || !IsSolid(cells[i])) return;
            mark[i] = true;
            stack[top++] = i;
        }
        for (int z = 0; z < sz; z++)
        for (int x = 0; x < sx; x++) { Seed(z * sx + x); Seed(((sy - 1) * sz + z) * sx + x); }
        for (int y = 1; y < sy - 1; y++)
        {
            for (int z = 0; z < sz; z++) { Seed((y * sz + z) * sx); Seed((y * sz + z) * sx + sx - 1); }
            for (int x = 0; x < sx; x++) { Seed(y * stride + x); Seed(y * stride + (sz - 1) * sx + x); }
        }

        // Relleno por conectividad de 26 vecinos (las hojas y las frondas de las palmas se tocan por aristas).
        while (top > 0)
        {
            int i = stack[--top];
            int x = i % sx, z = (i / sx) % sz, y = i / stride;
            for (int dy = -1; dy <= 1; dy++)
            {
                int ny = y + dy;
                if ((uint)ny >= (uint)sy) continue;
                for (int dz = -1; dz <= 1; dz++)
                {
                    int nz = z + dz;
                    if ((uint)nz >= (uint)sz) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx;
                        if ((uint)nx >= (uint)sx) continue;
                        int n = (ny * sz + nz) * sx + nx;
                        if (mark[n] || !IsSolid(cells[n])) continue;
                        mark[n] = true;
                        stack[top++] = n;
                    }
                }
            }
        }

        // Lo sólido no conectado con nada, fuera. Después, los sprites sin un sólido debajo (de abajo arriba; en la fila 0 se supone suelo: es el chunk de debajo).
        for (int i = 0; i < cells.Length; i++)
            if (!mark[i] && IsSolid(cells[i])) cells[i] = Blocks.Air;

        for (int y = 1; y < sy; y++)
        for (int i = y * stride, end = i + stride; i < end; i++)
        {
            ushort id = cells[i];
            if (id == Blocks.Air || id == Blocks.Water || Table.Defs[id].Render == BlockRender.Cube) continue;
            if (!IsSolid(cells[i - stride])) cells[i] = Blocks.Air;
        }
    }
}
