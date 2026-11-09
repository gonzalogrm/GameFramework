using GF.Core;
using GF.World;

namespace MiniCraft;

/// <summary>
/// Cuevas: TÚNELES sinuosos (dos ruidos 3D que se anulan a la vez) y CAVERNAS aplastadas (un ruido 3D con el eje vertical comprimido). El ruido se evalúa en una
/// malla de 4 bloques alineada con el mundo y se interpola, así que los chunks vecinos coinciden sin costuras y es barato; además se descartan de golpe los
/// bloques de 4x4x4 donde el ruido no puede abrir nada. El techo es de solo 5 bloques bajo la superficie y en las BOCAS (zonas dispersas de un ruido 2D de baja
/// frecuencia) las cuevas llegan hasta la superficie, con aberturas pequeñas. Nunca se excava bajo ciudades, poblados ni ruinas (CaveCarver.Planner, lo fija
/// DecorationStage). Dentro hay estalagmitas y estalactitas de la roca del planeta, cristales luminosos (CaveCrystals) y hongos (CaveFungi).
/// SurfaceOpen (para no plantar sobre una entrada) usa EXACTAMENTE la misma decisión que el excavado.
/// </summary>
public static class CaveCarver
{
    private const int S = 4;           // separación de la malla de ruido
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

    /// <summary>Valores de los tres ruidos en un nodo de la malla (ix, iy, iz = coordenadas del mundo / 4).</summary>
    private static void Node(INoise n, Params p, int ix, int iy, int iz, out float a, out float b, out float c)
    {
        float wx = ix * S, wy = iy * S, wz = iz * S;
        a = n.Sample(wx * p.Tf + 11.3f, wy * p.Tf + 27.9f, wz * p.Tf + 5.1f);
        b = n.Sample(wx * p.Tf + 97.1f, wy * p.Tf + 61.7f, wz * p.Tf + 83.3f);
        c = n.Sample(wx * p.Cf + 53.7f, wy * p.Cf * p.Squash + 17.2f, wz * p.Cf + 71.9f);
    }

    /// <summary>Interpolación trilineal de una malla guardada con pasos +1 (x), +nx (z) y +sy (y) desde el nodo i.</summary>
    private static float Tri(float[] f, int i, int nx, int sy, float fx, float fy, float fz)
    {
        float x00 = f[i] + (f[i + 1] - f[i]) * fx;
        float x10 = f[i + sy] + (f[i + sy + 1] - f[i + sy]) * fx;
        float x01 = f[i + nx] + (f[i + nx + 1] - f[i + nx]) * fx;
        float x11 = f[i + sy + nx] + (f[i + sy + nx + 1] - f[i + sy + nx]) * fx;
        float y0 = x00 + (x01 - x00) * fz, y1 = x10 + (x11 - x10) * fz;
        return y0 + (y1 - y0) * fy;
    }

    /// <summary>La decisión de excavar UNA celda: la usan el excavado del chunk y SurfaceOpen, con la misma aritmética.</summary>
    private static bool Decide(Params p, float[] ta, float[] tb, float[] tc, int i, int nx, int sy, float fx, float fy, float fz,
        int wy, int h, int sea, float mouth)
    {
        if (wy < FloorY || h <= sea + 2) return false;
        int roof = h - RoofDepth + (int)(6f * mouth);   // en las bocas, hasta la superficie
        if (wy >= roof) return false;
        float f = Math.Clamp((roof - wy) / (8f - 6f * mouth), 0f, 1f);   // se cierra hacia el techo
        if (f <= 0f) return false;
        // Aberturas pequeñas: justo bajo la superficie solo abren los picos más fuertes del ruido (túneles más finos y cavernas más exigentes).
        float surf = wy >= h - 1 ? 0.2f : wy >= h - 3 ? 0.08f : 0f;
        float tw = p.TunnelW * f * (1f - surf * 3f);
        if (tw > 0f && MathF.Abs(Tri(ta, i, nx, sy, fx, fy, fz)) < tw && MathF.Abs(Tri(tb, i, nx, sy, fx, fy, fz)) < tw) return true;
        return Tri(tc, i, nx, sy, fx, fy, fz) > p.CThr - 0.12f * mouth + (1f - f) * 0.35f + surf;
    }

    [ThreadStatic] private static float[]? t_a, t_b, t_c;

    /// <summary>¿Hay una entrada de cueva justo en la superficie de esta columna? (para no plantar nada encima). Los llamadores ya descartan las zonas de ciudad.</summary>
    public static bool SurfaceOpen(INoise noise, int seed, float density, int wx, int wz, int ground, int sea)
    {
        if (density <= 0f || ground <= sea + 2) return false;
        var p = GetParams(seed, density);
        float mouth = Mouth(noise, p, wx, wz);
        return mouth > 0f && (OpenAt(noise, p, wx, ground, wz, ground, sea, mouth) || OpenAt(noise, p, wx, ground - 1, wz, ground, sea, mouth));
    }

