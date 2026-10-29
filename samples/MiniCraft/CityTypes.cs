using GF.Core;

namespace MiniCraft;

/// <summary>
/// Tipo de ciudad: tamaño, murallas, irregularidad del borde, plaza, farolas y qué se construye en las parcelas (porcentajes de parcelas y de edificios).
/// FieldPct: huertos, solo en la periferia. Los porcentajes de parcela (Park + Garden + Statue + Well + Field) son sobre 100; el resto son edificios, y de ellos Hall + Tower + Workshop son sobre 100 (el resto, casas).
/// </summary>
public sealed record CityType(string Name, int Weight, double RadiusScale, int WallMode, double Irregularity, int PlazaHalf, int LampSpacing, int FloorsCap,
    int ParkPct, int GardenPct, int StatuePct, int WellPct, int HallPct, int TowerPct, int WorkshopPct, int FieldPct);

public static class CityTypes
{
    /// <summary>Mayor escala de radio y mayor irregularidad de la tabla: el planificador reserva ese espacio alrededor del centro.</summary>
    public const double MaxRadiusScale = 1.25, MaxIrregularity = 0.30;

    // WallMode: 0 nunca, 1 según el estilo del bioma, 2 siempre.   LampSpacing: bloques entre farolas (0 = sin farolas en las calles).
    public static readonly CityType[] All =
    {
        new("aldea",     30, 0.55, 0, 0.30,  6,  0,  2, 18, 14, 0, 8,   0,  0, 15, 30),
        new("pueblo",    35, 0.80, 1, 0.22,  8, 28,  3, 14, 10, 3, 5,   5,  5, 25, 22),
        new("ciudad",    22, 1.00, 1, 0.15, 10, 20, 99, 10,  8, 5, 4,  12, 12, 20, 12),
        new("capital",    5, 1.25, 2, 0.12, 14, 16, 99,  6,  8, 8, 2,  22, 18, 18, 8),
        new("fortaleza",  8, 0.70, 2, 0.10,  8,  0,  3,  0,  4, 0, 6,  15, 35, 20, 0),
    };

    public static CityType Pick(SplitMixRandom rng)
    {
        int total = 0;
        foreach (var t in All) total += t.Weight;
        int roll = rng.Next(total);
        foreach (var t in All)
        {
            roll -= t.Weight;
            if (roll < 0) return t;
        }
        return All[1];
    }
}
