using GF.Core;

namespace MiniCraft;

/// <summary>
/// Variantes de los campos de estatuas (la 0, el coloso con su círculo de estatuas, está en RuinSites): 1 avenida de reyes, 2 columbario de urnas. Dentro del círculo
/// nivelado (radio padR); qx, qz son coordenadas locales giradas y r = 1 el primer bloque sobre el suelo. El deterioro (de arriba abajo, sin bloques flotantes) lo aplica
/// RuinSites sobre lo que aquí es lo intacto.
/// </summary>
internal static class StatueSites
{
    private static int Mod(int a, int b) => ((a % b) + b) % b;
    private static double Dist(int x, int z, int cx, int cz) { double dx = x - cx, dz = z - cz; return Math.Sqrt(dx * dx + dz * dz); }

    public static ushort Block(City c, int variant, int qx, int qz, int r, int padR) =>
        variant == 1 ? Avenue(c, qx, qz, r, padR) : Columbarium(c, qx, qz, r, padR);

    /// <summary>Suelo de la variante (la calzada, las plazas, los caminos), con grietas; null = el suelo normal del círculo.</summary>
    public static ushort? Surface(City c, int variant, int qx, int qz, int wx, int wz, ushort natural, int padR)
    {
        bool paved;
        if (variant == 1)
        {
            int l = padR - 4;
            paved = (Math.Abs(qx) <= 2 && Math.Abs(qz) <= l) || Dist(qx, qz, 0, l - 2) <= 8 || (Math.Abs(qx) <= 5 && Math.Abs(qz + l) <= 2);
        }
        else
        {
            paved = Dist(qx, qz, 0, 0) <= 7 || Math.Abs(qx) <= 1 || Math.Abs(qz) <= 1 ||
                    (Math.Abs(Math.Abs(qx) - 14) <= 2 && Math.Abs(Math.Abs(qz) - 14) <= 2);
        }
        if (variant == 2 && Dist(qx, qz, 0, 0) <= 6.8)   // suelo de la rotonda: los tres anillos
        {
            double dd = Dist(qx, qz, 0, 0);
            if (dd < 1.5) return Blocks.Sand;
            if ((dd >= 2.5 && dd < 3.5) || (dd >= 4.5 && dd < 5.5)) return Blocks.Dirt;
        }
        if (!paved) return natural;
        ulong h = Hashing.Combine(c.Id, wx, 0xC4AB, wz);
        if ((h >> 40) * (1.0 / (1 << 24)) < c.Decay * 0.5) return (h & 1) == 0 ? natural : Blocks.Dirt;
        return c.Style.Plaza;
    }

    // ------------------------------------------------------------------ avenida de reyes

    /// <summary>
    /// Una calzada de 5 de ancho (solo el suelo) de punta a punta del círculo, un arco de triunfo en la entrada (pilares de 2 de ancho y 8 de alto con dintel y
    /// almenas), dos filas de estatuas de reyes (cada 8 bloques, a 6 de la calzada, cada una en su estado) y un coloso al fondo.
    /// </summary>
    private static ushort Avenue(City c, int qx, int qz, int r, int padR)
    {
        ushort P = c.Style.Plaza;
        int l = padR - 4;

        if (qz >= -l - 1 && qz <= -l + 1 && Math.Abs(qx) <= 4)   // arco de triunfo
        {
            if (Math.Abs(qx) >= 3 && r <= 8) return P;
            if (r == 9) return P;
            if (r == 10 && Mod(qx, 2) == 0) return P;
        }

        int count = (2 * l - 20) / 8;   // estatuas por fila
        int i = (int)Math.Round((qz - (-l + 6)) / 8.0);
        if (i >= 0 && i <= count)
        {
            int dz = qz - (-l + 6 + 8 * i);
            if (Math.Abs(dz) <= 1)
                for (int side = -1; side <= 1; side += 2)
                {
                    int cx = side * 6, dx = qx - cx;
                    if (Math.Abs(dx) > 5) continue;
                    int ddx = side > 0 ? -dx : dx;   // las estatuas caídas yacen hacia la calzada
                    int s = State(c, 200 + 2 * i + (side > 0 ? 1 : 0), 0.55);
                    ushort id = StatueShapes.Statue(s, ddx, dz, r, P, (i + (side > 0 ? 1 : 0)) % 3);
                    if (id != Blocks.Air) return id;
                }
        }

        if (Math.Abs(qx) <= 5 && Math.Abs(qz - (l - 2)) <= 13)   // coloso del fondo, caído hacia la calzada
            return StatueShapes.Colossus(State(c, 99, 0.4), qx, -(qz - (l - 2)), r, P, c.Id);
        return Blocks.Air;
    }

