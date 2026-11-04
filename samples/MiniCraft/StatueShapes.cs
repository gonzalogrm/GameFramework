using GF.Core;

namespace MiniCraft;

/// <summary>
/// Figuras de piedra sueltas, en coordenadas locales (dx, dz) respecto a su centro y r = 1 sobre el suelo: estatuas (con su pose y su estado), colosos, agujas
/// retorcidas, tótems de bandas, urnas y mausoleos. P es el bloque de piedra; las bandas oscuras son de tierra y las claras de arena. Ninguna figura imita símbolos
/// de religiones reales (nada de cruces: los brazos nunca se extienden a los dos lados a la vez). Las usan los campos de estatuas (RuinSites, StatueSites) y los
/// campos kilométricos (StatueFieldStage).
/// </summary>
internal static class StatueShapes
{
    /// <summary>
    /// Estatua de unos 6 de alto sobre un pedestal 3 x 3. Estado: 0 entera, 1 sin cabeza, 2 rota por la cintura, 3 caída (yace hacia +dx).
    /// Pose: 0 el portador (un brazo en alto con un orbe), 1 el lancero (una lanza hincada a su lado), 2 el escudero (escudo ancho y cresta).
    /// </summary>
    public static ushort Statue(int s, int dx, int dz, int r, ushort P, int pose = 0)
    {
        if (Math.Abs(dx) <= 1 && Math.Abs(dz) <= 1)
        {
            if (r == 1) return P;                                                              // pedestal
            if (s == 3) return Blocks.Air;                                                      // caída: solo queda el pedestal
            bool center = dx == 0 && dz == 0;
            if (center && r >= 2 && r <= (s == 2 ? 3 : 4)) return P;                            // cuerpo
            if (s == 2) return Math.Abs(dx) == 1 && Math.Abs(dz) == 1 && r == 2 ? P : Blocks.Air;   // cascotes
            switch (pose)
            {
                case 0:   // el portador
                    if (center && r == 5) return P;
                    if (s == 0 && center && r == 6) return P;                                   // cabeza
                    if (dx == 1 && dz == 0 && r >= 5 && r <= 7) return P;                       // brazo en alto con el orbe
                    break;
                case 1:   // el lancero
                    if (center && r == 5) return P;
                    if (s == 0 && center && r == 6) return P;
                    if (dx == 1 && dz == 0 && r >= 2 && r <= 8) return P;                       // lanza hincada
                    break;
                default:  // el escudero
                    if (Math.Abs(dx) == 1 && dz == 0 && r >= 3 && r <= 4) return P;             // escudo ancho
                    if (center && r == 5) return P;
                    if (s == 0 && center && (r == 6 || r == 7)) return P;                       // cabeza y cresta
                    break;
            }
            return Blocks.Air;
        }
        return s == 3 && dz == 0 && dx >= 2 && dx <= 5 && r == 1 ? P : Blocks.Air;              // la estatua caída yace sobre el suelo
    }

    /// <summary>Coloso de 11 de alto con pedestal de dos niveles, un brazo caído y el otro alzado con un orbe; caído, yace hacia +dz. 'seed' reparte los cascotes.</summary>
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
        if (s <= 1 && dz == 0)
        {
            if (dx == -2 && r >= 4 && r <= 7) return P;      // brazo caído
            if (dx == 2 && r >= 4 && r <= 10) return P;      // brazo alzado, con el orbe arriba
        }
        if (s == 2 && r <= 2 && (ax >= 3 || az >= 3) && ax <= 5 && az <= 5)
        {
            ulong h = Hashing.Combine(seed, dx, 0x57D, dz);
            if ((h >> 40) * (1.0 / (1 << 24)) < 0.3) return P;
        }
        return Blocks.Air;
    }

    /// <summary>Aguja retorcida: base 3 x 3 y una columna de 2 x 2 a la que le falta una celda, que gira con la altura (hasta r = 10).</summary>
    public static ushort Spire(int dx, int dz, int r, ushort P)
    {
        if (Math.Abs(dx) <= 1 && Math.Abs(dz) <= 1 && r == 1) return P;
        if (r < 2 || r > 10 || dx < 0 || dx > 1 || dz < 0 || dz > 1) return Blocks.Air;
        int missing = (r / 2) % 4;
        int mx = missing == 0 || missing == 3 ? 1 : 0, mz = missing < 2 ? 1 : 0;   // (1,1), (0,1), (0,0), (1,0)
        return dx == mx && dz == mz ? Blocks.Air : P;
    }

    /// <summary>Tótem de bandas: una columna de 5 con dos bandas oscuras y un remate claro.</summary>
    public static ushort Totem(int dx, int dz, int r, ushort P)
    {
        if (dx != 0 || dz != 0 || r < 1 || r > 5) return Blocks.Air;
        return r == 2 || r == 4 ? Blocks.Dirt : r == 5 ? Blocks.Sand : P;
    }

    /// <summary>Urna: pedestal de piedra con la urna (tierra) encima.</summary>
    public static ushort Urn(int dx, int dz, int r, ushort P) => dx == 0 && dz == 0 ? (r == 1 ? P : r == 2 ? Blocks.Dirt : Blocks.Air) : Blocks.Air;

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
