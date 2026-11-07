using GF.Core;

namespace MiniCraft;

/// <summary>
/// Templos monumentales (variantes 5 y 6 de los templos; las 0..4 están en RuinSites y TempleSites). Pertenecen a la misma civilización inventada de astrónomos y
/// cartógrafos: sus símbolos son el Ojo (una lente con pupila de cristal), los anillos concéntricos y la luz; nada imita símbolos de religiones reales.
///   5 GRAN HIPÓSTILO DE LA ESFERA: pilono de entrada, salón de naves con bosque de columnas (nave central alta con claristorio y lucernarios, naves laterales
///     bajas), gran cúpula con óculo, estrado con un altar de cristal bajo el Ojo de la pared del fondo, contrafuertes y dos torres de señales.
///   6 ROTONDA DEL GRAN DOMO: una rotonda de hasta 26 de radio con un portal y un pórtico, ventanas en lente, anillo de 16 columnas, un domo elíptico con óculo
///     y un altar central de cristal sobre una plataforma de anillos; todo sobre una terraza escalonada.
/// Se definen en el recinto cuadrado de semilado s (qx, qz: coordenadas locales giradas, entrada hacia -Z; r = 1 es el primer bloque sobre el suelo).
/// El deterioro (de arriba abajo, sin bloques flotantes) lo aplica RuinSites sobre lo que aquí es lo intacto.
/// </summary>
internal static class BigTemples
{
    private static int Mod(int a, int b) => ((a % b) + b) % b;
    private static double Dist(int x, int z, int cx, int cz) { double dx = x - cx, dz = z - cz; return Math.Sqrt(dx * dx + dz * dz); }

    public static ushort Block(City c, int variant, int qx, int qz, int r, int wx, int wz, int s)
    {
        if (Math.Max(Math.Abs(qx), Math.Abs(qz)) > s) return Blocks.Air;
        ushort id = variant == 5 ? Hypostyle(c, qx, qz, r, s) : Rotunda(c, qx, qz, r, s);
        return id != Blocks.Air || r != 1 ? id : RuinSites.Plant(c, wx, wz);   // la maleza crece donde no hay nada
    }

    private static ushort Paved(City c, int wx, int wz, ushort natural)
    {
        ulong h = Hashing.Combine(c.Id, wx, 0xC4AB, wz);
        double u = (h >> 40) * (1.0 / (1 << 24));
        if (u < c.Decay * 0.5) return (h & 1) == 0 ? natural : Blocks.Dirt;   // el empedrado está agrietado
        return c.Style.Plaza;
    }

    public static ushort? Surface(City c, int variant, int qx, int qz, int wx, int wz, ushort natural, int s)
    {
        int ax = Math.Abs(qx);
        if (variant == 5)
        {
            Dims(s, out int hw, out _, out int z0, out int z1, out int zd, out _);
            if (ax <= hw && qz >= z0 && qz <= z1)   // suelo del salón: anillos alrededor de la cúpula y alfombra hacia el altar
            {
                double d = Dist(qx, qz, 0, zd);
                if (d < 2) return Blocks.Sand;
                if ((d >= 4 && d < 5) || (d >= 7 && d < 8) || (d >= 11 && d < 12) || (d >= 16 && d < 17)) return Blocks.Dirt;
                if (ax <= 1 && qz < zd - 2) return Blocks.Sand;
                return Paved(c, wx, wz, natural);
            }
            if (qz < z0 && ax <= 8) return Paved(c, wx, wz, natural);   // avenida de entrada
            return natural;
        }

        int rr = Math.Min(s - 10, 26);
        double dd = Math.Sqrt((double)qx * qx + (double)qz * qz);
        if (dd <= rr - 3)
        {
            if (dd < 2) return Blocks.Sand;
            if ((dd >= 3 && dd < 4) || (dd >= 8 && dd < 9) || (dd >= 13 && dd < 14) || (dd >= 18 && dd < 19)) return Blocks.Dirt;
            return Paved(c, wx, wz, natural);
        }
        if (dd <= rr + 8 || (ax <= 3 && qz < 0)) return Paved(c, wx, wz, natural);
        return natural;
    }

    // ------------------------------------------------------------------ gran hipóstilo

