using GF.Core;

namespace MiniCraft;

/// <summary>
/// Variantes de los templos abandonados (la 0, el templo de columnas, está en RuinSites): 1 observatorio, 2 academia, 3 faro, 4 escalinata de monolitos. Son de una
/// civilización inventada de astrónomos y cartógrafos: sus símbolos son el Ojo (una lente con pupila), los tres anillos, la espiral y las bandas de piedra oscura y clara;
/// nada de ello imita símbolos de religiones reales. Todas se definen en el recinto cuadrado de semilado S (qx, qz: coordenadas locales giradas, entrada hacia -Z;
/// r = 1 es el primer bloque sobre el suelo). El deterioro lo aplica RuinSites sobre lo que aquí es lo intacto.
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
            1 => Observatory(c, qx, qz, r),
            2 => Academy(c, qx, qz, r, s),
            3 => Lighthouse(c, qx, qz, r),
            4 => Ziggurat(c, qx, qz, r, s),
            _ => Blocks.Air,
        };
        return id != Blocks.Air || r != 1 ? id : RuinSites.Plant(c, wx, wz);   // la maleza crece donde no hay nada
    }

    private static ushort Paved(City c, int wx, int wz, ushort natural)
    {
        ulong h = Hashing.Combine(c.Id, wx, 0xC4AB, wz);
        double u = (h >> 40) * (1.0 / (1 << 24));
        if (u < c.Decay * 0.5) return (h & 1) == 0 ? natural : Blocks.Dirt;   // el empedrado está agrietado
        return c.Style.Plaza;
    }

    /// <summary>Mosaico de los tres anillos: disco claro en el centro y tres anillos oscuros (a 3, 5,5 y 8,5 del centro), con el meridiano claro hacia la entrada.</summary>
    private static ushort Rings(City c, int qx, int qz, int wx, int wz, ushort natural, double d, double maxD)
    {
        if (qx == 0 && qz < 0) return Blocks.Sand;                              // el meridiano
        if (d < 1.5) return Blocks.Sand;
        if ((d >= 2.5 && d < 3.5) || (d >= 5 && d < 6) || (d >= 8 && d < maxD)) return Blocks.Dirt;
        return Paved(c, wx, wz, natural);
    }

    /// <summary>Suelo de la variante (mosaicos de anillos, caminos), con grietas; null = el suelo normal del recinto.</summary>
    public static ushort? Surface(City c, int variant, int qx, int qz, int wx, int wz, ushort natural, int s)
    {
        int m = Math.Max(Math.Abs(qx), Math.Abs(qz));
        double d = Dist(qx, qz, 0, 0);
        switch (variant)
        {
            case 1:   // observatorio: el salón con los tres anillos y el meridiano, y el camino de entrada
                if (d <= 9.7) return Rings(c, qx, qz, wx, wz, natural, d, 9.7);
                if (Math.Abs(qx) <= 1 && qz < -9) return qx == 0 ? Blocks.Sand : Paved(c, wx, wz, natural);
                return natural;
            case 2:   // academia: patio con el astrolabio (anillos claros y oscuros), claustro empedrado y camino
            {
                int e = s - 5, inner = e - 6;
                if (m <= inner) return d < 1.5 ? Blocks.Sand : (d >= 3.5 && d < 4.5) ? Blocks.Dirt : (d >= 7 && d < 8) ? Blocks.Sand : natural;
                if (m <= e || (Math.Abs(qx) <= 1 && qz < -e)) return Paved(c, wx, wz, natural);
                return natural;
            }
            case 3:   // faro: la base, la caseta y el camino
                if (Dist(qx, qz, 0, 3) <= 5 || InBox(qx, qz, -10, 0, -6, 4) || (Math.Abs(qx) <= 1 && qz < -2)) return Paved(c, wx, wz, natural);
                return natural;
            case 4:   // escalinata de monolitos: la pirámide, el camino y la plaza de los tres anillos al pie
                if (m <= 9) return Paved(c, wx, wz, natural);
                if (Dist(qx, qz, 0, -14) <= 5) return Rings(c, qx, qz + 14, wx, wz, natural, Dist(qx, qz, 0, -14), 5);
                if (Math.Abs(qx) <= 1 && qz <= -10) return Paved(c, wx, wz, natural);
                return natural;
        }
        return null;
    }

    // ------------------------------------------------------------------ observatorio

    /// <summary>
    /// Observatorio de la Esfera: un salón circular de 9 de radio con una cúpula hemisférica partida por una rendija de norte a sur (el meridiano), una esfera de piedra
    /// sobre una peana en el centro, un anillo de 12 menhires con bandas oscuras alrededor y dos torres de señales con un brasero (borde y cuatro cuernos) a los lados
    /// de la entrada.
    /// </summary>
    private static ushort Observatory(City c, int qx, int qz, int r)
    {
        ushort P = c.Style.Plaza;
        double d = Dist(qx, qz, 0, 0);

        if (d <= 2.5)   // esfera central: peana, pie y bola
        {
            if (Math.Abs(qx) <= 1 && Math.Abs(qz) <= 1 && r == 1) return P;
            if (qx == 0 && qz == 0 && r >= 2 && r <= 3) return P;
            double sd = Math.Sqrt(qx * qx + qz * qz + (r - 6.0) * (r - 6.0));
            if (sd <= 2.3 && r >= 4 && r <= 8) return P;
        }

        if (d <= 9.7)   // salón y cúpula
        {
            if (r <= 9)
            {
                bool door = qz < 0 && Math.Abs(qx) <= 1 && r <= 5;
                return Math.Abs(d - 9) <= 0.7 && !door ? P : Blocks.Air;
            }
            if (r <= 18)
            {
                double rd = Math.Sqrt(Math.Max(0.0, 81.0 - (r - 9.0) * (r - 9.0)));
                bool slit = Math.Abs(qx) <= 1 && r >= 11;   // la rendija
                return Math.Abs(d - rd) <= 0.8 && !slit ? P : Blocks.Air;
            }
            return Blocks.Air;
        }

        for (int k = 0; k < 12; k++)   // menhires (con hueco en el camino de entrada)
        {
            double a = k * Math.PI / 6;
            int mx = (int)Math.Round(17 * Math.Cos(a)), mz = (int)Math.Round(17 * Math.Sin(a));
            if (qx == mx && qz == mz && !(Math.Abs(mx) <= 2 && mz < 0) && r <= 4 + k % 3) return r == 2 || r == 4 ? Blocks.Dirt : P;
        }

        for (int side = -1; side <= 1; side += 2)   // torres de señales
        {
            int dx = qx - side * 12, dz = qz + 10;
            if (Math.Abs(dx) > 1 || Math.Abs(dz) > 1) continue;
            if (r <= 2) return P;
            bool ring = Math.Abs(dx) == 1 || Math.Abs(dz) == 1;
            if (r <= 16) return ring ? (r == 8 ? Blocks.Dirt : P) : Blocks.Air;   // fuste hueco con una banda oscura
            if (r == 17) return ring ? P : Blocks.Air;                              // borde del brasero
            if (r == 18) return Math.Abs(dx) == 1 && Math.Abs(dz) == 1 ? P : Blocks.Air;   // cuatro cuernos
        }
        return Blocks.Air;
    }

    // ------------------------------------------------------------------ academia

    /// <summary>
    /// Torre del Reloj de Sol: 5 x 5 hueca de 18 de alto con ranuras, una plataforma superior de 5 x 5 con parapeto y un gnomon de 4 de alto en el centro.
    /// </summary>
    private static ushort SundialTower(int dx, int dz, int r, ushort P)
    {
        int ax = Math.Abs(dx), az = Math.Abs(dz);
        if (r <= 18)
        {
            if (ax != 2 && az != 2) return Blocks.Air;
            bool corner = ax == 2 && az == 2;
            bool faceCenter = !corner && ((ax == 2 && dz == 0) || (az == 2 && dx == 0));
            return (r == 7 || r == 12) && faceCenter ? Blocks.Air : P;
        }
        if (r == 19) return P;                                        // plataforma
        if (r == 20 && (ax == 2 || az == 2)) return P;                // parapeto
        return ax == 0 && az == 0 && r >= 20 && r <= 23 ? P : Blocks.Air;   // el gnomon
    }

    /// <summary>
    /// Academia de los Archivos: un claustro cuadrado (anillo de 6 de ancho con tejado plano a 9, muro exterior con ventanas y puerta, arcada hacia el patio), la Sala de
    /// los Archivos en el ala de atrás (muros de 12, claraboyas, estanterías, una mesa y en la fachada al patio el Ojo: una lente de huecos con una pupila de madera),
    /// la Torre del Reloj de Sol en la esquina y un patio con un gnomon, cuatro mojones y matorrales.
    /// </summary>
    private static ushort Academy(City c, int qx, int qz, int r, int s)
    {
        ushort P = c.Style.Plaza;
        int e = s - 5, inner = e - 6, m = Math.Max(Math.Abs(qx), Math.Abs(qz));

        int tx = qx - (e - 2), tz = qz - (e - 2);
        if (Math.Abs(tx) <= 2 && Math.Abs(tz) <= 2) return SundialTower(tx, tz, r, P);

        if (m > inner && m <= e)
        {
            if (qz >= inner + 1 && Math.Abs(qx) <= 5)   // sala de los archivos
            {
                if (r == 13) return Math.Abs(qx) <= 3 && Mod(qz, 3) == 0 ? Blocks.Air : P;   // techo plano con claraboyas
                if (r > 13) return Blocks.Air;
                bool wallX = Math.Abs(qx) == 5, wallZ = qz == e || qz == inner + 1;
                if (wallX || wallZ)
                {
                    if (qz == inner + 1)
                    {
                        if (Math.Abs(qx) <= 1 && r <= 4) return Blocks.Air;   // puerta al patio
                        int w = 3 - Math.Abs(r - 8);                           // el Ojo: lente de huecos con pupila
                        if (r >= 5 && r <= 11)
                        {
                            if (Math.Abs(qx) == w) return Blocks.Air;
                            if (qx == 0 && r == 8) return Blocks.Log;
                        }
                        return P;
                    }
                    return r >= 5 && r <= 9 && Mod(qx + qz, 3) == 0 ? Blocks.Air : P;   // ventanales estrechos
                }
                if (Math.Abs(qx) == 4 && qz > inner + 1 && qz < e && r <= 3 && Mod(qz, 2) == 0) return P;   // estanterías
                return Math.Abs(qx) <= 1 && qz == e - 3 && r == 1 ? P : Blocks.Air;                        // mesa
            }
            if (r == 9) return P;                                   // tejado plano del claustro
            if (r > 9) return Blocks.Air;
            if (m == e)                                             // muro exterior
            {
                if (qz == -e && Math.Abs(qx) <= 2 && r <= 5) return Blocks.Air;
                return r >= 3 && r <= 4 && Mod(qx + qz, 3) == 0 ? Blocks.Air : P;
            }
            if (m == inner + 1) return Mod(qx + qz, 2) == 0 ? P : Blocks.Air;   // arcada hacia el patio
            return Blocks.Air;
        }

        if (m <= inner)   // patio
        {
            if (qx == 0 && qz == 0 && r <= 3) return P;                                  // gnomon
            if (r == 1 && ((Math.Abs(qx) == 4 && qz == 0) || (Math.Abs(qz) == 4 && qx == 0))) return P;   // cuatro mojones
            if (r == 1 && Math.Abs(qx) == 6 && Math.Abs(qz) == 6) return Blocks.Bush;
        }
        return Blocks.Air;
    }

    // ------------------------------------------------------------------ faro

    /// <summary>
    /// Faro del Vigía: una torre cilíndrica de 16 de alto a franjas (piedra y bandas oscuras) con puerta, ranuras, galería con parapeto y una linterna calada; una caseta del
    /// farero con puerta hacia la torre, y alrededor un campo de mojones apilados (pirámides pequeñas de piedra con un remate oscuro) sobre una malla de 4 x 4.
    /// </summary>
    private static ushort Lighthouse(City c, int qx, int qz, int r)
    {
        ushort P = c.Style.Plaza;
        int fz = qz - 3;
        double d = Dist(qx, fz, 0, 0);

        if (d <= 4.6)   // faro
        {
            double wall = Math.Abs(d - 2.5);
            if (r <= 16)
            {
                bool door = fz < 0 && qx == 0 && r <= 2;
                bool slit = (r == 8 || r == 12) && (qx == 0 || fz == 0);
                return wall <= 0.8 && !door && !slit ? (r % 6 >= 3 ? Blocks.Dirt : P) : Blocks.Air;
            }
            if (r == 17) return d <= 1 ? Blocks.Air : P;                                       // galería (con el hueco de la escalera)
            if (r == 18) return d >= 4.0 || wall <= 0.6 ? P : Blocks.Air;                       // parapeto y base de la linterna
            if (r == 19) return wall <= 0.6 && qx != 0 && fz != 0 ? P : Blocks.Air;            // linterna calada
            if (r == 20) return d <= 3.5 ? P : Blocks.Air;
            return r == 21 && d <= 1.5 ? P : Blocks.Air;
        }

        if (InBox(qx, qz, -10, 0, -6, 4))   // caseta del farero
        {
            if (r == 5) return P;
            if (r > 5) return Blocks.Air;
            if (qx == -10 || qx == -6 || qz == 0 || qz == 4)
            {
                if (qx == -6 && qz == 2 && r <= 2) return Blocks.Air;                // puerta hacia el faro
                return qz == 0 && qx == -8 && (r == 2 || r == 3) ? Blocks.Air : P;   // ventana
            }
            return Blocks.Air;
        }

        int cx = 4 * (int)Math.Round(qx / 4.0), cz = 4 * (int)Math.Round(qz / 4.0);   // mojones apilados
        int dx = qx - cx, dz = qz - cz;
        if (Math.Abs(dx) + Math.Abs(dz) <= 1 && Math.Abs(cx) > 1 && Dist(cx, cz, 0, 3) > 8 && !InBox(cx, cz, -12, -2, -4, 6))
        {
            ulong h = Hashing.Combine(c.Id, cx, 0x7B5, cz);
            if ((h >> 40) * (1.0 / (1 << 24)) < 0.45)
            {
                if (r == 1) return P;
                if (dx == 0 && dz == 0 && r == 2) return P;
                if (dx == 0 && dz == 0 && r == 3) return Blocks.Dirt;
            }
        }
        return Blocks.Air;
    }

    // ------------------------------------------------------------------ escalinata de monolitos

    /// <summary>
    /// Escalinata de los Monolitos: una pirámide escalonada de cuatro niveles (de 2 de ancho cada uno) con ocho monolitos de bandas en el borde de la plataforma y un brasero
    /// de piedra en el centro, y un camino de hitos (columnas bajas con remate oscuro) que sube desde la entrada.
    /// </summary>
    private static ushort Ziggurat(City c, int qx, int qz, int r, int s)
    {
        ushort P = c.Style.Plaza;
        int m = Math.Max(Math.Abs(qx), Math.Abs(qz));
        int h = m <= 3 ? 4 : m <= 5 ? 3 : m <= 7 ? 2 : m <= 9 ? 1 : 0;
        if (h > 0 && r <= h) return P;

        if (m <= 3)
        {
            int rr = r - 4;
            bool mono = m == 3 && Mod(qx, 3) == 0 && Mod(qz, 3) == 0;   // ocho monolitos
            if (mono && rr >= 1 && rr <= 5) return rr == 2 || rr == 4 ? Blocks.Dirt : P;
            if (m == 1 && rr >= 1 && rr <= 2) return P;                  // borde del brasero
            if (m == 0 && rr == 1) return Blocks.Dirt;                   // brasas
        }

        for (int i = 0; i < 12; i++)   // camino de hitos
        {
            int sz = -(s - 3) + 4 * i;
            if (sz > -12) break;
            if (Math.Abs(qx) == 3 && qz == sz && r <= 3) return r == 3 ? Blocks.Dirt : P;
        }
        return Blocks.Air;
    }
}
