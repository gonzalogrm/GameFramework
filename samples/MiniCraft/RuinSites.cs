using GF.Core;

namespace MiniCraft;

/// <summary>
/// Ruinas que no son ciudades: INSTALACIONES TECNOLÓGICAS abandonadas (recinto vallado con un hangar, antena, depósitos y paneles solares), TEMPLOS abandonados (plataforma escalonada, peristilo de columnas, altar y una avenida de columnas rotas) y CAMPOS DE ESTATUAS
/// (un coloso central y un círculo de estatuas, cada una en su estado). Como las ciudades, todo se calcula por columna a partir de (ciudad, coordenadas): sin
/// costuras entre chunks. Coordenadas (rx, rz) relativas al centro; r = 1 es justo sobre el suelo del sitio.
/// </summary>
internal static class RuinSites
{
    private const int Approach = 12;   // largo de la avenida de columnas del templo

    /// <summary>Cuántas variantes tiene cada clase de ruina (la variante sale de la celda, como el tipo de las ciudades).</summary>
    public static int VariantCount(SiteKind site) => 1;

    public static string VariantName(SiteKind site, int variant) => site switch
    {
        SiteKind.Temple => "templo clásico",
        SiteKind.Statues => "coloso y estatuas",
        SiteKind.Tech => "complejo de investigación",
        _ => "",
    };

    /// <summary>Altura máxima de lo construido sobre el suelo (para repartirlo por los chunks verticales).</summary>
    public static int Height(City c) => c.Site == SiteKind.Temple ? 10 : c.Site == SiteKind.Tech ? 15 : 13;

    private static int Mod(int a, int b) => ((a % b) + b) % b;

    private static double U(City c, int a, int b, int salt)
    {
        ulong h = Hashing.Combine(c.Id, a, salt, b);
        return (h >> 40) * (1.0 / (1 << 24));
    }

    /// <summary>El templo se gira (0 a 3 cuartos de vuelta) según la ciudad: la entrada no mira siempre al mismo lado.</summary>
    private static (int X, int Z) Rot(City c, int rx, int rz)
    {
        int rot = (int)(Hashing.Mix((ulong)(uint)c.Id) % 4UL);
        return rot switch { 0 => (rx, rz), 1 => (rz, -rx), 2 => (-rx, -rz), _ => (-rz, rx) };
    }

    /// <summary>Semilado de la plataforma del templo.</summary>
    private static int W(City c) => Math.Max(8, (int)(c.Radius / 1.45) - 8);

    /// <summary>Semilado del templo o de la instalación (para colocar cofres, NPC y enemigos).</summary>
    internal static int HalfSize(City c) => W(c);

    /// <summary>De coordenadas locales del templo o la instalación (ya giradas: la entrada hacia -Z) a coordenadas relativas al centro.</summary>
    internal static (int Rx, int Rz) FromLocal(City c, int qx, int qz)
    {
        int rot = (int)(Hashing.Mix((ulong)(uint)c.Id) % 4UL);
        return rot switch { 0 => (qx, qz), 1 => (-qz, qx), 2 => (-qx, -qz), _ => (qz, -qx) };
    }

    /// <summary>Distancia de la columna al borde de la parte que se nivela (≤ 0: dentro). Hasta 8 bloques fuera el suelo se funde con el natural.</summary>
    public static double Pad(City c, int rx, int rz)
    {
        if (c.Site == SiteKind.Temple || c.Site == SiteKind.Tech)
        {
            var q = Rot(c, rx, rz);
            return Math.Max(Math.Abs(q.X), Math.Abs(q.Z)) - W(c);
        }
        return Math.Sqrt((double)rx * rx + (double)rz * rz) - (c.Radius - 9);
    }

    /// <summary>¿Puede haber bloques construidos sobre el suelo en esta columna?</summary>
    public static bool Active(City c, int rx, int rz, double pad)
    {
        if (pad <= 0) return true;
        if (c.Site != SiteKind.Temple) return false;
        var q = Rot(c, rx, rz);
        int w = W(c);
        return Math.Abs(q.X) <= 3 && q.Z < -(w + 2) && q.Z >= -(w + 2 + Approach);   // la avenida de columnas
    }