    private static bool OpenAt(INoise noise, Params p, int wx, int wy, int wz, int h, int sea, float mouth)
    {
        int ix = IntMath.FloorDiv(wx, S), iy = IntMath.FloorDiv(wy, S), iz = IntMath.FloorDiv(wz, S);
        var ta = t_a ??= new float[8]; var tb = t_b ??= new float[8]; var tc = t_c ??= new float[8];
        for (int dy = 0; dy < 2; dy++)
        for (int dz = 0; dz < 2; dz++)
        for (int dx = 0; dx < 2; dx++)
        {
            int i = dy * 4 + dz * 2 + dx;   // misma disposición que la malla del chunk con nx = 2, sy = 4
            Node(noise, p, ix + dx, iy + dy, iz + dz, out ta[i], out tb[i], out tc[i]);
        }
        return Decide(p, ta, tb, tc, 0, 2, 4, (wx - ix * S) / (float)S, (wy - iy * S) / (float)S, (wz - iz * S) / (float)S, wy, h, sea, mouth);
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
        int nx = sx / S + 1, ny = sy / S + 1, nz = sz / S + 1, strideY = nx * nz;
        int ox4 = o.X / S, oy4 = o.Y / S, oz4 = o.Z / S;   // los orígenes de chunk son múltiplos de 4
        var ta = new float[nx * ny * nz];
        var tb = new float[ta.Length];
        var tc = new float[ta.Length];
        for (int iy = 0; iy < ny; iy++)
        for (int iz = 0; iz < nz; iz++)
        for (int ix = 0; ix < nx; ix++)
            Node(noise, p, ox4 + ix, oy4 + iy, oz4 + iz, out ta[iy * strideY + iz * nx + ix], out tb[iy * strideY + iz * nx + ix], out tc[iy * strideY + iz * nx + ix]);

        var cells = chunk.Cells;
        var carved = new bool[cells.Length];
        var mouthCol = new float[sx * sz];
        Array.Fill(mouthCol, float.NaN);
        var reserved = new sbyte[sx * sz];   // 0 sin mirar, 1 reservada, -1 libre
        float twMax = p.TunnelW, cavMin = p.CThr - 0.12f;
        bool any = false;
        int minY = int.MaxValue, maxY = int.MinValue;

        for (int iy = 0; iy < ny - 1; iy++)
        {
            int wy0 = o.Y + iy * S;
            if (wy0 > maxH + 1) break;
            if (wy0 + S - 1 < FloorY) continue;
            for (int iz = 0; iz < nz - 1; iz++)
            for (int ix = 0; ix < nx - 1; ix++)
            {
                int i = iy * strideY + iz * nx + ix;
                // ¿Puede el ruido abrir algo en este bloque de 4x4x4? Las interpolaciones nunca salen del rango de sus 8 esquinas.
                float amin = float.MaxValue, amax = float.MinValue, bmin = float.MaxValue, bmax = float.MinValue, cmax = float.MinValue;
                for (int k = 0; k < 8; k++)
                {
                    int j = i + (k & 1) + ((k >> 1) & 1) * nx + ((k >> 2) & 1) * strideY;
                    float a = ta[j], b = tb[j], c = tc[j];
                    if (a < amin) amin = a; if (a > amax) amax = a;
                    if (b < bmin) bmin = b; if (b > bmax) bmax = b;
                    if (c > cmax) cmax = c;
                }
                bool tunnel = amin < twMax && amax > -twMax && bmin < twMax && bmax > -twMax;
                if (!tunnel && cmax <= cavMin) continue;

                for (int ly = 0; ly < S; ly++)
                {
                    int y = iy * S + ly, wy = o.Y + y;
                    if (y >= sy) break;
                    float fy = ly / (float)S;
                    for (int lz = 0; lz < S; lz++)
                    {
                        int z = iz * S + lz;
                        float fz = lz / (float)S;
                        for (int lx = 0; lx < S; lx++)
                        {
                            int x = ix * S + lx, col = x + z * sx;
                            int h = heights[col];
                            if (h <= sea + 2 || wy >= h + 1) continue;       // bajo el mar, o por encima de lo que se puede excavar
                            int idx = (y * sz + z) * sx + x;
                            ushort id = cells[idx];
                            if (id == Blocks.Air || id == Blocks.Water) continue;

                            if (float.IsNaN(mouthCol[col])) mouthCol[col] = Mouth(noise, p, o.X + x, o.Z + z);
                            if (!Decide(p, ta, tb, tc, i, nx, strideY, lx / (float)S, fy, fz, wy, h, sea, mouthCol[col])) continue;

                            if (reserved[col] == 0) reserved[col] = (sbyte)(Planner != null && Planner.IsReserved(o.X + x, o.Z + z) ? 1 : -1);
                            if (reserved[col] > 0) continue;               // ni bajo ciudades ni ruinas

                            cells[idx] = Blocks.Air;
                            carved[idx] = true;
                            any = true;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                    }
                }
            }
        }
        if (!any) return;
        RemoveFloating(cells, carved, sx, sy, sz, Math.Max(1, minY - 1), Math.Min(sy - 2, maxY + 1));
        Decorate(chunk, carved, seed, crystalMult, fungusMult, deep);
    }

    private static bool IsSolid(ushort id) => id != Blocks.Air && id != Blocks.Water;

    /// <summary>
    /// Quita los bloques que quedan colgando tras excavar: sin nada sólido debajo ni encima y con a lo sumo un vecino lateral (se repite hasta 3 veces para las
    /// cadenas). Solo mira celdas interiores del chunk y solo en la franja de alturas donde se excavó, así que es barato y determinista. El terreno natural no tiene
    /// voladizos: esto solo toca lo que ha dejado la excavación.
    /// </summary>
    private static void RemoveFloating(ushort[] cells, bool[] carved, int sx, int sy, int sz, int yLo, int yHi)
    {
        int stride = sx * sz;
        for (int pass = 0; pass < 3; pass++)
        {
            bool changed = false;
            for (int y = yLo; y <= yHi; y++)
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

        float crystalRate = 0.006f * crystalMult, fungusRate = 0.012f * fungusMult;

        for (int z = 0; z < sz; z++)
        for (int x = 0; x < sx; x++)
        for (int y = 1; y < sy - 1; y++)
        {
            int idx = (y * sz + z) * sx + x;
            if (!carved[idx] || cells[idx] != Blocks.Air) continue;
            bool floor = IsSolid(cells[idx - stride]), ceiling = IsSolid(cells[idx + stride]);
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
