using GF.Core;
using GF.World;

namespace MiniCraft;

/// <summary>
/// Escribe los bloques de una planta en un chunk, en coordenadas relativas a su base (0,0,0 = la celda sobre el suelo). Solo toca celdas del propio chunk.
/// Un bloque "blando" (copa) solo ocupa aire; uno "duro" (tronco) ocupa aire y sustituye copas. Con el orden de recorrido fijo de TreeStage, el resultado
/// no depende del chunk que se esté generando: no hay costuras.
/// </summary>
internal sealed class FloraWriter
{
    private readonly ushort[] _cells;
    private readonly int _ox, _oy, _oz, _sx, _sy, _sz;
    private int _bx, _by, _bz;

    public FloraWriter(Chunk<ushort> chunk, CellCoord origin)
    {
        _cells = chunk.Cells;
        var s = chunk.Shape;
        _sx = s.SizeX; _sy = s.SizeY; _sz = s.SizeZ;
        _ox = origin.X; _oy = origin.Y; _oz = origin.Z;
    }

    public void SetBase(int wx, int wy, int wz) { _bx = wx; _by = wy; _bz = wz; }

    public static bool IsSoft(ushort id) => id == Blocks.Leaves || id == Blocks.Fronds || id == Blocks.SporeCap;

    public void Put(int dx, int dy, int dz, ushort id, bool hard)
    {
        int x = _bx + dx - _ox, y = _by + dy - _oy, z = _bz + dz - _oz;
        if ((uint)x >= (uint)_sx || (uint)y >= (uint)_sy || (uint)z >= (uint)_sz) return;
        int i = (y * _sz + z) * _sx + x;
        ushort cur = _cells[i];
        if (cur == Blocks.Air || (hard && IsSoft(cur))) _cells[i] = id;
    }
}