    /// <summary>Suelo (r = 0) de la parte nivelada: piedra; en el campo de estatuas el empedrado está agrietado y asoma el suelo natural.</summary>
    public static ushort Surface(City c, int wx, int wz, ushort natural)
    {
        if ((c.Site == SiteKind.Statues || c.Site == SiteKind.Tech) && U(c, wx, wz, 0xC4AB) < c.Decay * 0.5) return U(c, wz, wx, 0xC4AC) < 0.5 ? natural : Blocks.Dirt;
        return c.Style.Plaza;
    }

    /// <summary>Bloque en la fila r. 'profile' (de Profile) dice qué filas de la columna siguen en pie; las plantas crecen donde no hay nada.</summary>
    public static ushort Block(City c, int rx, int rz, int r, int wx, int wy, int wz, bool[]? profile)
    {
        if (c.Site == SiteKind.Temple || c.Site == SiteKind.Tech)
        {
            ushort raw = c.Site == SiteKind.Temple ? TempleBlock(c, rx, rz, r, wx, wz) : TechBlock(c, rx, rz, r, wx, wz);
            if (IsPlant(raw)) return raw;
            if (raw == Blocks.Air) return Blocks.Air;
            return profile != null && r < profile.Length && profile[r] ? Intact(c, rx, rz, r, wx, wz) : Blocks.Air;
        }
        return StatuesBlock(c, rx, rz, r, wx, wz);
    }

    /// <summary>Qué filas de la columna siguen en pie (null en los campos de estatuas, que no necesitan perfil).</summary>
    public static bool[]? Profile(City c, int rx, int rz, int wx, int wz) =>
        c.Site == SiteKind.Statues ? null : SiteProfile(c, rx, rz, wx, wz, 0);

    // ------------------------------------------------------------------ deterioro estructural (templos e instalaciones)

    private static ushort Intact(City c, int rx, int rz, int r, int wx, int wz)
    {
        ushort id = c.Site == SiteKind.Temple ? TempleBlock(c, rx, rz, r, wx, wz) : TechBlock(c, rx, rz, r, wx, wz);
        if (id == c.Style.Plaza || id == Blocks.Air) return id;
        if (id == Blocks.GlassBlue) return Blocks.Air;                          // sin cristales: queda el hueco de la ventana
        if (id == Blocks.LampWarm || IsPlant(id)) return Blocks.Air;
        return id;
    }

    /// <summary>Filas de la plataforma del templo, que se conservan siempre (hace de suelo).</summary>
    private static int BaseRows(City c, int rx, int rz)
    {
        if (c.Site != SiteKind.Temple) return 0;
        var (qx, qz) = Rot(c, rx, rz);
        int d = W(c) - Math.Max(Math.Abs(qx), Math.Abs(qz));
        return d < 0 ? 0 : d < 2 ? 1 : d < 4 ? 2 : 3;
    }

    /// <summary>
    /// Como el de los edificios: un bloque aguanta si el de debajo aguanta y no supera la altura de la columna (se rompe de arriba abajo, con relieve irregular);
    /// un techo, una tapa o unos travesaños (con aire debajo) aguantan solo si un bloque contiguo a su altura aguanta, hasta 2 bloques de una columna en pie.
    /// </summary>
    private static bool[] SiteProfile(City c, int rx, int rz, int wx, int wz, int depth)
    {
        int rows = Height(c);
        var intact = new ushort[rows + 1];
        int top = 0;
        for (int r = 1; r <= rows; r++)
        {
            intact[r] = Intact(c, rx, rz, r, wx, wz);
            if (intact[r] != Blocks.Air) top = r;
        }
        double n = 0.65 * U(c, wx >> 1, wz >> 1, 0xCA9) + 0.35 * U(c, wx, wz, 0xCAA);
        int cap = Math.Max(BaseRows(c, rx, rz), (int)Math.Floor(top * (1 - c.Decay * (0.15 + 0.85 * n))));
        var surv = new bool[rows + 1];
        bool belowIntact = true, belowSurv = true;
        for (int r = 1; r <= rows; r++)
        {
            bool air = intact[r] == Blocks.Air;
            if (!air)
                surv[r] = r > 1 && !belowIntact
                    ? depth <= 1 && SiteHorizontal(c, rx, rz, r, wx, wz, depth)
                    : belowSurv && r <= cap;
            belowIntact = !air;
            belowSurv = surv[r];
        }
        return surv;
    }

