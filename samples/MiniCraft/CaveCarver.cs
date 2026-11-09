using GF.Core;
using GF.World;

namespace MiniCraft;

/// <summary>
/// Cuevas: TÚNELES sinuosos (dos ruidos 3D que se anulan a la vez) y CAVERNAS aplastadas (un ruido 3D con el eje vertical comprimido). El techo es de solo
/// 5 bloques bajo la superficie (el mundo es bajo) y en las BOCAS (zonas dispersas de un ruido 2D de baja frecuencia) las cuevas llegan hasta la superficie:
/// ahí hay entradas visibles. Nunca se excava bajo ciudades, poblados ni ruinas (CaveCarver.Planner, lo fija DecorationStage). Dentro hay estalagmitas y
/// estalactitas de la roca del planeta, cristales luminosos (CaveCrystals) y hongos (CaveFungi): de noche y bajo tierra son la única luz.
/// La decisión de cada celda depende solo de (semilla, x, y, z, altura de la columna): TreeStage y DecorationStage la consultan (SurfaceOpen) para no plantar
/// sobre una entrada.
/// </summary>
public static class CaveCarver
{
    private const int FloorY = 2;      // mínimo de roca en el fondo del mundo
    private const int RoofDepth = 5;   // roca sobre una cueva fuera de las bocas

    /// <summary>Planificador de ciudades y ruinas: sus zonas no se excavan. Lo asigna DecorationStage al crearse.</summary>
    public static CityPlanner? Planner { get; set; }

    private sealed class Params
    {
        public readonly int Seed; public readonly float Density;
        public readonly float TunnelW, Tf, Cf, CThr, Squash, Mf, MouthThr;

        public Params(int seed, float density)
        {
            Seed = seed; Density = density;
            var rng = new SplitMixRandom(Hashing.Mix((ulong)(uint)seed ^ 0xCA7E5UL));
            TunnelW = (0.05f + rng.NextFloat() * 0.04f) * MathF.Sqrt(density);
            Tf = 1f / (22f + rng.NextFloat() * 16f);
            Cf = 1f / (40f + rng.NextFloat() * 30f);
            CThr = Math.Clamp(0.52f + rng.NextFloat() * 0.1f - 0.1f * (density - 1f), 0.3f, 0.95f);
            Squash = 1.6f + rng.NextFloat();
            Mf = 1f / (60f + rng.NextFloat() * 60f);
            MouthThr = 0.3f + rng.NextFloat() * 0.1f;   // bocas escasas
        }
    }

    private static volatile Params? _cache;

    private static Params GetParams(int seed, float density)
    {
        var p = _cache;
        if (p != null && p.Seed == seed && p.Density == density) return p;
        return _cache = new Params(seed, density);
    }

