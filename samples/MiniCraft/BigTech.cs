using GF.Core;

namespace MiniCraft;

/// <summary>
/// Instalaciones tecnológicas monumentales (variantes 5 y 6; las 0..4 están en RuinSites y TechSites). Definidas en el recinto cuadrado de semilado s (qx, qz:
/// coordenadas locales giradas, entrada hacia -Z; r = 1 es el primer bloque sobre el suelo). El deterioro lo aplica RuinSites sobre lo que aquí es lo intacto.
///   5 ARCOLOGÍA: una megaestructura de tres terrazas concéntricas de metal oxidado (con muros de ventanas, pasillos anulares con puertas hacia la entrada y un
///     atrio central con un corazón de cristal), cuatro torres en las esquinas con una baliza y, fuera, campos de paneles solares sobre postes.
///   6 GRAN RADIOTELESCOPIO: un plato parabólico de hasta 20 de radio sobre un pedestal (con costillas y puntales hasta el receptor de cristal), cuatro platos
///     menores en diagonal, el edificio de control con consolas y dos mástiles de celosía.
/// </summary>
internal static class BigTech
{
    private static readonly (int X, int Z)[] Corners = { (1, 1), (1, -1), (-1, 1), (-1, -1) };

    private static int Mod(int a, int b) => ((a % b) + b) % b;

    public static ushort Block(City c, int variant, int qx, int qz, int r, int wx, int wz, int s)
    {
        ushort id = variant == 5 ? Arcology(c, qx, qz, r, s) : Telescope(c, qx, qz, r, s);
        return id != Blocks.Air || r != 1 ? id : RuinSites.Plant(c, wx, wz);   // la maleza crece donde no hay nada
    }

    private static ushort Paved(City c, int wx, int wz, ushort natural)
    {
        ulong h = Hashing.Combine(c.Id, wx, 0xC4AB, wz);
        double u = (h >> 40) * (1.0 / (1 << 24));
        if (u < c.Decay * 0.5) return (h & 1) == 0 ? natural : Blocks.Dirt;   // el firme está agrietado
        return c.Style.Plaza;
    }

    public static ushort? Surface(City c, int variant, int qx, int qz, int wx, int wz, ushort natural, int s)
    {
        int ax = Math.Abs(qx), m = Math.Max(ax, Math.Abs(qz));
        if (variant == 5) return m <= s * 70 / 100 + 2 || (ax <= 2 && qz < 0) ? Paved(c, wx, wz, natural) : natural;

        int bigR = Math.Min(20, s * 35 / 100), dd = s * 62 / 100, zb0 = -(s - 16);
        bool paved = Math.Sqrt((double)qx * qx + (double)qz * qz) <= bigR + 3 || (ax <= 2 && qz < 0) ||
                     (ax <= 11 && qz >= zb0 - 1 && qz <= zb0 + 13);
        foreach (var (sx, sz) in Corners)
            if (Math.Sqrt((double)(qx - sx * dd) * (qx - sx * dd) + (double)(qz - sz * dd) * (qz - sz * dd)) <= 9) paved = true;
        return paved ? Paved(c, wx, wz, natural) : natural;
    }

    // ------------------------------------------------------------------ arcología

    private static ushort Arcology(City c, int qx, int qz, int r, int s)
    {
        ushort P = c.Style.Plaza, M = Blocks.RustMetal, Pn = Blocks.Panel;
        int a1 = s * 70 / 100, a2 = s * 48 / 100, a3 = s * 28 / 100;
        const int H1 = 10, H2 = 19, H3 = 28;
        int ax = Math.Abs(qx), az = Math.Abs(qz), m = Math.Max(ax, az);

        foreach (var (sx, sz) in Corners)   // torres de las esquinas: 5 x 5 huecas con troneras y una baliza de cristal
        {
            int dx = qx - sx * a1, dz = qz - sz * a1;
            if (Math.Abs(dx) > 2 || Math.Abs(dz) > 2) continue;
            bool ring = Math.Abs(dx) == 2 || Math.Abs(dz) == 2;
            if (r <= 2 || r == 31) return M;
            if (r <= 30) return ring && !(r % 8 == 5 && (dx == 0 || dz == 0)) ? M : Blocks.Air;
            return dx == 0 && dz == 0 && r <= 34 ? Blocks.Crystal : Blocks.Air;
        }

        if (m <= a1)
        {
            int a = m <= a3 ? a3 : m <= a2 ? a2 : a1;
            int h = m <= a3 ? H3 : m <= a2 ? H2 : H1;
            if (m >= a - 1)   // muro de la terraza, con una puerta hacia la entrada y hileras de ventanas
            {
                if (qz < 0 && az >= ax && ax <= 3 && r <= 7) return Blocks.Air;
                if (r <= h) return r >= 3 && (r % 6 == 3 || r % 6 == 4) && Mod(qx + qz, 3) != 0 ? Blocks.Air : r % 6 == 0 ? Pn : M;
                return r == h + 1 && Mod(qx + qz, 2) == 0 ? P : Blocks.Air;   // almenas
            }
            if (r == h) return m <= a3 - 6 && h == H3 ? Blocks.Air : P;       // techo (lucernario sobre el atrio)
            if (r > h) return Blocks.Air;
            if (h == H3 && m <= 1 && r >= 4 && r <= 16) return Blocks.Crystal;   // el corazón de cristal del atrio
            return Mod(qx, 8) == 0 && Mod(qz, 8) == 0 ? M : Blocks.Air;          // pilares
        }

        if (m > a1 + 3 && m < s - 1 && !(qz < 0 && ax <= 2) && Mod(qx, 4) < 2 && Mod(qz, 4) < 2)   // paneles solares sobre postes
        {
            if (r == 3) return Pn;
            if (r <= 2 && Mod(qx, 4) == 0 && Mod(qz, 4) == 0) return M;
        }
        return Blocks.Air;
    }

