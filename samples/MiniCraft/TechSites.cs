using GF.Core;

namespace MiniCraft;

/// <summary>
/// Variantes de las instalaciones tecnológicas abandonadas (la 0, el complejo de investigación, está en RuinSites): 1 planta nuclear, 2 refinería, 3 aeropuerto, 4 cosmopuerto.
/// Todas se definen en el recinto cuadrado de semilado S (qx, qz: coordenadas locales giradas, entrada hacia -Z; r = 1 es el primer bloque sobre el suelo) y
/// devuelven Air donde no hay nada. El deterioro (de arriba abajo, sin bloques flotantes) lo aplica RuinSites sobre lo que aquí es el edificio intacto.
/// </summary>
internal static class TechSites
{
    private static int Mod(int a, int b) => ((a % b) + b) % b;
    private static bool InBox(int x, int z, int x0, int z0, int x1, int z1) => x >= x0 && x <= x1 && z >= z0 && z <= z1;
    private static double Dist(int x, int z, int cx, int cz) { double dx = x - cx, dz = z - cz; return Math.Sqrt(dx * dx + dz * dz); }

    public static ushort Block(City c, int variant, int qx, int qz, int r, int wx, int wz, int s)
    {
        ushort id = variant switch
        {
            1 => Nuclear(c, qx, qz, r, s),
            2 => Refinery(c, qx, qz, r, s),
            3 => Airport(c, qx, qz, r, s),
            4 => Spaceport(c, qx, qz, r, s),
            _ => Blocks.Air,
        };
        return id != Blocks.Air || r != 1 ? id : RuinSites.Plant(c, wx, wz);   // la maleza crece donde no hay nada
    }

    /// <summary>Suelo especial de la variante (pistas, plataformas de hormigón), con grietas; null = el suelo normal del recinto.</summary>
    public static ushort? Surface(City c, int variant, int qx, int qz, int wx, int wz, ushort natural, int s)
    {
        if (variant == 3)
        {
            if (qz == 0 && Math.Abs(qx) <= s - 6 && Mod(qx, 8) < 4) return Blocks.Sand;   // línea central de la pista, a trazos
            return AirportPaved(qx, qz, s) ? Paved(c, wx, wz, natural) : natural;
        }
        if (variant == 4)
        {
            if (Dist(qx, qz, 0, 0) <= 4) return Blocks.Dirt;                               // la plataforma, chamuscada
            return SpacePaved(qx, qz, s) ? Paved(c, wx, wz, natural) : natural;
        }
        return null;
    }

    private static ushort Paved(City c, int wx, int wz, ushort natural)
    {
        ulong h = Hashing.Combine(c.Id, wx, 0xC4AB, wz);
        double u = (h >> 40) * (1.0 / (1 << 24));
        if (u < c.Decay * 0.5) return (h & 1) == 0 ? natural : Blocks.Dirt;   // el firme está agrietado
        return c.Style.Plaza;
    }

    // ------------------------------------------------------------------ planta nuclear

