namespace MiniCraft;

/// <summary>
/// Ajustes de las ciudades (bloque "Cities" de worldsettings.json). Chance, radios, desnivel y plantas cambian CÓMO se genera el mundo (se guardan con él);
/// aldeanos, guardas y NPC especiales solo cuántos habitantes aparecen.
/// </summary>
public sealed class CitySettings
{
    public float Chance { get; set; } = 0.8f;       // probabilidad de intentar una ciudad por celda de 1024 bloques (0..1)
    public int MinRadius { get; set; } = 52;        // radio mínimo de una ciudad, en bloques (30..200)
    public int MaxRadius { get; set; } = 80;        // radio máximo (no menor que el mínimo; la celda mide 1024)
    public int MaxSlope { get; set; } = 10;          // desnivel máximo del terreno bajo la ciudad: mayor = más ciudades en terreno movido (0..30)
    public float SizeFactor { get; set; } = 1.6f;   // multiplica el radio de las CIUDADES (no el de los poblados) (0.5..3)
    public int MaxFloors { get; set; } = 4;         // plantas máximas de los edificios; las torres llegan a esta altura (1..8)
    public int Villagers { get; set; } = 6;         // aldeanos por ciudad (0 = ciudades sin habitantes)
    public int Guards { get; set; } = -1;           // guardas por ciudad (-1 = según su tamaño, 0 = ninguno)
    public bool SpecialNpcs { get; set; } = true;   // mercader, alcalde y herreros

    public CityOptions ToOptions() => new()
    {
        Chance = Chance, MinRadius = MinRadius, MaxRadius = MaxRadius, MaxSlope = MaxSlope, MaxFloors = MaxFloors, SizeFactor = SizeFactor,
    };

    public void Validate(List<string> warnings)
    {
        var v = new SettingsValidator("Cities", warnings);
        Chance = v.F(nameof(Chance), Chance, 0f, 1f);
        MinRadius = v.I(nameof(MinRadius), MinRadius, 30, 200);
        MaxRadius = v.I(nameof(MaxRadius), MaxRadius, 30, 300);
        if (MaxRadius < MinRadius) { MaxRadius = MinRadius; v.Warn("MaxRadius no puede ser menor que MinRadius; se iguala"); }
        SizeFactor = float.IsNaN(SizeFactor) ? 1.6f : Math.Clamp(SizeFactor, 0.5f, 3f);
        int fit = (int)(470 / (1.625 * SizeFactor));   // la ciudad más grande tiene que caber en su celda de 1024
        if (MaxRadius > fit)
        {
            v.Warn($"MaxRadius: {MaxRadius} no cabe con SizeFactor {SizeFactor}, se usa {Math.Max(30, fit)}");
            MaxRadius = Math.Max(30, fit);
            if (MinRadius > MaxRadius) MinRadius = MaxRadius;
        }
        MaxSlope = v.I(nameof(MaxSlope), MaxSlope, 0, 30);
        MaxFloors = v.I(nameof(MaxFloors), MaxFloors, 1, 8);
        Villagers = v.I(nameof(Villagers), Villagers, 0, 50);
        Guards = v.I(nameof(Guards), Guards, -1, 20);
    }
}
