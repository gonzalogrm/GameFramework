using GF.Core;

namespace GF.World;

/// <summary>A* sobre la rejilla 2D (8 direcciones, sin cortar esquinas). Devuelve el camino incluyendo inicio y meta.</summary>
public static class AStar
{
    private static readonly (int X, int Y)[] Dirs =
        { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) };

    public static List<CellCoord>? FindPath<TCell>(IWorld<TCell> world, CellCoord start, CellCoord goal,
        Func<TCell, bool> walkable, int maxNodes = 4000) where TCell : unmanaged
    {
        if (start == goal) return new List<CellCoord> { start };
        if (!walkable(world.GetCell(goal))) return null;

        var open = new PriorityQueue<CellCoord, int>();
        var g = new Dictionary<CellCoord, int> { [start] = 0 };
        var came = new Dictionary<CellCoord, CellCoord>();
        open.Enqueue(start, Heuristic(start, goal));
        int expanded = 0;

        while (open.TryDequeue(out var cur, out _))
        {
            if (cur == goal)
            {
                var path = new List<CellCoord> { cur };
                while (came.TryGetValue(cur, out var prev)) { path.Add(prev); cur = prev; }
                path.Reverse();
                return path;
            }
            if (++expanded > maxNodes) return null;

            int gc = g[cur];
            foreach (var (dx, dy) in Dirs)
            {
                var n = new CellCoord(cur.X + dx, cur.Y + dy, cur.Z);
                if (!walkable(world.GetCell(n))) continue;
                bool diagonal = dx != 0 && dy != 0;
                if (diagonal &&
                    (!walkable(world.GetCell(new CellCoord(cur.X + dx, cur.Y, cur.Z))) ||
                     !walkable(world.GetCell(new CellCoord(cur.X, cur.Y + dy, cur.Z))))) continue;

                int cost = gc + (diagonal ? 14 : 10);
                if (g.TryGetValue(n, out int old) && old <= cost) continue;
                g[n] = cost;
                came[n] = cur;
                open.Enqueue(n, cost + Heuristic(n, goal));
            }
        }
        return null;
    }

    private static int Heuristic(CellCoord a, CellCoord b)
    {
        int dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);
        return 10 * (dx + dy) - 6 * Math.Min(dx, dy);   // distancia octil
    }
}
