using GF.Core;

namespace MiniCraft;

/// <summary>
/// Figuras de piedra sueltas, en coordenadas locales (dx, dz) respecto a su centro y r = 1 sobre el suelo: estatuas (con su estado), colosos, obeliscos, urnas,
/// estelas y mausoleos. P es el bloque de piedra. Las usan los campos de estatuas (RuinSites, StatueSites) y los campos kilométricos (StatueFieldStage).
/// </summary>
internal static class StatueShapes
{
    /// <summary>Estatua de 6 de alto sobre un pedestal 3 x 3. Estado: 0 entera, 1 sin cabeza, 2 rota por la cintura, 3 caída (yace hacia +dx).</summary>
    public static ushort Statue(int s, int dx, int dz, int r, ushort P)
    {
        if (Math.Abs(dx) <= 1 && Math.Abs(dz) <= 1)
        {
            if (r == 1) return P;                                                              // pedestal
            if (s == 3) return Blocks.Air;                                                      // caída: solo queda el pedestal
            bool center = dx == 0 && dz == 0;
            if (center && r >= 2 && r <= (s == 2 ? 3 : 4)) return P;                            // cuerpo
            if (s <= 1 && r == 5 && (center || (dz == 0 && Math.Abs(dx) == 1))) return P;       // hombros y brazos
            if (s == 0 && center && r == 6) return P;                                           // cabeza
            if (s == 2 && Math.Abs(dx) == 1 && Math.Abs(dz) == 1 && r == 2) return P;           // cascotes
            return Blocks.Air;
        }
        return s == 3 && dz == 0 && dx >= 2 && dx <= 5 && r == 1 ? P : Blocks.Air;              // la estatua caída yace sobre el suelo
    }

    /// <summary>Coloso de 11 de alto con pedestal de dos niveles y brazos extendidos; caído, yace hacia +dz. 'seed' reparte los cascotes.</summary>
    public static ushort Colossus(int s, int dx, int dz, int r, ushort P, int seed)
    {
        int ax = Math.Abs(dx), az = Math.Abs(dz);
        if (ax <= 3 && az <= 3 && r == 1) return P;
        if (ax <= 2 && az <= 2 && r == 2) return P;
        if (s == 3) return dz >= 3 && dz <= 12 && ax <= 1 && r <= 2 ? P : Blocks.Air;
        if (ax <= 1 && az <= 1)
        {
            if (r >= 3 && r <= (s == 2 ? 5 : 8)) return P;
            if (s == 0 && (r == 9 || r == 10)) return P;
            if (s == 0 && r == 11 && ax == 0 && az == 0) return P;
        }
        if (s <= 1 && r == 8 && ax <= 3 && az <= 1) return P;
        if (s == 2 && r <= 2 && (ax >= 3 || az >= 3) && ax <= 5 && az <= 5)
        {
            ulong h = Hashing.Combine(seed, dx, 0x57D, dz);
            if ((h >> 40) * (1.0 / (1 << 24)) < 0.3) return P;
        }
        return Blocks.Air;
    }

    /// <summary>Obelisco: base 3 x 3 y aguja de 1 x 1 hasta r = 10.</summary>
    public static ushort Obelisk(int dx, int dz, int r, ushort P)
    {
        if (Math.Abs(dx) <= 1 && Math.Abs(dz) <= 1 && r == 1) return P;
        return dx == 0 && dz == 0 && r >= 2 && r <= 10 ? P : Blocks.Air;
    }

    /// <summary>Urna: pedestal de piedra con la urna (tierra) encima.</summary>
    public static ushort Urn(int dx, int dz, int r, ushort P) => dx == 0 && dz == 0 ? (r == 1 ? P : r == 2 ? Blocks.Dirt : Blocks.Air) : Blocks.Air;

    /// <summary>Estela: losa de 3 de ancho y 3 de alto con un remate.</summary>
    public static ushort Stele(int dx, int dz, int r, ushort P)
    {
        if (dz == 0 && Math.Abs(dx) <= 1 && r <= 3) return P;
        return dx == 0 && dz == 0 && r == 4 ? P : Blocks.Air;
    }

    /// <summary>Mausoleo: caja de 5 x 5 con tejado a 4 de alto y una puerta hacia -dz.</summary>
    public static ushort Mausoleum(int dx, int dz, int r, ushort P)
    {
        int ax = Math.Abs(dx), az = Math.Abs(dz);
        if (ax > 2 || az > 2) return Blocks.Air;
        if (r == 4) return P;
        if (r > 4 || (ax != 2 && az != 2)) return Blocks.Air;
        return dz == -2 && dx == 0 && r <= 2 ? Blocks.Air : P;
    }
}