    /// <summary>
    /// Dos torres de refrigeración (cascarones de revolución con cintura), el edificio de contención del reactor (cilindro con cúpula, vasija y una puerta hacia -Z),
    /// la sala de turbinas entre ambos y una chimenea alta. Cada cascarón es de 1 a 1,4 bloques de grueso.
    /// </summary>
    private static ushort Nuclear(City c, int qx, int qz, int r, int s)
    {
        ushort P = c.Style.Plaza;

        for (int i = 0; i < 2; i++)   // torres de refrigeración
        {
            int cx = -(s - 9), cz = i == 0 ? s - 10 : -(s - 10);
            double d = Dist(qx, qz, cx, cz);
            if (d <= 8 && r <= 16 && Math.Abs(d - (4 + 2.5 * Math.Pow((r - 9) / 8.0, 2))) <= 0.7) return P;
        }

        double dr = Dist(qx, qz, s - 11, 0);   // contención del reactor
        if (dr <= 7)
        {
            if (r <= 8)
            {
                bool door = qz < 0 && Math.Abs(qx - (s - 11)) <= 1 && r <= 3;
                if (Math.Abs(dr - 5.5) <= 0.7 && !door) return P;
                if (Math.Abs(qx - (s - 11)) <= 1 && Math.Abs(qz) <= 1 && r <= 4) return P;   // vasija del reactor
            }
            else if (r <= 14)
            {
                double rd = Math.Sqrt(Math.Max(0.0, 36 - (r - 8) * (r - 8)));   // cúpula
                if (Math.Abs(dr - rd) <= 0.8) return P;
            }
        }

        if (InBox(qx, qz, -5, -6, 3, 6))   // sala de turbinas
        {
            if (r == 7) return P;
            if (r > 7) return Blocks.Air;
            bool wallX = qx == -5 || qx == 3, wallZ = qz == -6 || qz == 6;
            if (wallX || wallZ)
            {
                if (qz == -6 && Math.Abs(qx + 1) <= 1 && r <= 4) return Blocks.Air;   // portón
                bool window = r >= 3 && r <= 4 && !(wallX && wallZ) && Mod(wallX ? qz : qx, 2) == 0;
                return window ? Blocks.Air : P;
            }
            if ((qz == -2 || qz == 2) && qx >= -3 && qx <= 1 && r <= 2) return P;   // turbinas
            return Blocks.Air;
        }

        int dx = qx - (s - 5), dz = qz + (s - 6);   // chimenea: tubo de 3 x 3 y 20 de alto
        if (Math.Abs(dx) <= 1 && Math.Abs(dz) <= 1)
        {
            if (r <= 2) return P;
            if (r <= 20 && (Math.Abs(dx) == 1 || Math.Abs(dz) == 1)) return P;
        }
        return Blocks.Air;
    }

    // ------------------------------------------------------------------ refinería

    /// <summary>
    /// Campo de depósitos cilíndricos (dos columnas escalonadas, de alturas distintas y con escotilla), torres de destilación con plataformas, una tubería
    /// elevada sobre postes, una caseta de bombas y una chimenea de antorcha.
    /// </summary>
    private static ushort Refinery(City c, int qx, int qz, int r, int s)
    {
        ushort P = c.Style.Plaza;

        for (int col = 0; col < 2; col++)   // depósitos
        {
            int tx = -(s - 6) + 10 * col;
            for (int tz = -(s - 6) + 5 * col; tz <= s - 6; tz += 10)
            {
                double d = Dist(qx, qz, tx, tz);
                if (d > 4.7) continue;
                int hh = 5 + (int)(Hashing.Combine(c.Id, tx, tz, 0x7A7) % 4UL);
                if (r == hh + 1) return d <= 1 ? Blocks.Air : P;   // tapa con escotilla
                if (r <= hh && d >= 3.3) return P;                 // pared (hueco por dentro)
            }
        }

        int cols = s >= 22 ? 3 : 2;   // torres de destilación
        for (int i = 0; i < cols; i++)
        {
            int cx = 2 + 7 * i, cz = s - 9, dx = qx - cx, dz = qz - cz;
            if (Math.Abs(dx) > 2 || Math.Abs(dz) > 2) continue;
            int hh = 14 + (int)(Hashing.Combine(c.Id, cx, cz, 0x7A8) % 5UL);
            if (Math.Abs(dx) <= 1 && Math.Abs(dz) <= 1)
            {
                if (r <= 2 || r == hh + 1) return P;
                if (r <= hh && (Math.Abs(dx) == 1 || Math.Abs(dz) == 1)) return P;
            }
            else if (r == 6 || r == 11) return P;   // plataformas en anillo
        }

        if (qz == -3 && qx >= -1 && qx <= s - 5)   // tubería elevada sobre postes
        {
            if (r <= 2 && Mod(qx, 5) == 0) return P;
            if (r == 3) return P;
        }

        if (InBox(qx, qz, 4, -11, 11, -6))   // caseta de bombas
        {
            if (r == 5) return P;
            if (r > 5) return Blocks.Air;
            bool wallX = qx == 4 || qx == 11, wallZ = qz == -11 || qz == -6;
            if (wallX || wallZ)
            {
                if (qz == -11 && Math.Abs(qx - 7) <= 1 && r <= 3) return Blocks.Air;
                bool window = (r == 2 || r == 3) && !(wallX && wallZ) && Mod(wallX ? qz : qx, 2) == 0;
                return window ? Blocks.Air : P;
            }
            return Blocks.Air;
        }

        int fx = qx - (s - 4), fz = qz + (s - 4);   // chimenea de antorcha
        if (Math.Abs(fx) <= 1 && Math.Abs(fz) <= 1)
        {
            if (r <= 2) return P;
            if (fx == 0 && fz == 0 && r <= 22) return P;
        }
        return Blocks.Air;
    }

