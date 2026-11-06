using GF.Core;
using GF.World;
using GF.World.Map;

namespace MiniCraft;

/// <summary>
/// Lugares misteriosos repartidos por el mundo (uno cada ~380 bloques de media, según WorldSettings.MysteryDensity), solo en terreno llano y fuera de ciudades y ruinas:
///  - SUMIDERO: un pozo circular de 3 a 6 de radio y de 10 a 18 de fondo, con un collar de paneles de aleación, hongos luminosos en las paredes y cristales en el fondo.
///  - CÍRCULO DE MONOLITOS: de 6 a 10 menhires de la roca del planeta con una punta de cristal (alguno caído) alrededor de un altar de paneles con un cristal.
///  - PILONO: una torre de metal oxidado de 14 a 22 de alto sobre una plataforma, con anillos de paneles, puntales y una corona de cristal.
///  - ARBOLEDA DE CRISTAL: de 6 a 12 agujas de cristal de hasta 12 de alto.
/// Todo se calcula por (semilla, celda de 160 bloques), así que no hay costuras entre chunks. Se ejecuta al final de DecorationStage: despeja la vegetación de su zona.
/// </summary>
internal static class MysterySites
{
    private const int Cell = 160, Reach = 14, Margin = 20;

    private sealed class Builder
    {
        public readonly ushort[] Cells;
        public readonly int Ox, Oy, Oz, Sx, Sy, Sz;
        private readonly TerrainColumnCache _columns;

        public Builder(Chunk<ushort> chunk, CellCoord o, TerrainColumnCache columns)
        {
            Cells = chunk.Cells;
            var s = chunk.Shape;
            Sx = s.SizeX; Sy = s.SizeY; Sz = s.SizeZ;
            Ox = o.X; Oy = o.Y; Oz = o.Z;
            _columns = columns;
        }

        public int Height(int wx, int wz) => _columns.HeightAt(wx, wz);
        public bool InColumn(int wx, int wz) => (uint)(wx - Ox) < (uint)Sx && (uint)(wz - Oz) < (uint)Sz;

        private int Index(int wx, int wy, int wz)
        {
            int x = wx - Ox, y = wy - Oy, z = wz - Oz;
            return (uint)x < (uint)Sx && (uint)y < (uint)Sy && (uint)z < (uint)Sz ? (y * Sz + z) * Sx + x : -1;
        }

        /// <summary>Escribe un bloque (sustituye lo que haya, salvo agua si no se fuerza).</summary>
        public void Set(int wx, int wy, int wz, ushort id)
        {
            int i = Index(wx, wy, wz);
            if (i >= 0) Cells[i] = id;
        }

        /// <summary>Sustituye solo un bloque sólido (paredes de un pozo): no toca aire ni agua.</summary>
        public void Replace(int wx, int wy, int wz, ushort id)
        {
            int i = Index(wx, wy, wz);
            if (i >= 0 && Cells[i] != Blocks.Air && Cells[i] != Blocks.Water) Cells[i] = id;
        }

        /// <summary>Excava (aire) salvo el agua.</summary>
        public void Carve(int wx, int wy, int wz)
        {
            int i = Index(wx, wy, wz);
            if (i >= 0 && Cells[i] != Blocks.Water) Cells[i] = Blocks.Air;
        }

        /// <summary>Quita la vegetación (todo lo no vacío salvo agua) entre dos alturas de una columna.</summary>
        public void Clear(int wx, int wz, int y0, int y1)
        {
            if (!InColumn(wx, wz)) return;
            for (int y = Math.Max(y0, Oy); y <= Math.Min(y1, Oy + Sy - 1); y++) Carve(wx, y, wz);
        }

        public void ClearDisc(int cx, int cz, int radius, int height)
        {
            for (int wz = Math.Max(cz - radius, Oz); wz <= Math.Min(cz + radius, Oz + Sz - 1); wz++)
            for (int wx = Math.Max(cx - radius, Ox); wx <= Math.Min(cx + radius, Ox + Sx - 1); wx++)
                if ((wx - cx) * (wx - cx) + (wz - cz) * (wz - cz) <= radius * radius)
                {
                    int h = Height(wx, wz);
                    Clear(wx, wz, h + 1, h + height);
                }
        }
    }

    private static float Rand(int seed, int a, int b, int c) => (Hashing.Combine(seed, a, b, c) >> 40) * (1f / (1 << 24));

