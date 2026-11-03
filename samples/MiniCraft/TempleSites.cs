using GF.Core;

namespace MiniCraft;

/// <summary>
/// Variantes de los templos abandonados (la 0, el templo clásico de peristilo, está en RuinSites): 1 catedral, 2 monasterio con claustro, 3 ermita con cementerio, 4 calvario. Todas se definen
/// en el recinto cuadrado de semilado S (qx, qz: coordenadas locales giradas, entrada hacia -Z; r = 1 es el primer bloque sobre el suelo) y devuelven Air
/// donde no hay nada. El deterioro (de arriba abajo, sin bloques flotantes) lo aplica RuinSites sobre lo que aquí es el edificio intacto. Los árboles y
/// el seto se hacen con matorrales (plantas), que no se deterioran.
/// </summary>
internal static class TempleSites
{
    private static int Mod(int a, int b) => ((a % b) + b) % b;
    private static bool InBox(int x, int z, int x0, int z0, int x1, int z1) => x >= x0 && x <= x1 && z >= z0 && z <= z1;
    private static double Dist(int x, int z, int cx, int cz) { double dx = x - cx, dz = z - cz; return Math.Sqrt(dx * dx + dz * dz); }

    public static ushort Block(City c, int variant, int qx, int qz, int r, int wx, int wz, int s)
    {
        if (Math.Max(Math.Abs(qx), Math.Abs(qz)) > s) return Blocks.Air;
        ushort id = variant switch
        {
            1 => Cathedral(c, qx, qz, r, s),
            2 => Monastery(c, qx, qz, r, s),
            3 => Chapel(c, qx, qz, r, s),
            4 => Calvary(c, qx, qz, r, s),
            _ => Blocks.Air,
        };
        return id != Blocks.Air || r != 1 ? id : RuinSites.Plant(c, wx, wz);   // la maleza crece donde no hay nada
    }

    /// <summary>Suelo de la variante (naves empedradas, patios con hierba, caminos), con grietas; null = el suelo normal del recinto.</summary>
    public static ushort? Surface(City c, int variant, int qx, int qz, int wx, int wz, ushort natural, int s)
    {
        int m = Math.Max(Math.Abs(qx), Math.Abs(qz));
        switch (variant)
        {
            case 1:
            {
                int zf = -(s - 6);
                bool paved = CathedralDepth(qx, qz, s) >= 0 || (Math.Abs(qx) <= 9 && qz >= zf - 6 && qz < zf) ||
                             (Math.Abs(Math.Abs(qx) - 7) <= 2 && qz >= zf && qz <= zf + 4);
                return paved ? Paved(c, wx, wz, natural) : natural;
            }
            case 2:
            {
                int e = s - 5, i = e - 6;
                if (qx == 0 && qz == 0) return Blocks.Water;                 // el pozo del claustro
                if (m <= i) return natural;                                   // el patio, con hierba
                if (m <= e || (Math.Abs(qx) <= 1 && qz < -e)) return Paved(c, wx, wz, natural);
                return natural;
            }
            case 3:
            {
                bool paved = InBox(qx, qz, -3, -2, 3, 8) || InBox(qx, qz, -6, -1, -4, 1) || (Math.Abs(qx) <= 1 && qz < -2 && qz >= -(s - 1));
                return paved ? Paved(c, wx, wz, natural) : natural;
            }
            case 4:
                return m <= 8 || (Math.Abs(qx) <= 1 && qz <= -9) ? Paved(c, wx, wz, natural) : natural;
        }
        return null;
    }

    private static ushort Paved(City c, int wx, int wz, ushort natural)
    {
        ulong h = Hashing.Combine(c.Id, wx, 0xC4AB, wz);
        double u = (h >> 40) * (1.0 / (1 << 24));
        if (u < c.Decay * 0.5) return (h & 1) == 0 ? natural : Blocks.Dirt;   // el empedrado está agrietado
        return c.Style.Plaza;
    }