    private static void Dims(int s, out int hw, out int nave, out int z0, out int z1, out int zd, out int rd)
    {
        hw = Math.Max(14, s * 42 / 100);                 // semianchura del salón
        nave = Math.Max(4, hw * 40 / 100);               // semianchura de la nave central
        z0 = -(s * 55 / 100); z1 = s * 55 / 100;         // el salón va de z0 a z1
        rd = Math.Min(nave + 1, 9);                      // radio de la cúpula
        zd = z1 - rd - 6;                                // centro de la cúpula
    }

    private static ushort Hypostyle(City c, int qx, int qz, int r, int s)
    {
        ushort P = c.Style.Plaza;
        Dims(s, out int hw, out int nave, out int z0, out int z1, out int zd, out int rd);
        int ax = Math.Abs(qx);
        bool nv = ax <= nave + 2;   // sobre la nave central (con su claristorio)

        // Pilono de entrada: dos torreones macizos con un portón, almenas y troneras.
        if (qz >= z0 - 6 && qz <= z0 - 1 && ax <= hw + 4)
        {
            if (ax <= 3 && r <= 11 - (ax * ax) / 2) return Blocks.Air;
            if (r <= 16) return r >= 8 && r <= 10 && qz == z0 - 3 && Mod(ax, 6) == 4 ? Blocks.Air : P;
            return r == 17 && Mod(qx + qz, 2) == 0 ? P : Blocks.Air;
        }

        // Torres de señales detrás de las naves, con brasero de cristal.
        for (int sg = -1; sg <= 1; sg += 2)
        {
            int dx = qx - sg * (hw + 3), dz = qz - (z1 - 6);
            if (Math.Abs(dx) > 1 || Math.Abs(dz) > 1) continue;
            bool ring = Math.Abs(dx) == 1 || Math.Abs(dz) == 1;
            if (r <= 2) return P;
            if (r <= 27) return ring && !(r % 8 == 5 && (dx == 0 || dz == 0)) ? P : Blocks.Air;
            if (r == 28) return P;
            return dx == 0 && dz == 0 && r <= 31 ? Blocks.Crystal : Blocks.Air;
        }

        // Contrafuertes en los muros laterales.
        if (ax > hw && ax <= hw + 3 && qz >= z0 && qz <= z1 && Mod(qz - z0, 8) == 4) return r <= 13 - (ax - hw) * 3 ? P : Blocks.Air;

        // Avenida de entrada: postes con una luz de cristal.
        if (qz < z0 - 6)
            return ax == 7 && qz >= -s + 3 && Mod(qz - z0, 6) == 0 ? (r <= 4 ? P : r == 5 ? Blocks.Crystal : Blocks.Air) : Blocks.Air;
        if (qz < z0 || qz > z1 || ax > hw) return Blocks.Air;

        int wallH = nv ? 20 : 12;
        if (qz == z0) return ax <= 3 && r <= 10 ? Blocks.Air : r <= wallH ? P : Blocks.Air;   // fachada del salón, con portón
        if (qz == z1)   // muro del fondo con el Ojo: una lente de huecos con la pupila de cristal
        {
            if (ax <= 6 && r >= 8 && r <= 18)
            {
                int w = 5 - Math.Abs(r - 13);
                if (w >= 0 && ax <= w) return ax <= 1 && r >= 12 && r <= 14 ? Blocks.Crystal : Blocks.Air;
            }
            return r <= wallH ? P : Blocks.Air;
        }
        if (ax == hw)   // muro lateral con ventanales en lente
        {
            if (r > 12) return Blocks.Air;
            int m = Mod(qz - z0, 6);
            bool lens = m >= 2 && m <= 4 && (r >= 5 && r <= 9 || (m == 3 && (r == 4 || r == 10)));
            return lens ? Blocks.Air : P;
        }

        // Cúpula sobre la nave central, con óculo.
        double ddx = qx, ddz = qz - zd, h2 = ddx * ddx + ddz * ddz;
        if (r >= 20)
        {
            double d3 = Math.Sqrt(h2 + (r - 20.0) * (r - 20.0));
            if (d3 <= rd && d3 >= rd - 0.8) return (r - 20) >= rd - 1 && h2 <= 2 ? Blocks.Air : P;
        }
        bool inDome = h2 < (rd - 1.0) * (rd - 1.0);

        if (r == 20 && nv) return inDome || (ax <= 2 && Mod(qz - z0, 12) >= 4 && Mod(qz - z0, 12) <= 7) ? Blocks.Air : P;   // techo de la nave con lucernarios
        if (r == 12 && ax >= nave + 2) return P;                                                                              // techo de las naves laterales
        if (ax == nave + 2 && r >= 13 && r <= 19)                                                                              // claristorio
            return r >= 15 && r <= 18 && (Mod(qz - z0, 4) == 1 || Mod(qz - z0, 4) == 2) ? Blocks.Air : P;

        int mid = (nave + 2 + hw) / 2;   // columnas: 2 x 2, altas en la nave y bajas en las laterales
        if (Mod(qz - z0 - 4, 6) <= 1)
        {
            if ((ax == nave || ax == nave + 1) && r <= 19) return P;
            if ((ax == mid || ax == mid + 1) && r <= 11) return P;
        }

        if (ax <= nave && qz >= z1 - 9)   // estrado de tres escalones y altar de cristal
        {
            int step = qz > z1 - 3 ? 3 : qz > z1 - 6 ? 2 : 1;
            if (r <= step) return P;
            if (ax <= 1 && qz >= z1 - 4 && qz <= z1 - 2)
            {
                if (r <= 4) return P;
                if (r == 5 && ax == 0 && qz == z1 - 3) return Blocks.Crystal;
            }
        }
        return Blocks.Air;
    }