    private static int State(City c, int i, double entireBase)
    {
        ulong h = Hashing.Combine(c.Id, i, 0x57B, 1);
        double u = (h >> 40) * (1.0 / (1 << 24)), p0 = entireBase * (1 - c.Decay) + 0.05;
        return u < p0 ? 0 : u < p0 + 0.25 ? 1 : u < p0 + 0.5 ? 2 : 3;
    }

    // ------------------------------------------------------------------ columbario

    /// <summary>
    /// Una rotonda central (cascarón de radio 5 y 8 de alto con cúpula y óculo, puerta hacia -Z, altar y un anillo de urnas dentro), cuatro mausoleos en las
    /// diagonales, un muro bajo con puertas en los ejes y, entre medias, un campo de urnas sobre una malla de 3 x 3 con algún monumento más alto.
    /// </summary>
    private static ushort Columbarium(City c, int qx, int qz, int r, int padR)
    {
        ushort P = c.Style.Plaza;
        double d = Dist(qx, qz, 0, 0);

        if (d <= 7)   // rotonda
        {
            if (r <= 8)
            {
                bool door = qz < 0 && Math.Abs(qx) <= 1 && r <= 3;
                if (Math.Abs(d - 5) <= 0.7 && !door) return P;
                if (Math.Abs(qx) <= 1 && Math.Abs(qz) <= 1) return r == 1 ? P : (qx == 0 && qz == 0 && r == 2 ? Blocks.Dirt : Blocks.Air);   // altar con una urna
                if (Math.Abs(d - 3) <= 0.5 && Mod(qx + qz, 2) == 0) return StatueShapes.Urn(0, 0, r, P);                                    // urnas en el anillo interior
            }
            else if (r <= 12)
            {
                double rd = Math.Sqrt(Math.Max(0.0, 30.25 - (r - 8) * (r - 8)));
                if (!(r >= 12 && d <= 2) && Math.Abs(d - rd) <= 0.8) return P;   // cúpula con óculo
            }
            return Blocks.Air;
        }

        for (int sx = -1; sx <= 1; sx += 2)   // mausoleos
            for (int sz = -1; sz <= 1; sz += 2)
            {
                int dx = qx - sx * 14, dz = qz - sz * 14;
                if (Math.Abs(dx) <= 2 && Math.Abs(dz) <= 2) return StatueShapes.Mausoleum(sx > 0 ? -dx : dx, sz > 0 ? -dz : dz, r, P);
            }

        if (Math.Abs(d - (padR - 1)) <= 0.7)   // muro bajo con puertas
            return !(Math.Abs(qx) <= 1 || Math.Abs(qz) <= 1) && r <= 2 ? P : Blocks.Air;

        if (d < padR - 2 && d > 8 && Mod(qx, 3) == 0 && Mod(qz, 3) == 0 && Math.Abs(qx) > 1 && Math.Abs(qz) > 1)   // campo de urnas
        {
            ulong h = Hashing.Combine(c.Id, qx, 0x5E9, qz);
            double u = (h >> 40) * (1.0 / (1 << 24));
            if (u < 0.12) return r <= 4 ? P : Blocks.Air;                   // monumento
            if (u < 0.85) return StatueShapes.Urn(0, 0, r, P);               // urna
        }
        return Blocks.Air;
    }
}