    // ------------------------------------------------------------------ aeropuerto

    /// <summary>
    /// Pista a lo largo de X (solo el suelo), terminal con fachada acristalada (huecos entre pilares), torre de control con cabina, dos hangares de techo
    /// abovedado y dos aviones posados en la plataforma (fuselaje, alas, cabina y cola, apoyados en el suelo).
    /// </summary>
    private static ushort Airport(City c, int qx, int qz, int r, int s)
    {
        ushort P = c.Style.Plaza;

        if (InBox(qx, qz, -12, 10, 12, 20))   // terminal
        {
            if (r == 7) return P;
            if (r > 7) return Blocks.Air;
            bool wallX = Math.Abs(qx) == 12, wallZ = qz == 10 || qz == 20;
            if (wallX || wallZ)
            {
                if (qz == 10 && Math.Abs(qx) <= 2 && r <= 4) return Blocks.Air;                       // entrada
                bool front = qz == 10 && r >= 2 && r <= 4 && Mod(qx, 3) != 0;                          // fachada acristalada: huecos entre pilares
                bool window = r >= 3 && r <= 4 && !(wallX && wallZ) && Mod(wallX ? qz : qx, 2) == 0;
                return front || window ? Blocks.Air : P;
            }
            return Blocks.Air;
        }

        int tx = qx - 15, tz = qz - 15;   // torre de control
        if (Math.Abs(tx) <= 1 && Math.Abs(tz) <= 1)
        {
            if (r <= 2) return P;
            if (r <= 13) return Math.Abs(tx) == 1 || Math.Abs(tz) == 1 ? P : Blocks.Air;
        }
        if (Math.Abs(tx) <= 2 && Math.Abs(tz) <= 2 && r >= 14 && r <= 17)   // cabina 5 x 5
        {
            if (r == 14 || r == 17) return P;
            bool edge = Math.Abs(tx) == 2 || Math.Abs(tz) == 2;
            if (!edge) return Blocks.Air;
            bool corner = Math.Abs(tx) == 2 && Math.Abs(tz) == 2;
            return corner || Mod(tx + tz, 2) != 0 ? P : Blocks.Air;   // ventanales: huecos
        }

        for (int i = 0; i < 2; i++)   // hangares (a la izquierda), con portón hacia la pista
        {
            int x0 = -(s - 4), x1 = x0 + 11, z0 = i == 0 ? 10 : -24, z1 = z0 + 14;
            if (!InBox(qx, qz, x0, z0, x1, z1)) continue;
            double t = (qx - (x0 + 5.5)) / 5.5;
            int roof = 9 + (int)Math.Round(2 * (1 - t * t));   // techo abovedado
            if (r == roof) return P;
            if (r > roof) return Blocks.Air;
            if (qx == x0 || qx == x1 || qz == z0 || qz == z1)
            {
                int doorZ = i == 0 ? z0 : z1;
                return qz == doorZ && Math.Abs(qx - (x0 + 5)) <= 3 && r <= 6 ? Blocks.Air : P;
            }
            return Blocks.Air;
        }

        for (int i = 0; i < 2; i++)   // aviones
        {
            int dx = qx - (i == 0 ? -2 : 9), dz = qz - (i == 0 ? -16 : -14);
            if (dx < -7 || dx > 7 || Math.Abs(dz) > 5) continue;
            if (dz == 0 && dx >= -6 && dx <= 6 && r <= 2) return P;                                  // fuselaje
            if (dz == 0 && dx >= -1 && dx <= 2 && r == 3) return P;                                  // cabina
            if (dx >= -1 && dx <= 2 && Math.Abs(dz) >= 1 && r == 1) return P;                        // alas
            if (dz == 0 && dx >= -6 && dx <= -5 && r >= 3 && r <= 5) return P;                       // cola
        }
        return Blocks.Air;
    }

