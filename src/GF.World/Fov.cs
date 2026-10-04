using GF.Core;

namespace GF.World;

/// <summary>Campo de visión 2D por shadowcasting recursivo, sobre el plano Z del origen.</summary>
public static class Fov
{
    private static readonly int[,] Mult =
    {
        { 1, 0, 0, -1, -1, 0, 0, 1 },
        { 0, 1, -1, 0, 0, -1, 1, 0 },
        { 0, 1, 1, 0, 0, -1, -1, 0 },
        { 1, 0, 0, 1, -1, 0, 0, -1 },
    };

    /// <param name="visit">Se invoca por cada celda visible (incluidos los muros que bloquean la vista).</param>
    public static void Compute<TCell>(IWorld<TCell> world, CellCoord origin, int radius,
        Func<TCell, bool> blocksLight, Action<CellCoord> visit) where TCell : unmanaged
    {
        visit(origin);
        var scanner = new Scanner<TCell>(world, origin, radius, blocksLight, visit);
        for (int oct = 0; oct < 8; oct++) scanner.Scan(1, 1.0, 0.0, oct);
    }

    private sealed class Scanner<TCell> where TCell : unmanaged
    {
        private readonly IWorld<TCell> _world;
        private readonly CellCoord _o;
        private readonly int _radius;
        private readonly Func<TCell, bool> _blocks;
        private readonly Action<CellCoord> _visit;

        public Scanner(IWorld<TCell> world, CellCoord o, int radius, Func<TCell, bool> blocks, Action<CellCoord> visit)
        {
            _world = world; _o = o; _radius = radius; _blocks = blocks; _visit = visit;
        }

        public void Scan(int row, double start, double end, int oct)
        {
            if (start < end) return;
            int xx = Mult[0, oct], xy = Mult[1, oct], yx = Mult[2, oct], yy = Mult[3, oct];
            double newStart = 0;
            bool blocked = false;

            for (int distance = row; distance <= _radius && !blocked; distance++)
            {
                int dy = -distance;
                for (int dx = -distance; dx <= 0; dx++)
                {
                    var cell = new CellCoord(_o.X + dx * xx + dy * xy, _o.Y + dx * yx + dy * yy, _o.Z);
                    double lSlope = (dx - 0.5) / (dy + 0.5);
                    double rSlope = (dx + 0.5) / (dy - 0.5);
                    if (start < rSlope) continue;
                    if (end > lSlope) break;

                    if (dx * dx + dy * dy <= _radius * _radius) _visit(cell);
                    bool isBlocking = _blocks(_world.GetCell(cell));

                    if (blocked)
                    {
                        if (isBlocking) { newStart = rSlope; continue; }
                        blocked = false;
                        start = newStart;
                    }
                    else if (isBlocking && distance < _radius)
                    {
                        blocked = true;
                        Scan(distance + 1, start, lSlope, oct);
                        newStart = rSlope;
                    }
                }
            }
        }
    }
}