    /// <summary>
    /// Torre cuadrada de 5 x 5 hueca (dx, dz relativos a su centro): muros hasta la altura h, vanos del campanario en el centro de las cuatro caras (de h - 6 a
    /// h - 3), dos ranuras y una aguja piramidal encima (5 x 5, 3 x 3 y 1 x 1).
    /// </summary>
    private static ushort Tower5(int dx, int dz, int r, int h, ushort P)
    {
        int ax = Math.Abs(dx), az = Math.Abs(dz);
        if (r <= h)
        {
            if (ax != 2 && az != 2) return Blocks.Air;
            bool corner = ax == 2 && az == 2;
            bool faceCenter = !corner && ((ax == 2 && dz == 0) || (az == 2 && dx == 0));
            bool belfry = r >= h - 6 && r <= h - 3 && faceCenter;
            bool slit = (r == 7 || r == h - 10) && faceCenter;
            return belfry || slit ? Blocks.Air : P;
        }
        int k = r - h - 1;
        return k >= 0 && k <= 2 && Math.Max(ax, az) <= 2 - k ? P : Blocks.Air;
    }

    // ------------------------------------------------------------------ catedral

    /// <summary>
    /// Profundidad de una celda dentro de la planta de cruz latina (0 en el muro, -1 fuera): la nave (|qx| ≤ 4, de la fachada al ábside), el transepto (brazos de
    /// hasta 13 hacia cada lado, a la altura s - 15) y el ábside semicircular de radio 4,5 al fondo.
    /// </summary>
    private static int CathedralDepth(int qx, int qz, int s)
    {
        int zf = -(s - 6), zb = s - 10, tz = s - 15, t = Math.Min(s - 5, 13);
        int depth = -1;
        if (Math.Abs(qx) <= 4 && qz >= zf && qz <= zb) depth = Math.Max(depth, Math.Min(4 - Math.Abs(qx), qz - zf));
        if (Math.Abs(qz - tz) <= 4 && Math.Abs(qx) <= t) depth = Math.Max(depth, Math.Min(4 - Math.Abs(qz - tz), t - Math.Abs(qx)));
        if (qz >= zb)
        {
            double d = Dist(qx, qz, 0, zb);
            if (d <= 4.5) depth = Math.Max(depth, (int)Math.Floor(4.5 - d));
        }
        return depth;
    }

    /// <summary>
    /// Catedral: planta de cruz latina con muros de 10 y techo escalonado a dos aguas (cumbrera a 15), fachada con doble puerta y rosetón, ventanales estrechos,
    /// contrafuertes, dos torres con campanario y aguja (25 de alto), arcada de columnas, bancos y altar en el ábside.
    /// </summary>
    private static ushort Cathedral(City c, int qx, int qz, int r, int s)
    {
        ushort P = c.Style.Plaza;
        int zf = -(s - 6), zb = s - 10, tz = s - 15;

        for (int side = -1; side <= 1; side += 2)   // torres de la fachada
        {
            int dx = qx - side * 7, dz = qz - (zf + 2);
            if (Math.Abs(dx) <= 2 && Math.Abs(dz) <= 2) return Tower5(dx, dz, r, 22, P);
        }

        int depth = CathedralDepth(qx, qz, s);
        if (depth >= 0)
        {
            int roof = 11 + depth;
            if (r == roof) return P;
            if (r > roof) return Blocks.Air;
            if (depth == 0)
            {
                if (qz == zf && Math.Abs(qx) <= 1 && (r <= 5 || (r >= 7 && r <= 9))) return Blocks.Air;   // doble puerta y rosetón
                return r >= 4 && r <= 7 && Mod(qx + qz, 3) == 0 ? Blocks.Air : P;                         // ventanales estrechos
            }
            if (Math.Abs(qx) == 2 && qz > zf + 2 && qz < tz - 5 && Mod(qz - zf, 3) == 0 && r <= 12) return P;   // arcada de la nave
            if (qz < tz - 5 && qz > zf + 3 && r == 1 && (Math.Abs(qx) == 1 || Math.Abs(qx) == 3) && Mod(qz, 2) == 0) return Blocks.Log;   // bancos
            if (Math.Abs(qx) <= 1 && qz >= zb - 1 && qz <= zb + 1 && r <= 1) return P;   // altar
            if (qx == 0 && qz == zb && r == 2) return P;
            return Blocks.Air;
        }

        if (Math.Abs(qx) == 5 && qz > zf + 4 && qz < zb - 1 && Math.Abs(qz - tz) > 5 && Mod(qz - zf, 4) == 0 && r <= 7) return P;   // contrafuertes
        return Blocks.Air;
    }