    private static float Mouth(INoise n, Params p, int wx, int wz)
    {
        float t = Math.Clamp((n.Sample(wx * p.Mf + 31.7f, wz * p.Mf + 12.9f) - p.MouthThr) / 0.15f, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static bool Carved(INoise n, Params p, int wx, int wy, int wz, int h, int sea, float mouth)
    {
        if (wy < FloorY || h <= sea + 2) return false;
        int roof = h - RoofDepth + (int)(6f * mouth);   // en las bocas, hasta la superficie
        if (wy >= roof) return false;
        float f = Math.Clamp((roof - wy) / (8f - 6f * mouth), 0f, 1f);   // se cierra hacia el techo
        if (f <= 0f) return false;
        // Aberturas pequeñas: justo bajo la superficie solo abren los picos más fuertes del ruido (túneles más finos y cavernas más exigentes).
        float surf = wy >= h - 1 ? 0.2f : wy >= h - 3 ? 0.08f : 0f;
        float tw = p.TunnelW * f * (1f - surf * 3f);
        if (tw > 0f && MathF.Abs(n.Sample(wx * p.Tf + 11.3f, wy * p.Tf + 27.9f, wz * p.Tf + 5.1f)) < tw &&
            MathF.Abs(n.Sample(wx * p.Tf + 97.1f, wy * p.Tf + 61.7f, wz * p.Tf + 83.3f)) < tw) return true;
        return n.Sample(wx * p.Cf + 53.7f, wy * p.Cf * p.Squash + 17.2f, wz * p.Cf + 71.9f) > p.CThr - 0.12f * mouth + (1f - f) * 0.35f + surf;
    }

    /// <summary>¿Hay una entrada de cueva justo en la superficie de esta columna? (para no plantar nada encima)</summary>
    public static bool SurfaceOpen(INoise noise, int seed, float density, int wx, int wz, int ground, int sea)
    {
        if (density <= 0f || ground <= sea + 2) return false;
        if (Planner != null && Planner.IsReserved(wx, wz)) return false;
        var p = GetParams(seed, density);
        float mouth = Mouth(noise, p, wx, wz);
        return mouth > 0f && (Carved(noise, p, wx, ground, wz, ground, sea, mouth) || Carved(noise, p, wx, ground - 1, wz, ground, sea, mouth));
    }

    public static void Carve(Chunk<ushort> chunk, INoise noise, int seed, int[] heights, int sea, float density, float crystalMult, float fungusMult, ushort deep)
    {
        if (density <= 0f) return;
        var shape = chunk.Shape;
        int sx = shape.SizeX, sy = shape.SizeY, sz = shape.SizeZ;
        var o = shape.Origin(chunk.Coord);
        int maxH = int.MinValue;
        foreach (int h in heights) if (h > maxH) maxH = h;
        if (o.Y + FloorY > maxH + 1 && o.Y > 0) return;   // por encima de todo el terreno

        var p = GetParams(seed, density);
        var cells = chunk.Cells;
        var carved = new bool[cells.Length];
        bool any = false;
        for (int z = 0; z < sz; z++)
        for (int x = 0; x < sx; x++)
        {
            int h = heights[x + z * sx];
            if (h <= sea + 2) continue;                    // bajo el mar: sin cuevas
            int wx = o.X + x, wz = o.Z + z;
            if (Planner != null && Planner.IsReserved(wx, wz)) continue;   // ni bajo ciudades ni ruinas

            float mouth = Mouth(noise, p, wx, wz);
            int roof = h - RoofDepth + (int)(6f * mouth);
            int yLo = Math.Max(FloorY - o.Y, 0), yHi = Math.Min(sy - 1, roof - 1 - o.Y);
            for (int y = yLo; y <= yHi; y++)
            {
                int idx = (y * sz + z) * sx + x;
                ushort id = cells[idx];
                if (id == Blocks.Air || id == Blocks.Water) continue;
                if (!Carved(noise, p, wx, o.Y + y, wz, h, sea, mouth)) continue;
                cells[idx] = Blocks.Air;
                carved[idx] = true;
                any = true;
            }
        }
        if (!any) return;
        RemoveFloating(cells, carved, sx, sy, sz);
        Decorate(chunk, carved, seed, crystalMult, fungusMult, deep);
    }

    private static bool IsSolid(ushort id) => id != Blocks.Air && id != Blocks.Water;

    /// <summary>
    /// Quita los bloques que quedan colgando tras excavar: sin nada sólido debajo ni encima y con a lo sumo un vecino lateral (se repite hasta 3 veces para las
    /// cadenas). Solo mira celdas interiores del chunk, así que es determinista y no depende de los vecinos. El terreno natural no tiene voladizos: esto solo toca
    /// lo que ha dejado la excavación.
    /// </summary>
    private static void RemoveFloating(ushort[] cells, bool[] carved, int sx, int sy, int sz)
    {
        int stride = sx * sz;
        for (int pass = 0; pass < 3; pass++)
        {
            bool changed = false;
            for (int y = 1; y < sy - 1; y++)
            for (int z = 1; z < sz - 1; z++)
            for (int x = 1; x < sx - 1; x++)
            {
                int idx = (y * sz + z) * sx + x;
                if (!IsSolid(cells[idx])) continue;
                if (IsSolid(cells[idx - stride]) || IsSolid(cells[idx + stride])) continue;
                int sides = (IsSolid(cells[idx - 1]) ? 1 : 0) + (IsSolid(cells[idx + 1]) ? 1 : 0) +
                            (IsSolid(cells[idx - sx]) ? 1 : 0) + (IsSolid(cells[idx + sx]) ? 1 : 0);
                if (sides > 1) continue;
                cells[idx] = Blocks.Air;
                carved[idx] = true;
                changed = true;
            }
            if (!changed) break;
        }
    }

    /// <summary>Estalagmitas, estalactitas, cristales y hongos dentro de lo excavado. Solo toca celdas interiores del chunk en vertical: es determinista y sin costuras.</summary>
    private static void Decorate(Chunk<ushort> chunk, bool[] carved, int seed, float crystalMult, float fungusMult, ushort deep)
    {
        var shape = chunk.Shape;
        int sx = shape.SizeX, sy = shape.SizeY, sz = shape.SizeZ, stride = sx * sz;
        var o = shape.Origin(chunk.Coord);
        var cells = chunk.Cells;

        bool Solid(ushort id) => id != Blocks.Air && id != Blocks.Water;
        float crystalRate = 0.006f * crystalMult, fungusRate = 0.012f * fungusMult;

        for (int z = 0; z < sz; z++)
        for (int x = 0; x < sx; x++)
        for (int y = 1; y < sy - 1; y++)
        {
            int idx = (y * sz + z) * sx + x;
            if (!carved[idx] || cells[idx] != Blocks.Air) continue;
            bool floor = Solid(cells[idx - stride]), ceiling = Solid(cells[idx + stride]);
            if (!floor && !ceiling) continue;

            int wx = o.X + x, wy = o.Y + y, wz = o.Z + z;
            if (floor)
            {
                ulong h = Hashing.Combine(seed, wx, wy, wz);
                float r = (h >> 40) * (1f / (1 << 24));
                if (r < crystalRate) Stack(cells, idx, stride, sy - 1 - y, 1 + (int)((h >> 8) % 3UL), Blocks.Crystal);
                else if (r < crystalRate + fungusRate)
                {
                    cells[idx] = Blocks.Fungus;
                    if (((h >> 12) & 1) == 0 && y + 1 < sy - 1 && cells[idx + stride] == Blocks.Air) cells[idx + stride] = Blocks.SporeCap;
                }
                else if (r < crystalRate + fungusRate + 0.02f) Stack(cells, idx, stride, sy - 1 - y, 1 + (int)((h >> 8) % 2UL), deep);
            }
            if (ceiling && cells[idx] == Blocks.Air)
            {
                ulong h = Hashing.Combine(seed ^ 0x5A5A, wx, wy, wz);
                float r = (h >> 40) * (1f / (1 << 24));
                if (r < 0.02f) Stack(cells, idx, -stride, y, 1 + (int)((h >> 8) % 3UL), deep);
                else if (r < 0.02f + 0.003f * crystalMult) Stack(cells, idx, -stride, y, 1 + (int)((h >> 8) % 2UL), Blocks.Crystal);
            }
        }
    }

    /// <summary>Escribe 'count' bloques seguidos desde idx en la dirección 'step' mientras haya aire y quepan en el chunk (room = celdas disponibles).</summary>
    private static void Stack(ushort[] cells, int idx, int step, int room, int count, ushort id)
    {
        for (int k = 0; k < count && k <= room; k++)
        {
            int i = idx + k * step;
            if (i < 0 || i >= cells.Length || cells[i] != Blocks.Air) break;
            cells[i] = id;
        }
    }
}