    public static void Apply(Chunk<ushort> chunk, GenerationContext ctx, MiniCraftClimate climate, WorldScale scale, TerrainColumnCache columns, CityPlanner planner)
    {
        float density = climate.Settings.MysteryDensity;
        if (density <= 0f) return;

        var shape = chunk.Shape;
        var o = shape.Origin(chunk.Coord);
        int sx = shape.SizeX, sz = shape.SizeZ;
        int cellsX = Math.Max(1, scale.WidthBlocks / Cell);
        int minGround = climate.SeaLevel + climate.Settings.BeachHeight + 2, worldHeight = climate.Settings.WorldHeight;
        Builder? b = null;

        for (int gz = IntMath.FloorDiv(o.Z - Reach, Cell); gz <= IntMath.FloorDiv(o.Z + sz + Reach, Cell); gz++)
        {
            if (gz < 0 || gz * Cell >= scale.HeightBlocks) continue;
            for (int gx = IntMath.FloorDiv(o.X - Reach, Cell); gx <= IntMath.FloorDiv(o.X + sx + Reach, Cell); gx++)
            {
                int gw = ((gx % cellsX) + cellsX) % cellsX;
                ulong h = Hashing.Combine(ctx.Seed, gw, 0x5173, gz);
                if ((h >> 40) * (1f / (1 << 24)) >= 0.22f * density) continue;

                int cx = gx * Cell + Margin + (int)((h >> 8) % (ulong)(Cell - 2 * Margin));
                int cz = gz * Cell + Margin + (int)((h >> 24) % (ulong)(Cell - 2 * Margin));
                if (cx + Reach < o.X || cx - Reach >= o.X + sx || cz + Reach < o.Z || cz - Reach >= o.Z + sz) continue;
                if ((uint)cz >= (uint)scale.HeightBlocks) continue;

                int g0 = columns.HeightAt(cx, cz);
                if (g0 <= minGround || g0 + 26 > worldHeight - 1) continue;
                if (planner.IsReserved(cx, cz) || !Flat(columns, planner, cx, cz, g0)) continue;

                b ??= new Builder(chunk, o, columns);
                var rng = new SplitMixRandom(h ^ 0x51735173UL);
                int pick = (int)((h >> 44) % 10UL);
                if (pick < 3) Sinkhole(b, cx, cz, g0, rng);
                else if (pick < 6) Monoliths(b, cx, cz, rng);
                else if (pick < 8) Pylon(b, cx, cz, g0, rng);
                else Grove(b, cx, cz, rng);
            }
        }
    }

    /// <summary>Terreno llano alrededor (4 puntos a 6 bloques con un desnivel máximo de 2) y sin ciudades ni ruinas cerca.</summary>
    private static bool Flat(TerrainColumnCache columns, CityPlanner planner, int cx, int cz, int g0)
    {
        foreach (var (dx, dz) in new[] { (6, 0), (-6, 0), (0, 6), (0, -6) })
            if (Math.Abs(columns.HeightAt(cx + dx, cz + dz) - g0) > 2 || planner.IsReserved(cx + dx, cz + dz)) return false;
        return true;
    }

    // ------------------------------------------------------------------ sumidero

    private static void Sinkhole(Builder b, int cx, int cz, int g0, SplitMixRandom rng)
    {
        int r = 3 + rng.Next(4);
        int depth = Math.Min(10 + rng.Next(9), g0 - 4);
        int reach = r + 3;
        for (int wz = Math.Max(cz - reach, b.Oz); wz <= Math.Min(cz + reach, b.Oz + b.Sz - 1); wz++)
        for (int wx = Math.Max(cx - reach, b.Ox); wx <= Math.Min(cx + reach, b.Ox + b.Sx - 1); wx++)
        {
            double d = Math.Sqrt((double)(wx - cx) * (wx - cx) + (double)(wz - cz) * (wz - cz));
            if (d > reach) continue;
            int hNat = b.Height(wx, wz);
            b.Clear(wx, wz, hNat + 1, hNat + 12);

            float reff = r + (Rand(0x51, wx, 0, wz) - 0.5f) * 1.6f;   // borde irregular
            if (d <= reff)
            {
                float t = (float)(d / reff);
                int floor = g0 - (int)(depth * (1f - t * t * 0.8f));   // cuenco: más hondo en el centro
                for (int y = floor; y <= hNat + 1; y++) b.Carve(wx, y, wz);
                if (d < 1.5) { for (int k = 0; k < 3; k++) b.Set(wx, floor + k, wz, Blocks.Crystal); }
                else if (Rand(0x52, wx, floor, wz) < 0.10f) b.Set(wx, floor, wz, Blocks.Crystal);
            }
            else if (d <= reff + 1.6)
            {
                for (int y = g0 - depth + 1; y <= hNat - 2; y++)   // paredes con hongos luminosos
                    if (Rand(0x53, wx, y, wz) < 0.12f) b.Replace(wx, y, wz, Blocks.Fungus);
                b.Set(wx, hNat, wz, Blocks.Panel);                  // collar de aleación
            }
        }
    }