    private static bool SiteHorizontal(City c, int rx, int rz, int r, int wx, int wz, int depth)
    {
        bool supported = false;
        for (int k = 0; k < 4 && !supported; k++)
        {
            int dx = k == 0 ? 1 : k == 1 ? -1 : 0, dz = k == 2 ? 1 : k == 3 ? -1 : 0;
            var p = SiteProfile(c, rx + dx, rz + dz, wx + dx, wz + dz, depth + 1);
            supported = r < p.Length && p[r];
        }
        if (!supported) return false;
        double n = 0.7 * U(c, wx >> 1, wz >> 1, 0xCAB + r) + 0.3 * U(c, wx, wz, 0xCAC + r);
        return n < (1 - c.Decay) * 0.45;   // los techos y las tapas casi no se conservan
    }

    private static bool IsPlant(ushort id) => id == Blocks.GrassTuft || id == Blocks.TallGrass || id == Blocks.FlowerRed || id == Blocks.FlowerYellow;

    /// <summary>Hierba, hierba alta o flores que han crecido sobre la piedra (nada en desierto y nieve).</summary>
    private static ushort Plant(City c, int wx, int wz)
    {
        if (c.Style.Name is "desierto" or "nevada") return Blocks.Air;
        ulong h = Hashing.Combine(c.Id, wx, 0xF11, wz);
        float u = (h >> 40) * (1f / (1 << 24));
        if (u < 0.04f) return Blocks.GrassTuft;
        if (u < 0.06f) return Blocks.TallGrass;
        if (u < 0.075f) return ((h >> 4) & 1) == 0 ? Blocks.FlowerRed : Blocks.FlowerYellow;
        return Blocks.Air;
    }

    // ------------------------------------------------------------------ templo

    private static ushort TempleBlock(City c, int rx, int rz, int r, int wx, int wz)
    {
        var st = c.Style;
        var (qx, qz) = Rot(c, rx, rz);
        int w = W(c), d = w - Math.Max(Math.Abs(qx), Math.Abs(qz));

        if (d >= 0)   // plataforma escalonada: tres niveles, de 2 bloques de ancho cada uno
        {
            int ht = d < 2 ? 1 : d < 4 ? 2 : 3;
            if (r <= ht) return st.Plaza;
            if (ht < 3) return r == ht + 1 ? Plant(c, wx, wz) : Blocks.Air;
            return TempleTop(c, qx, qz, r - 3, wx, wz, w);
        }

        // Avenida de columnas hacia la entrada (-Z): pares de columnas rotas.
        if (Math.Abs(qx) == 3 && qz < -(w + 2) && qz >= -(w + 2 + Approach) && Mod(-qz - (w + 2), 4) == 1)
        {
            int ph = 3 + (int)(U(c, qx, qz, 0xC01) * 3);
            return r <= ph ? st.Plaza : Blocks.Air;
        }
        return Blocks.Air;
    }

    /// <summary>El templo sobre la plataforma (rr = 1 es la primera fila sobre ella): peristilo de columnas, muro del fondo con un nicho, altar y techo con un óculo.</summary>
    private static ushort TempleTop(City c, int qx, int qz, int rr, int wx, int wz, int w)
    {
        var st = c.Style;
        int pc = w - 6;                       // el peristilo queda 2 bloques dentro de la plataforma superior (semilado w - 4)
        int ax = Math.Abs(qx), az = Math.Abs(qz);

        if (ax <= 1 && az <= 1)               // altar
        {
            if (rr == 1) return st.Plaza;
            if (rr == 2 && qx == 0 && qz == 0) return st.Plaza;
        }
        if (rr > 6) return Blocks.Air;
        if (rr == 6) return ax <= pc && az <= pc && !(ax <= 1 && az <= 1) ? st.Plaza : Blocks.Air;   // techo con óculo

        bool onX = az == pc && ax <= pc, onZ = ax == pc && az <= pc;
        if (onX || onZ)
        {
            if (qz == pc) return ax <= 1 && rr >= 2 && rr <= 4 ? Blocks.Air : st.Plaza;   // muro del fondo con un nicho
            if (qz == -pc && ax <= 1) return Blocks.Air;                                   // entrada
            bool column = (onX && Mod(qx + pc, 3) == 0) || (onZ && Mod(qz + pc, 3) == 0) || (ax == pc && az == pc);
            return column ? st.Plaza : Blocks.Air;
        }
        if (pc >= 6 && ax == 3 && az == 3) return st.Plaza;                                // columnas interiores
        return rr == 1 ? Plant(c, wx, wz) : Blocks.Air;
    }

