using GF.Core;

namespace GF.World.Voxel;

/// <summary>
/// A* sobre el terreno voxel para un cuerpo de 'bodyHeight' celdas de alto. Un nodo es una celda donde se puede estar de pie (suelo sólido
/// debajo, hueco para el cuerpo). Desde cada nodo se puede andar a las 8 direcciones horizontales, subir un escalón de 1 bloque y bajar
/// hasta 'maxDrop' bloques. Lo no cargado cuenta como sólido (no se planea a través de chunks que no existen).
/// Limitado a 'maxNodes' expansiones: si no llega a la meta, devuelve el camino hasta el nodo explorado MÁS CERCANO a ella, que es lo
/// que hace falta para perseguir (se vuelve a planificar a menudo). Solo hilo principal.
/// </summary>
public static class VoxelPathfinder
{
    private static readonly (int X, int Z)[] Dirs = { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) };

    /// <returns>Celdas desde 'start' (incluida) hasta la meta o el nodo más cercano; null si no hay ni un paso posible.</returns>
    public static List<CellCoord>? FindPath(IWorld<ushort> world, BlockRegistry blocks, CellCoord start, CellCoord goal,
        int maxNodes = 400, int maxDrop = 3, int bodyHeight = 2)
    {
        var table = blocks.GetTable();
        var shape = world.Shape;

        bool Solid(int x, int y, int z)
        {
            var c = new CellCoord(x, y, z);
            return world.GetChunk(shape.ToChunk(c)) == null || table.Defs[world.GetCell(c)].Solid;
        }

        bool CanStand(int x, int y, int z)
        {
            if (!Solid(x, y - 1, z)) return false;
            for (int h = 0; h < bodyHeight; h++) if (Solid(x, y + h, z)) return false;
            return true;
        }

        float Heuristic(CellCoord c)
        {
            int dx = Math.Abs(c.X - goal.X), dz = Math.Abs(c.Z - goal.Z);
            return (dx + dz) + (1.414f - 2f) * Math.Min(dx, dz) + 0.5f * Math.Abs(c.Y - goal.Y);
        }

        var open = new PriorityQueue<CellCoord, float>();
        var g = new Dictionary<CellCoord, float> { [start] = 0f };
        var came = new Dictionary<CellCoord, CellCoord>();
        var closed = new HashSet<CellCoord>();
        open.Enqueue(start, Heuristic(start));

        var best = start;
        float bestH = Heuristic(start);
        int expanded = 0;

        while (open.TryDequeue(out var cur, out _))
        {
            if (!closed.Add(cur)) continue;
            if (cur.X == goal.X && cur.Z == goal.Z && Math.Abs(cur.Y - goal.Y) <= 1) return Build(came, cur, start);

            float h = Heuristic(cur);
            if (h < bestH) { bestH = h; best = cur; }
            if (++expanded > maxNodes) break;

            float gc = g[cur];
            foreach (var (dx, dz) in Dirs)
            {
                bool diagonal = dx != 0 && dz != 0;
                int nx = cur.X + dx, nz = cur.Z + dz;

                // Las diagonales solo si las dos columnas laterales están libres (sin cortar esquinas).
                if (diagonal && (Solid(cur.X + dx, cur.Y, cur.Z) || Solid(cur.X + dx, cur.Y + 1, cur.Z) ||
                                 Solid(cur.X, cur.Y, cur.Z + dz) || Solid(cur.X, cur.Y + 1, cur.Z + dz))) continue;

                for (int dy = 1; dy >= -maxDrop; dy--)   // como mucho una de estas alturas tiene suelo
                {
                    int ny = cur.Y + dy;
                    if (!CanStand(nx, ny, nz)) continue;
                    if (diagonal && dy != 0) break;
                    if (dy > 0 && Solid(cur.X, cur.Y + bodyHeight, cur.Z)) break;   // sin hueco encima para subir el escalón
                    bool clear = true;                                                // al bajar, la columna destino libre hasta la altura actual
                    for (int k = ny + bodyHeight; k < cur.Y + bodyHeight && clear; k++) clear = !Solid(nx, k, nz);
                    if (!clear) break;

                    var next = new CellCoord(nx, ny, nz);
                    if (closed.Contains(next)) break;
                    float cost = gc + (diagonal ? 1.414f : 1f) + (dy != 0 ? 0.6f : 0f);
                    if (g.TryGetValue(next, out float old) && old <= cost) break;
                    g[next] = cost;
                    came[next] = cur;
                    open.Enqueue(next, cost + Heuristic(next));
                    break;
                }
            }
        }

        return best == start ? null : Build(came, best, start);
    }

    private static List<CellCoord> Build(Dictionary<CellCoord, CellCoord> came, CellCoord end, CellCoord start)
    {
        var path = new List<CellCoord> { end };
        for (var c = end; c != start && came.TryGetValue(c, out var prev); c = prev) path.Add(prev);
        path.Reverse();
        return path;
    }
}