    // ------------------------------------------------------------------ monasterio

    /// <summary>
    /// Monasterio: un claustro cuadrado (anillo de 6 de ancho con tejado plano a 9, muro exterior con ventanas y puerta, arcada hacia el patio), una iglesia en el
    /// ala de atrás (muros de 12, techo a dos aguas), un campanario en la esquina y un patio con un pozo central y matorrales.
    /// </summary>
    private static ushort Monastery(City c, int qx, int qz, int r, int s)
    {
        ushort P = c.Style.Plaza;
        int e = s - 5, inner = e - 6, m = Math.Max(Math.Abs(qx), Math.Abs(qz));

        int tx = qx - (e - 2), tz = qz - (e - 2);   // campanario (esquina de atrás a la derecha)
        if (Math.Abs(tx) <= 2 && Math.Abs(tz) <= 2) return Tower5(tx, tz, r, 18, P);

        if (m > inner && m <= e)
        {
            if (qz >= inner + 1 && Math.Abs(qx) <= 5)   // iglesia
            {
                int depth = 5 - Math.Abs(qx), roof = 13 + depth;
                if (r == roof) return P;
                if (r > roof) return Blocks.Air;
                if (depth == 0 || qz == e || qz == inner + 1)
                {
                    if (qz == inner + 1 && Math.Abs(qx) <= 1 && r <= 6) return Blocks.Air;   // puerta al patio
                    return r >= 5 && r <= 9 && Mod(qx + qz, 3) == 0 ? Blocks.Air : P;         // ventanales
                }
                return qx == 0 && qz == e - 2 && r <= 2 ? P : Blocks.Air;                     // altar
            }
            if (r == 9) return P;                                   // tejado plano del claustro
            if (r > 9) return Blocks.Air;
            if (m == e)                                             // muro exterior
            {
                if (qz == -e && Math.Abs(qx) <= 2 && r <= 5) return Blocks.Air;                // puerta
                return r >= 3 && r <= 4 && Mod(qx + qz, 3) == 0 ? Blocks.Air : P;              // ventanas
            }
            if (m == inner + 1) return Mod(qx + qz, 2) == 0 ? P : Blocks.Air;                  // arcada hacia el patio
            return Blocks.Air;
        }

        if (m <= inner)   // patio: brocal del pozo (el agua está en el suelo) y matorrales
        {
            if (m == 1 && r == 1) return P;
            if (r == 1 && (Math.Abs(qx) == 5 || qx == 0) && (Math.Abs(qz) == 5 || qz == 0) && !(qx == 0 && qz == 0)) return Blocks.Bush;
        }
        return Blocks.Air;
    }

    // ------------------------------------------------------------------ ermita

    private static ushort Tower3(int dx, int dz, int r, ushort P)
    {
        int ax = Math.Abs(dx), az = Math.Abs(dz);
        if (r <= 11)
        {
            if (ax == 0 && az == 0) return Blocks.Air;
            bool faceCenter = (ax == 1 && dz == 0) || (az == 1 && dx == 0);
            return faceCenter && r >= 8 && r <= 9 ? Blocks.Air : P;   // vanos del campanario
        }
        if (r == 12) return P;
        return r == 13 && ax == 0 && az == 0 ? P : Blocks.Air;
    }