    // ------------------------------------------------------------------ instalación tecnológica

    /// <summary>
    /// Recinto cuadrado de semilado S con valla baja y una puerta en el lado -Z. Dentro: un HANGAR al fondo (muros con ventanas, portón, techo plano con tragaluces y
    /// filas de consolas con pantalla), una ANTENA en la esquina de atrás a la izquierda (base, mástil de 12 y travesaños), uno o dos DEPÓSITOS cilíndricos a la derecha
    /// y un campo de PANELES SOLARES a la izquierda. Se gira como el templo. El hormigón es piedra; las pantallas y los paneles, cristal.
    /// </summary>
    private static ushort TechBlock(City c, int rx, int rz, int r, int wx, int wz)
    {
        var st = c.Style;
        var (qx, qz) = Rot(c, rx, rz);
        int s = W(c), m = Math.Max(Math.Abs(qx), Math.Abs(qz));
        if (m > s) return Blocks.Air;
        if (m == s) return qz == -s && Math.Abs(qx) <= 2 ? Blocks.Air : r <= 2 ? st.Plaza : Blocks.Air;   // valla, con puerta

        // antena
        int mx = -(s - 4), mz = s - 4, mdx = qx - mx, mdz = qz - mz;
        if (Math.Abs(mdx) <= 2 && Math.Abs(mdz) <= 2)
        {
            if (Math.Abs(mdx) <= 1 && Math.Abs(mdz) <= 1 && r <= 2) return st.Plaza;
            if (mdx == 0 && mdz == 0 && r >= 3 && r <= 14) return Blocks.Log;
            if ((r == 5 || r == 8 || r == 11) && ((mdz == 0 && Math.Abs(mdx) <= 2) || (mdx == 0 && Math.Abs(mdz) <= 2))) return Blocks.Log;
        }

        // depósitos: uno si el recinto es pequeño, dos si es grande
        for (int i = 0; i < (s >= 20 ? 2 : 1); i++)
        {
            int tx = s - 5, tz = s >= 20 ? (i == 0 ? -s + 6 : -6) : -s / 2;
            int d2 = (qx - tx) * (qx - tx) + (qz - tz) * (qz - tz);
            if (d2 > 12) continue;
            if (r == 8) return d2 <= 1 ? Blocks.Air : st.Plaza;   // tapa con una escotilla
            if (r <= 7 && d2 >= 5) return st.Plaza;               // pared (el interior queda hueco)
        }

        // hangar
        int bw = Math.Max(6, s / 2), z0 = s / 5, z1 = z0 + Math.Max(8, s / 2);
        if (Math.Abs(qx) <= bw && qz >= z0 && qz <= z1)
        {
            if (r == 7) return Mod(qz - z0, 6) == 3 && Math.Abs(qx) <= bw - 2 ? Blocks.GlassBlue : st.Plaza;   // techo con tragaluces
            if (r > 7) return Blocks.Air;
            bool wallX = Math.Abs(qx) == bw, wallZ = qz == z0 || qz == z1;
            if (wallX || wallZ)
            {
                if (qz == z0 && Math.Abs(qx) <= 2 && r <= 4) return Blocks.Air;   // portón
                bool window = r >= 3 && r <= 4 && !(wallX && wallZ) && Mod(wallX ? qz : qx, 2) == 0;
                return window ? Blocks.GlassBlue : st.Plaza;
            }
            if (Mod(qz - z0, 4) == 2 && Math.Abs(qx) < bw - 1 && Mod(qx, 2) == 0)   // consolas con pantalla
            {
                if (r <= 2) return st.Plaza;
                if (r == 3) return Blocks.GlassBlue;
            }
            return r == 1 ? Plant(c, wx, wz) : Blocks.Air;
        }

        // paneles solares: mesas de un bloque con un cristal encima
        if (qx >= -s + 3 && qx <= -4 && qz >= -s + 3 && qz <= -3 && Mod(qx, 2) == 0 && Mod(qz, 3) == 0)
        {
            if (r == 1) return st.Plaza;
            if (r == 2) return Blocks.GlassBlue;
        }
        return r == 1 ? Plant(c, wx, wz) : Blocks.Air;
    }

