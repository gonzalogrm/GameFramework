using GF.Core;
using GF.World;

namespace MiniCraft;

/// <summary>
/// Cuevas: TÚNELES sinuosos (dos ruidos 3D que se anulan a la vez) y CAVERNAS aplastadas (un ruido 3D con el eje vertical comprimido). El ruido se evalúa en una
/// malla de 4 bloques alineada con el mundo y se interpola, así que los chunks vecinos coinciden sin costuras y es barato. Solo se excava a más de 10 bloques
/// bajo la superficie (y se estrecha al acercarse a ese techo): así no rompen ciudades ni ruinas, que se nivelan después. Tamaño, frecuencia y anchura salen
/// de la semilla del planeta. Dentro hay estalagmitas y estalactitas de la roca del planeta, cristales luminosos (CrystalDensityMultiplier) y hongos
/// (FungusDensityMultiplier): de noche y bajo tierra son la única luz.
/// </summary>
public static class CaveCarver
{
    private const int S = 4;           // separación de la malla de ruido
    private const int RoofDepth = 10;  // mínimo de roca sobre una cueva

    public static void Carve(Chunk<ushort> chunk, INoise noise, int seed, int[] heights, int sea, float density, float crystalMult, float fungusMult, ushort deep)
    {
        if (density <= 0f) return;
        var shape = chunk.Shape;
        int sx = shape.SizeX, sy = shape.SizeY, sz = shape.SizeZ;
        var o = shape.Origin(chunk.Coord);
        int maxH = int.MinValue;
        foreach (int h in heights) if (h > maxH) maxH = h;
        if (o.Y + 3 > maxH - RoofDepth) return;   // sin roca suficiente sobre este chunk

        var rng = new SplitMixRandom(Hashing.Mix((ulong)(uint)seed ^ 0xCA7E5UL));
        float tunnelWidth = (0.05f + rng.NextFloat() * 0.04f) * MathF.Sqrt(density);
        float tf = 1f / (22f + rng.NextFloat() * 16f), cf = 1f / (40f + rng.NextFloat() * 30f);
        float cavernThr = Math.Clamp(0.52f + rng.NextFloat() * 0.1f - 0.1f * (density - 1f), 0.3f, 0.95f);
        float squash = 1.6f + rng.NextFloat();

        int nx = sx / S + 1, ny = sy / S + 1, nz = sz / S + 1;
        var ta = new float[nx * ny * nz];
        var tb = new float[ta.Length];
        var tc = new float[ta.Length];
        for (int iy = 0; iy < ny; iy++)
        for (int iz = 0; iz < nz; iz++)
        for (int ix = 0; ix < nx; ix++)
        {
            float wx = o.X + ix * S, wy = o.Y + iy * S, wz = o.Z + iz * S;
            int i = (iy * nz + iz) * nx + ix;
            ta[i] = noise.Sample(wx * tf + 11.3f, wy * tf + 27.9f, wz * tf + 5.1f);
            tb[i] = noise.Sample(wx * tf + 97.1f, wy * tf + 61.7f, wz * tf + 83.3f);
            tc[i] = noise.Sample(wx * cf + 53.7f, wy * cf * squash + 17.2f, wz * cf + 71.9f);
        }

        int stepY = nx * nz;
        float Tri(float[] f, int ix, int iy, int iz, float fx, float fy, float fz)
        {
            int i = (iy * nz + iz) * nx + ix;
            float x00 = f[i] + (f[i + 1] - f[i]) * fx, x10 = f[i + nx] + (f[i + nx + 1] - f[i + nx]) * fx;
            float x01 = f[i + stepY] + (f[i + stepY + 1] - f[i + stepY]) * fx;
            float x11 = f[i + stepY + nx] + (f[i + stepY + nx + 1] - f[i + stepY + nx]) * fx;
            return (x00 + (x10 - x00) * fz) + ((x01 + (x11 - x01) * fz) - (x00 + (x10 - x00) * fz)) * fy;
        }

        var cells = chunk.Cells;
        var carved = new bool[cells.Length];
        bool any = false;
        for (int y = 0; y < sy; y++)
        {
            int wy = o.Y + y;
            if (wy < 3) continue;
            int iy = y / S;
            float fy = (y % S) / (float)S;
            for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
            {
                int h = heights[x + z * sx];
                if (h <= sea + 2) continue;                    // bajo el mar: sin cuevas
                int roof = h - RoofDepth;
                if (wy >= roof) continue;
                int idx = (y * sz + z) * sx + x;
                ushort id = cells[idx];
                if (id == Blocks.Air || id == Blocks.Water) continue;

                float f = Math.Clamp((roof - wy) / 8f, 0f, 1f);   // se cierra hacia el techo
                int ix = x / S, iz = z / S;
                float fx = (x % S) / (float)S, fz = (z % S) / (float)S;
                bool open = false;
                if (f > 0f)
                {
                    float n1 = Tri(ta, ix, iy, iz, fx, fy, fz);
                    if (MathF.Abs(n1) < tunnelWidth * f)
                        open = MathF.Abs(Tri(tb, ix, iy, iz, fx, fy, fz)) < tunnelWidth * f;
                    if (!open) open = Tri(tc, ix, iy, iz, fx, fy, fz) > cavernThr + (1f - f) * 0.35f;
                }
                if (!open) continue;
                cells[idx] = Blocks.Air;
                carved[idx] = true;
                any = true;
            }
        }
        if (!any) return;
        Decorate(chunk, carved, seed, crystalMult, fungusMult, deep);
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