    /// <summary>
    /// Ermita: una capilla de 7 x 11 con techo a dos aguas, puerta, ventanas y altar, un campanario de 3 x 3 junto a la fachada, y un cementerio alrededor con valla
    /// baja, camino de entrada y lápidas, cruces y obeliscos sobre una malla de 3 x 3 (el 55 % de los puntos).
    /// </summary>
    private static ushort Chapel(City c, int qx, int qz, int r, int s)
    {
        ushort P = c.Style.Plaza;
        int m = Math.Max(Math.Abs(qx), Math.Abs(qz));

        int tx = qx + 5;   // campanario
        if (Math.Abs(tx) <= 1 && Math.Abs(qz) <= 1) return Tower3(tx, qz, r, P);

        if (Math.Abs(qx) <= 3 && qz >= -2 && qz <= 8)
        {
            int depth = 3 - Math.Abs(qx), roof = 7 + depth;
            if (r == roof) return P;
            if (r > roof) return Blocks.Air;
            if (depth == 0 || qz == -2 || qz == 8)
            {
                if (qz == -2 && Math.Abs(qx) <= 1 && r <= 4) return Blocks.Air;   // puerta
                return depth == 0 && r >= 3 && r <= 4 && Mod(qz, 3) == 0 ? Blocks.Air : P;
            }
            return qx == 0 && qz == 7 && r == 1 ? P : Blocks.Air;                  // altar
        }

        if (m == s - 1) return qz == -(s - 1) && Math.Abs(qx) <= 2 ? Blocks.Air : r <= 2 ? P : Blocks.Air;   // valla del cementerio, con puerta

        // lápidas, cruces y obeliscos
        int cx = 3 * (int)Math.Round(qx / 3.0), cz = 3 * (int)Math.Round(qz / 3.0), dx = qx - cx;
        if (qz == cz && Math.Abs(dx) <= 1 && Math.Max(Math.Abs(cx), Math.Abs(cz)) <= s - 3 && Math.Abs(cx) > 1 && !InBox(cx, cz, -9, -4, 6, 11))
        {
            ulong h = Hashing.Combine(c.Id, cx, 0x7B5, cz);
            if ((h >> 40) * (1.0 / (1 << 24)) < 0.55)
            {
                double t = ((h >> 8) & 0xFFFF) / 65536.0;
                if (t < 0.65) return dx == 0 && r <= 2 ? P : Blocks.Air;                     // lápida
                if (t < 0.9) return dx == 0 ? (r <= 3 ? P : Blocks.Air) : (r == 3 ? P : Blocks.Air);   // cruz
                return dx == 0 && r <= 4 ? P : Blocks.Air;                                   // obelisco
            }
        }
        return Blocks.Air;
    }

    // ------------------------------------------------------------------ calvario

    /// <summary>
    /// Calvario: un montículo escalonado de 4 niveles con tres cruces (la central de 12 de alto y dos menores a los lados) y una vía crucis: pilares con
    /// nicho a ambos lados del camino que sube desde la entrada, cada 2 bloques.
    /// </summary>
    private static ushort Calvary(City c, int qx, int qz, int r, int s)
    {
        ushort P = c.Style.Plaza;
        int m = Math.Max(Math.Abs(qx), Math.Abs(qz));
        int mh = m <= 2 ? 4 : m <= 4 ? 3 : m <= 6 ? 2 : m <= 8 ? 1 : 0;
        if (mh > 0 && r <= mh) return P;

        if (qz == 0)
        {
            if (qx == 0 && r >= 5 && r <= 12) return P;                 // cruz central
            if (Math.Abs(qx) <= 2 && qx != 0 && r == 10) return P;
            if (Math.Abs(qx) == 4 && r >= 4 && r <= 9) return P;       // cruces laterales
        }
        if (Math.Abs(qx) == 4 && Math.Abs(qz) == 1 && r == 8) return P;

        for (int i = 0; i < 14; i++)   // vía crucis
        {
            int sz = -(s - 3) + 2 * i;
            if (sz > -10) break;
            if (qx == ((i & 1) == 0 ? -3 : 3) && qz == sz && r <= 4) return P;
        }
        return Blocks.Air;
    }
}