    // ------------------------------------------------------------------ campo de estatuas

    private static ushort StatuesBlock(City c, int rx, int rz, int r, int wx, int wz)
    {
        var st = c.Style;
        int n = 4 + (int)(Hashing.Mix((ulong)(uint)c.Id ^ 0x5747UL) % 5UL);   // 4 a 8 estatuas
        double padR = c.Radius - 9, ringR = 0.62 * padR, phase = U(c, 0, 0, 0x57F) * 6.2832;

        ushort id = Colossus(c, rx, rz, r, st);
        if (id != Blocks.Air) return id;
        for (int i = 0; i < n; i++)
        {
            double a = phase + (i + (U(c, i, 0, 0x57A) - 0.5) * 0.3) * (2 * Math.PI / n);
            int dx = rx - (int)Math.Round(ringR * Math.Cos(a)), dz = rz - (int)Math.Round(ringR * Math.Sin(a));
            if (Math.Abs(dx) > 5 || Math.Abs(dz) > 1) continue;
            id = Statue(c, i, dx, dz, r, st);
            if (id != Blocks.Air) return id;
        }
        return r == 1 ? Plant(c, wx, wz) : Blocks.Air;
    }

    /// <summary>Estado de una estatua: 0 entera, 1 sin cabeza, 2 rota por la cintura, 3 caída. Cuanto más deterioro, menos enteras.</summary>
    private static int State(City c, int i, double entireBase)
    {
        double u = U(c, i, 1, 0x57B), p0 = entireBase * (1 - c.Decay) + 0.05;
        return u < p0 ? 0 : u < p0 + 0.25 ? 1 : u < p0 + 0.5 ? 2 : 3;
    }

    private static ushort Statue(City c, int i, int dx, int dz, int r, CityStyle st)
    {
        int s = State(c, i, 0.55);
        if (Math.Abs(dx) <= 1 && Math.Abs(dz) <= 1)
        {
            if (r == 1) return st.Plaza;                    // pedestal 3x3
            if (s == 3) return Blocks.Air;                  // caída: solo queda el pedestal
            bool center = dx == 0 && dz == 0;
            if (center && r >= 2 && r <= (s == 2 ? 3 : 4)) return st.Plaza;   // cuerpo
            if (s <= 1 && r == 5 && (center || (dz == 0 && Math.Abs(dx) == 1))) return st.Plaza;   // hombros y brazos
            if (s == 0 && center && r == 6) return st.Plaza;                    // cabeza
            if (s == 2 && Math.Abs(dx) == 1 && Math.Abs(dz) == 1 && r == 2) return st.Plaza;       // cascotes
            return Blocks.Air;
        }
        if (s == 3 && dz == 0 && dx >= 2 && dx <= 5 && r == 1) return st.Plaza;   // la estatua caída yace sobre el suelo
        return Blocks.Air;
    }

    private static ushort Colossus(City c, int dx, int dz, int r, CityStyle st)
    {
        int s = State(c, 99, 0.4);
        int ax = Math.Abs(dx), az = Math.Abs(dz);
        if (ax <= 3 && az <= 3 && r == 1) return st.Plaza;       // pedestal de dos niveles
        if (ax <= 2 && az <= 2 && r == 2) return st.Plaza;
        if (s == 3)                                              // caído: yace hacia +Z (cuerpo y cabeza)
            return dz >= 3 && dz <= 12 && ax <= 1 && r <= 2 ? st.Plaza : Blocks.Air;
        if (ax <= 1 && az <= 1)
        {
            if (r >= 3 && r <= (s == 2 ? 5 : 8)) return st.Plaza;                       // cuerpo
            if (s == 0 && (r == 9 || r == 10)) return st.Plaza;                         // cabeza
            if (s == 0 && r == 11 && ax == 0 && az == 0) return st.Plaza;
        }
        if (s <= 1 && r == 8 && ax <= 3 && az <= 1) return st.Plaza;                    // brazos extendidos
        if (s == 2 && r <= 2 && (ax >= 3 || az >= 3) && ax <= 5 && az <= 5 && U(c, dx, dz, 0x57D) < 0.3) return st.Plaza;   // cascotes
        return Blocks.Air;
    }
}