/// <summary>Las formas (blueprints) de planta. Cada una recibe la especie, la altura y el radio de ESTE ejemplar y un RNG determinista.</summary>
internal static class FloraShapes
{
    private static readonly (int X, int Z)[] Axes = { (1, 0), (-1, 0), (0, 1), (0, -1) };
    private static readonly (int X, int Z)[] Dirs8 = { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) };

    public static void Build(FloraWriter w, FloraSpecies sp, int h, int r, SplitMixRandom rng)
    {
        switch (sp.Shape)
        {
            case FloraShape.Round: Round(w, sp, h, r); break;
            case FloraShape.Conifer: Conifer(w, sp, h, r); break;
            case FloraShape.Umbrella: Umbrella(w, sp, h, r, rng); break;
            case FloraShape.Palm: Palm(w, sp, h, r, rng); break;
            case FloraShape.Mushroom: Mushroom(w, sp, h, r, rng); break;
            case FloraShape.Twisted: Twisted(w, sp, h, r, rng); break;
            case FloraShape.Weeping: Weeping(w, sp, h, r, rng); break;
            case FloraShape.Dead: Dead(w, sp, h, rng); break;
            case FloraShape.Cactus: Cactus(w, sp, h, rng); break;
            case FloraShape.Spire: Spire(w, sp, h, rng); break;
            case FloraShape.Bush: Bush(w, sp, h, r); break;
            case FloraShape.Thorn: Thorn(w, sp, h, r); break;
            default: Mound(w, sp, r); break;
        }
    }

    // ------------------------------------------------------------------ utilidades

    private static void Blob(FloraWriter w, int cx, int cy, int cz, float rx, float ry, float rz, ushort id, bool hard)
    {
        int ex = (int)MathF.Ceiling(rx), ey = (int)MathF.Ceiling(ry), ez = (int)MathF.Ceiling(rz);
        for (int dy = -ey; dy <= ey; dy++)
        {
            int y = cy + dy;
            if (y < 0) continue;
            for (int dz = -ez; dz <= ez; dz++)
            for (int dx = -ex; dx <= ex; dx++)
                if ((dx * dx) / (rx * rx) + (dy * dy) / (ry * ry) + (dz * dz) / (rz * rz) <= 1f) w.Put(cx + dx, y, cz + dz, id, hard);
        }
    }

    private static void Disc(FloraWriter w, int y, int rad, ushort id, bool hard)
    {
        if (rad < 0 || y < 0) return;
        float lim = rad * rad + rad * 0.5f;
        for (int dz = -rad; dz <= rad; dz++)
        for (int dx = -rad; dx <= rad; dx++)
            if (dx * dx + dz * dz <= lim) w.Put(dx, y, dz, id, hard);
    }

    private static void Trunk(FloraWriter w, int h, ushort id)
    {
        for (int y = 0; y < h; y++) w.Put(0, y, 0, id, true);
    }

    // ------------------------------------------------------------------ árboles

    private static void Round(FloraWriter w, FloraSpecies sp, int h, int r)
    {
        Blob(w, 0, h, 0, r + 0.5f, r * 0.75f + 0.6f, r + 0.5f, sp.Canopy, false);
        Trunk(w, h, sp.Trunk);
    }

    private static void Conifer(FloraWriter w, FloraSpecies sp, int h, int r)
    {
        for (int y = 2; y <= h + 1; y++)
        {
            float t = (float)(h + 1 - y) / MathF.Max(1, h - 1);
            int rad = (int)MathF.Round(r * t);
            if ((y & 1) == 0 && rad > 0) rad--;   // pisos: un nivel sí y otro no, más estrecho
            Disc(w, y, rad, sp.Canopy, false);
        }
        Trunk(w, h + 1, sp.Trunk);
    }

    private static void Umbrella(FloraWriter w, FloraSpecies sp, int h, int r, SplitMixRandom rng)
    {
        Disc(w, h, r, sp.Canopy, false);
        Disc(w, h + 1, Math.Max(0, r - 2), sp.Canopy, false);
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            int d2 = dx * dx + dz * dz;
            if (d2 > (r - 1) * (r - 1) && d2 <= r * r + r * 0.5f && rng.Chance(0.5f)) w.Put(dx, h - 1, dz, sp.Canopy, false);   // borde caído
        }
        Trunk(w, h, sp.Trunk);
    }

    private static void Palm(FloraWriter w, FloraSpecies sp, int h, int r, SplitMixRandom rng)
    {
        int leanX = rng.Next(-2, 3), leanZ = rng.Next(-1, 2);
        for (int y = 0; y < h; y++)
        {
            float t = y / (float)h;
            w.Put((int)MathF.Round(leanX * t * t), y, (int)MathF.Round(leanZ * t * t), sp.Trunk, true);
        }
        int tx = leanX, tz = leanZ;
        w.Put(tx, h, tz, sp.Canopy, false);
        w.Put(tx, h + 1, tz, sp.Canopy, false);
        for (int d = 0; d < Dirs8.Length; d++)
        {
            int len = d < 4 ? r : Math.Max(1, r - 1);
            for (int i = 1; i <= len; i++)
            {
                int yy = h + (i == 1 ? 1 : 0) - (i * i) / (r + 2);   // fronda que se arquea hacia abajo
                w.Put(tx + Dirs8[d].X * i, yy, tz + Dirs8[d].Z * i, sp.Canopy, false);
            }
        }
    }

    private static void Mushroom(FloraWriter w, FloraSpecies sp, int h, int r, SplitMixRandom rng)
    {
        Trunk(w, h, sp.Trunk);
        Disc(w, h, r, sp.Canopy, false);
        Disc(w, h + 1, r - 1, sp.Canopy, false);
        if (r >= 4) Disc(w, h + 2, r - 3, sp.Canopy, false);
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            int d2 = dx * dx + dz * dz;
            if (d2 > (r - 1) * (r - 1) && d2 <= r * r + r * 0.5f && h - 1 >= 1 && rng.Chance(0.7f)) w.Put(dx, h - 1, dz, sp.Canopy, false);   // láminas bajo el sombrero
        }
    }

    private static void Twisted(FloraWriter w, FloraSpecies sp, int h, int r, SplitMixRandom rng)
    {
        var xs = new int[h];
        var zs = new int[h];
        int x = 0, z = 0;
        for (int y = 0; y < h; y++)
        {
            xs[y] = x; zs[y] = z;
            w.Put(x, y, z, sp.Trunk, true);
            if (y >= 2 && rng.Chance(0.35f))
            {
                x = Math.Clamp(x + rng.Next(-1, 2), -2, 2);
                z = Math.Clamp(z + rng.Next(-1, 2), -2, 2);
            }
        }
        Blob(w, x, h, z, r + 0.5f, r * 0.7f + 0.6f, r + 0.5f, sp.Canopy, false);
        for (int b = 0; b < 2; b++)   // ramas con su propia mata de hojas
        {
            int y0 = 2 + rng.Next(Math.Max(1, h - 3));
            var (ax, az) = Axes[rng.Next(4)];
            int len = 2 + rng.Next(2), ex = xs[y0], ey = y0, ez = zs[y0];
            for (int i = 1; i <= len; i++)
            {
                ex = xs[y0] + ax * i; ey = y0 + i / 2; ez = zs[y0] + az * i;
                w.Put(ex, ey, ez, sp.Trunk, true);
            }
            Blob(w, ex, ey + 1, ez, 1.5f, 1.2f, 1.5f, sp.Canopy, false);
        }
    }

    private static void Weeping(FloraWriter w, FloraSpecies sp, int h, int r, SplitMixRandom rng)
    {
        int rr = r + 1;
        Blob(w, 0, h, 0, rr + 0.5f, r * 0.6f + 0.6f, rr + 0.5f, sp.Canopy, false);
        for (int dz = -rr; dz <= rr; dz++)
        for (int dx = -rr; dx <= rr; dx++)
        {
            int d2 = dx * dx + dz * dz;
            if (d2 < (rr - 1) * (rr - 1) || d2 > rr * rr + rr || !rng.Chance(0.55f)) continue;
            int len = 2 + rng.Next(4);
            for (int k = 1; k <= len && h - k >= 1; k++) w.Put(dx, h - k, dz, sp.Canopy, false);   // cortina que cuelga
        }
        Trunk(w, h, sp.Trunk);
    }

    private static void Dead(FloraWriter w, FloraSpecies sp, int h, SplitMixRandom rng)
    {
        Trunk(w, h, sp.Trunk);
        int count = 2 + rng.Next(3);
        for (int b = 0; b < count; b++)
        {
            int y0 = Math.Max(2, h / 2 + rng.Next(Math.Max(1, h - h / 2)));
            var (ax, az) = Axes[rng.Next(4)];
            int len = 2 + rng.Next(3), x = 0, z = 0, y = y0;
            for (int i = 1; i <= len; i++)
            {
                x = ax * i; z = az * i; y = y0 + i / 2;
                w.Put(x, y, z, sp.Trunk, true);
            }
            w.Put(x, y + 1, z, sp.Trunk, true);   // la punta de la rama sube
        }
    }

    private static void Cactus(FloraWriter w, FloraSpecies sp, int h, SplitMixRandom rng)
    {
        Trunk(w, h, sp.Trunk);
        if (h >= 4)
        {
            int arms = 1 + rng.Next(3);
            for (int a = 0; a < arms; a++)
            {
                var (ax, az) = Axes[rng.Next(4)];
                int y0 = 2 + rng.Next(Math.Max(1, h - 3)), outLen = 1 + rng.Next(2), up = 1 + rng.Next(3);
                for (int i = 1; i <= outLen; i++) w.Put(ax * i, y0, az * i, sp.Trunk, true);
                for (int k = 1; k <= up; k++) w.Put(ax * outLen, y0 + k, az * outLen, sp.Trunk, true);
            }
        }
        if (sp.Canopy != sp.Trunk) w.Put(0, h, 0, sp.Canopy, false);   // flor o fruto en la punta
    }

    private static void Spire(FloraWriter w, FloraSpecies sp, int h, SplitMixRandom rng)
    {
        for (int y = 0; y < h; y++)
        {
            w.Put(0, y, 0, sp.Trunk, true);
            if (y < h / 3)   // base ancha en cruz
            {
                w.Put(1, y, 0, sp.Trunk, true); w.Put(-1, y, 0, sp.Trunk, true);
                w.Put(0, y, 1, sp.Trunk, true); w.Put(0, y, -1, sp.Trunk, true);
            }
        }
        int n = 2 + rng.Next(3);
        for (int i = 0; i < n; i++)   // agujas menores alrededor
        {
            int ox = rng.Next(-3, 4), oz = rng.Next(-3, 4);
            if (Math.Abs(ox) + Math.Abs(oz) < 2) continue;
            int sh = h / 3 + rng.Next(Math.Max(1, h / 3));
            for (int y = 0; y < sh; y++) w.Put(ox, y, oz, sp.Trunk, true);
        }
    }

    // ------------------------------------------------------------------ arbustos

    private static void Bush(FloraWriter w, FloraSpecies sp, int h, int r)
    {
        if (h >= 2) w.Put(0, 0, 0, sp.Trunk, true);
        Blob(w, 0, h, 0, r + 0.5f, r * 0.6f + 0.5f, r + 0.5f, sp.Canopy, false);
    }

    private static void Thorn(FloraWriter w, FloraSpecies sp, int h, int r)
    {
        Trunk(w, h, sp.Trunk);
        foreach (var (ax, az) in Axes)
            for (int i = 1; i <= r; i++) w.Put(ax * i, h - 1 + (i == r ? 1 : 0), az * i, sp.Canopy, false);
        for (int d = 4; d < Dirs8.Length; d++)
            for (int i = 1; i <= Math.Max(1, r - 1); i++) w.Put(Dirs8[d].X * i, h - 1, Dirs8[d].Z * i, sp.Canopy, false);
        w.Put(0, h, 0, sp.Canopy, false);
    }

    private static void Mound(FloraWriter w, FloraSpecies sp, int r) =>
        Blob(w, 0, 0, 0, r + 0.5f, r * 0.7f + 0.3f, r + 0.5f, sp.Canopy, false);
}