    private static bool AirportPaved(int qx, int qz, int s) =>
        (Math.Abs(qz) <= 3 && Math.Abs(qx) <= s - 3) ||                 // pista
        (Math.Abs(qx) <= 1 && qz >= 3 && qz <= 10) ||                   // calle de rodaje
        InBox(qx, qz, -14, -26, 14, -4) || InBox(qx, qz, -12, 10, 12, 20) ||   // plataforma y terminal
        InBox(qx, qz, -(s - 4), 10, -(s - 4) + 11, 24) || InBox(qx, qz, -(s - 4), -24, -(s - 4) + 11, -10);   // hangares

    // ------------------------------------------------------------------ cosmopuerto

    /// <summary>
    /// Cohete sobre su mesa de lanzamiento en el centro (cuerpo, ojiva y aletas), torre de servicio de cuatro montantes con pasarela al cohete, búnker de control,
    /// edificio de ensamblaje y dos depósitos de combustible.
    /// </summary>
    private static ushort Spaceport(City c, int qx, int qz, int r, int s)
    {
        ushort P = c.Style.Plaza;
        double d0 = Dist(qx, qz, 0, 0);

        if (d0 <= 2.3 && r <= 3) return P;                       // mesa de lanzamiento
        if (d0 <= 2.3 && r >= 4 && r <= 16) return P;            // cuerpo del cohete
        if (r >= 17 && r <= 20 && d0 <= (r == 17 ? 1.8 : r == 18 ? 1.3 : r == 19 ? 0.9 : 0.5)) return P;   // ojiva
        if (r >= 4 && r <= 6 && ((qz == 0 && Math.Abs(qx) == 3) || (qx == 0 && Math.Abs(qz) == 3))) return P;   // aletas

        int gx = qx - 6;   // torre de servicio
        if (Math.Abs(gx) <= 1 && Math.Abs(qz) <= 1)
        {
            bool corner = Math.Abs(gx) == 1 && Math.Abs(qz) == 1;
            if (corner && r <= 24) return P;                                  // montantes
            if (!corner && (r == 8 || r == 14 || r == 20)) return P;          // plataformas
        }
        if (r == 14 && qz == 0 && qx >= 3 && qx <= 4) return P;               // pasarela al cohete

        int bx0 = -(s - 4), bx1 = bx0 + 9;   // búnker de control
        if (InBox(qx, qz, bx0, -5, bx1, 5))
        {
            if (r == 6) return P;
            if (r > 6) return Blocks.Air;
            if (qx == bx0 || qx == bx1 || qz == -5 || qz == 5) return qx == bx1 && Math.Abs(qz) <= 1 && r <= 3 ? Blocks.Air : P;
            return Blocks.Air;
        }

        if (InBox(qx, qz, -8, 14, 8, 24))   // edificio de ensamblaje
        {
            if (r == 17) return P;
            if (r > 17) return Blocks.Air;
            bool wallX = Math.Abs(qx) == 8, wallZ = qz == 14 || qz == 24;
            if (wallX || wallZ)
            {
                if (qz == 14 && Math.Abs(qx) <= 4 && r <= 13) return Blocks.Air;   // portón alto
                bool window = wallX && r >= 8 && r <= 9 && Mod(qz, 3) == 0;
                return window ? Blocks.Air : P;
            }
            return Blocks.Air;
        }

        for (int i = 0; i < 2; i++)   // depósitos de combustible
        {
            double d = Dist(qx, qz, 16, i == 0 ? -12 : -20);
            if (d > 3.7) continue;
            if (r == 8) return d <= 1 ? Blocks.Air : P;
            if (r <= 7 && d >= 2.5) return P;
        }
        return Blocks.Air;
    }

    private static bool SpacePaved(int qx, int qz, int s) =>
        Dist(qx, qz, 0, 0) <= 10 || (Math.Abs(qx) <= 1 && qz <= -10) ||
        InBox(qx, qz, -(s - 4), -5, -(s - 4) + 9, 5) || InBox(qx, qz, -8, 14, 8, 24) ||
        Dist(qx, qz, 16, -12) <= 4.5 || Dist(qx, qz, 16, -20) <= 4.5;
}