    // ------------------------------------------------------------------ rotonda del gran domo

    private static ushort Rotunda(City c, int qx, int qz, int r, int s)
    {
        ushort P = c.Style.Plaza;
        int R = Math.Min(s - 10, 26);
        int ax = Math.Abs(qx);
        double d = Math.Sqrt((double)qx * qx + (double)qz * qz);

        // Pórtico de entrada: dos filas de columnas bajo un frontón plano, sobre un zócalo.
        if (qz < 0 && qz >= -(R + 6) && qz <= -(R + 1) && ax <= 11)
        {
            if (r == 12) return P;
            if (r <= 11 && (ax == 5 || ax == 9) && (qz == -(R + 2) || qz == -(R + 5))) return P;
            return r <= 3 ? P : Blocks.Air;
        }
        if (d > R + 8) return Blocks.Air;
        if (d > R) { int h = 4 - (int)((d - R - 0.01) / 2.0); return r <= h ? P : Blocks.Air; }   // terraza escalonada alrededor

        int hd = Math.Max(8, R * 6 / 10);
        if (r >= 14)   // domo elíptico: cascarón con óculo en lo alto
        {
            double e = Math.Pow((r - 14.0) / hd, 2) + (d / R) * (d / R);
            return e <= 1.0 && e >= 0.86 && !(d <= 4 && r - 14 > hd * 0.8) ? P : Blocks.Air;
        }
        if (d >= R - 2)   // muro con gran portal y ventanas en lente a cada 45 grados
        {
            if (qz < 0 && ax <= 4 && r <= 9 - (ax * ax) / 4) return Blocks.Air;
            double f = Math.Atan2(qz, qx) * 8 / (2 * Math.PI);
            f -= Math.Floor(f);
            double lens = 0.12 - Math.Abs(r - 7) * 0.025;
            return r >= 4 && r <= 10 && Math.Abs(f - 0.5) < lens ? Blocks.Air : P;
        }

        double rc = R - 6;   // anillo de 16 columnas (sin la que taparía el portal)
        if (Math.Abs(d - rc) < 1.5 && r <= 13)
        {
            int i = (int)Math.Round(Math.Atan2(qz, qx) / (2 * Math.PI) * 16);
            double ca = i * 2 * Math.PI / 16;
            if (i != -4 && qx == (int)Math.Round(rc * Math.Cos(ca)) && qz == (int)Math.Round(rc * Math.Sin(ca))) return P;
        }

        if (d <= 4 && r == 1) return P;                           // plataforma central
        if (d <= 1.5 && r <= 3) return P;                         // peana
        return d <= 1.0 && (r == 4 || r == 5) ? Blocks.Crystal : Blocks.Air;   // esfera de cristal
    }
}
