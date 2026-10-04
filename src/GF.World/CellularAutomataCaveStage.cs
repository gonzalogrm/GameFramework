using GF.Core;

namespace GF.World;

/// <summary>
/// Cuevas con autómata celular (estilo roguelike). El ruido inicial sale de un hash por coordenada de mundo
/// y cada chunk simula con un margen igual al número de iteraciones, así que los bordes entre chunks
/// coinciden exactamente, sin costuras y con cualquier orden de generación.
/// Rellena todas las capas Z del chunk con el mismo patrón 2D.
/// </summary>
public sealed class CellularAutomataCaveStage<TCell> : IGenerationStage<TCell> where TCell : unmanaged
{
    private readonly TCell _floor, _wall;
    private readonly float _fill;
    private readonly int _iterations;

    public CellularAutomataCaveStage(TCell floor, TCell wall, float wallFillRatio = 0.45f, int iterations = 4)
    {
        _floor = floor; _wall = wall; _fill = wallFillRatio; _iterations = iterations;
    }

    public void Apply(Chunk<TCell> chunk, GenerationContext ctx)
    {
        var shape = chunk.Shape;
        var origin = shape.Origin(chunk.Coord);
        int pad = _iterations;
        int w = shape.SizeX + 2 * pad, h = shape.SizeY + 2 * pad;
        var cur = new bool[w * h];     // true = muro
        var next = new bool[w * h];

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            ulong hash = Hashing.Combine(ctx.Seed, origin.X + x - pad, origin.Y + y - pad, 0x5EED);
            cur[y * w + x] = (hash >> 40) * (1f / (1 << 24)) < _fill;
        }

        for (int it = 0; it < _iterations; it++)
        {
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int walls = 0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if ((uint)nx >= (uint)w || (uint)ny >= (uint)h || cur[ny * w + nx]) walls++;
                }
                next[y * w + x] = walls >= 5 || (cur[y * w + x] && walls == 4);
            }
            (cur, next) = (next, cur);
        }

        for (int z = 0; z < shape.SizeZ; z++)
        for (int y = 0; y < shape.SizeY; y++)
        for (int x = 0; x < shape.SizeX; x++)
            chunk[x, y, z] = cur[(y + pad) * w + x + pad] ? _wall : _floor;
    }
}