    // ------------------------------------------------------------------ círculo de monolitos

    private static void Monoliths(Builder b, int cx, int cz, SplitMixRandom rng)
    {
        int n = 6 + rng.Next(5), radius = 5 + rng.Next(4);
        ushort[] mats = { Biomes.DeepStone, Blocks.Chalk, Blocks.Basalt, Blocks.Strata };
        ushort mat = mats[rng.Next(mats.Length)];
        b.ClearDisc(cx, cz, radius + 2, 10);

        for (int i = 0; i < n; i++)
        {
            double angle = 2 * Math.PI * i / n + (rng.NextFloat() - 0.5f) * 0.3;
            int px = cx + (int)Math.Round(radius * Math.Cos(angle)), pz = cz + (int)Math.Round(radius * Math.Sin(angle));
            int height = 4 + rng.Next(4);
            bool fallen = rng.Chance(0.2f);
            int shown = fallen ? 1 + rng.Next(2) : height;

            int hNat = b.Height(px, pz);
            for (int y = 1; y <= shown; y++) b.Set(px, hNat + y, pz, mat);
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })   // base en cruz
                b.Set(px + dx, b.Height(px + dx, pz + dz) + 1, pz + dz, mat);
            if (!fallen) b.Set(px, hNat + height + 1, pz, Blocks.Crystal);
        }

        int hC = b.Height(cx, cz);   // altar central
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++) b.Set(cx + dx, hC + 1, cz + dz, Blocks.Panel);
        b.Set(cx, hC + 2, cz, Blocks.Panel);
        b.Set(cx, hC + 3, cz, Blocks.Crystal);
    }

    // ------------------------------------------------------------------ pilono

    private static void Pylon(Builder b, int cx, int cz, int g0, SplitMixRandom rng)
    {
        int height = 14 + rng.Next(9);
        b.ClearDisc(cx, cz, 5, height + 6);

        for (int dz = -2; dz <= 2; dz++)
        for (int dx = -2; dx <= 2; dx++)
        {
            int wx = cx + dx, wz = cz + dz;
            for (int y = b.Height(wx, wz) + 1; y <= g0; y++) b.Set(wx, y, wz, Biomes.DeepStone);   // cimientos
            b.Set(wx, g0 + 1, wz, Math.Max(Math.Abs(dx), Math.Abs(dz)) == 2 ? Blocks.Panel : Blocks.RustMetal);
        }
        for (int k = 1; k <= height; k++)
        {
            int y = g0 + 1 + k;
            b.Set(cx, y, cz, Blocks.RustMetal);
            if (k % 5 == 0)   // anillos de paneles
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }) b.Set(cx + dx, y, cz + dz, Blocks.Panel);
        }
        for (int k = 1; k <= 3; k++) b.Set(cx, g0 + 1 + height + k, cz, Blocks.Crystal);   // corona luminosa

        foreach (var (sxn, szn) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })   // puntales
            for (int k = 0; k <= 6; k++)
                b.Set(cx + sxn * (2 - k / 3), g0 + 2 + k, cz + szn * (2 - k / 3), Blocks.Panel);
    }

    // ------------------------------------------------------------------ arboleda de cristal

    private static void Grove(Builder b, int cx, int cz, SplitMixRandom rng)
    {
        int radius = 7 + rng.Next(4), count = 6 + rng.Next(7);
        b.ClearDisc(cx, cz, radius + 2, 14);
        for (int i = 0; i < count; i++)
        {
            double angle = rng.NextFloat() * 2 * Math.PI, dist = rng.NextFloat() * radius;
            int px = cx + (int)Math.Round(dist * Math.Cos(angle)), pz = cz + (int)Math.Round(dist * Math.Sin(angle));
            int height = 4 + rng.Next(9);
            bool wide = rng.Chance(0.5f);

            int hNat = b.Height(px, pz);
            for (int y = 1; y <= height; y++) b.Set(px, hNat + y, pz, Blocks.Crystal);
            if (wide)
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    for (int y = 1; y <= height / 3; y++) b.Set(px + dx, b.Height(px + dx, pz + dz) + y, pz + dz, Blocks.Crystal);
        }
    }
}