    // ------------------------------------------------------------------ gran radiotelescopio

    private static int DishY(double d, int baseH, double focus) => baseH + (int)Math.Round(d * d / focus);

    /// <summary>Un plato parabólico sobre un pedestal: cascarón de paneles con costillas de metal bajo él, mástil receptor y, si 'struts', cuatro puntales.</summary>
    private static ushort Dish(int dx, int dz, int r, int R, int baseH, int pedR, double focus, bool struts, int mastExtra)
    {
        ushort M = Blocks.RustMetal, Pn = Blocks.Panel;
        double d = Math.Sqrt((double)dx * dx + (double)dz * dz);
        if (d <= pedR && r <= baseH) return M;
        if (d <= R)
        {
            int yb = DishY(d, baseH, focus);
            if (r == yb) return Pn;
            if (d > pedR && r < yb && (r == yb - 1 || dx == 0 || dz == 0 || Math.Abs(dx) == Math.Abs(dz))) return M;   // costillas
        }
        int top = DishY(R, baseH, focus) + mastExtra;
        if (dx == 0 && dz == 0)
        {
            if (r > baseH && r <= top) return Pn;
            if (r > top && r <= top + 2) return Blocks.Crystal;
        }
        if (struts)
        {
            int t = dz == 0 ? Math.Abs(dx) : dx == 0 ? Math.Abs(dz) : -1;
            if (t >= 1 && t <= R && r == DishY(R, baseH, focus) + (int)Math.Round((double)mastExtra * (R - t) / R)) return Pn;
        }
        return Blocks.Air;
    }

    private static ushort Telescope(City c, int qx, int qz, int r, int s)
    {
        ushort P = c.Style.Plaza, M = Blocks.RustMetal, Pn = Blocks.Panel;
        int ax = Math.Abs(qx);
        int bigR = Math.Min(20, s * 35 / 100), dd = s * 62 / 100;

        ushort id = Dish(qx, qz, r, bigR, 8, 3, bigR * 1.5, true, 5);
        if (id != Blocks.Air) return id;
        foreach (var (sx, sz) in Corners)   // cuatro platos menores
        {
            int dx = qx - sx * dd, dz = qz - sz * dd;
            if (Math.Abs(dx) > 8 || Math.Abs(dz) > 8) continue;
            id = Dish(dx, dz, r, 7, 5, 1, 8.4, false, 2);
            if (id != Blocks.Air) return id;
        }

        int zb0 = -(s - 16), zb1 = zb0 + 12;   // edificio de control
        if (ax <= 10 && qz >= zb0 && qz <= zb1)
        {
            if (r == 7) return ax <= 6 && Mod(qx, 4) == 2 ? Blocks.Air : P;
            if (r > 7) return Blocks.Air;
            if (ax == 10 || qz == zb0 || qz == zb1)
            {
                if (qz == zb0 && ax <= 2 && r <= 4) return Blocks.Air;
                return r >= 3 && r <= 4 && Mod(qx + qz, 3) == 0 ? Blocks.Air : P;
            }
            if (Mod(qz - zb0, 4) == 2 && ax <= 6 && Mod(qx, 2) == 0) return r <= 2 ? M : r == 3 ? Pn : Blocks.Air;   // consolas
            return Blocks.Air;
        }

        for (int sg = -1; sg <= 1; sg += 2)   // mástiles de celosía
        {
            int dx = qx - sg * (s - 8);
            if (Math.Abs(dx) > 1 || Math.Abs(qz) > 1) continue;
            if (r <= 22) return Math.Abs(dx) == 1 && Math.Abs(qz) == 1 ? M : r % 6 == 0 ? Pn : Blocks.Air;
            return dx == 0 && qz == 0 && r <= 24 ? Blocks.Crystal : Blocks.Air;
        }
        return Blocks.Air;
    }
}
