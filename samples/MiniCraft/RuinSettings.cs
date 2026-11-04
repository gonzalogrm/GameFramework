namespace MiniCraft;

/// <summary>
/// Ajustes de las ruinas y de los campos de estatuas (bloque "Ruins" de worldsettings.json). La frecuencia, el reparto por clases y variantes, el tamaño, el
/// deterioro y los campos kilométricos cambian CÓMO se genera el mundo (se guardan con él); los enemigos, los cofres y los NPC solo lo que aparece.
/// </summary>
public sealed class RuinSettings
{
    // ---- generación
    public float Chance { get; set; } = 0.45f;        // probabilidad de ruina en una celda sin ciudad (en una celda con ciudad, el 35 % de esto) (0..1)
    public int CityWeight { get; set; } = 40;         // peso de cada clase: ciudad abandonada, templo, campo de estatuas e instalación tecnológica (0 = nunca)
    public int TempleWeight { get; set; } = 22;
    public int StatuesWeight { get; set; } = 18;
    public int TechWeight { get; set; } = 20;
    public int[] TempleVariants { get; set; } = { 1, 1, 1, 1, 1 };   // pesos por variante: templo de columnas, observatorio, academia, faro, escalinata de monolitos (0 = nunca)
    public int[] StatuesVariants { get; set; } = { 1, 1, 1 };         // coloso y estatuas, avenida de reyes, columbario
    public int[] TechVariants { get; set; } = { 1, 1, 1, 1, 1 };     // complejo de investigación, planta nuclear, refinería, aeropuerto, cosmopuerto
    public float MinDecay { get; set; } = 0.25f;      // deterioro de las ruinas: de MinDecay a MaxDecay (0,05 a 0,95)
    public float MaxDecay { get; set; } = 0.9f;
    public float SizeFactor { get; set; } = 1f;       // multiplica el radio de las ruinas (1 a 1,5: los diseños están pensados para su tamaño mínimo)
    public float FieldChance { get; set; } = 0.55f;   // probabilidad de campo kilométrico por celda de 4096 bloques (0..1)
    public int FieldMinRadius { get; set; } = 350;    // radio de los campos kilométricos (100 a 1500)
    public int FieldMaxRadius { get; set; } = 750;
    public float FieldDensity { get; set; } = 0.65f;  // probabilidad máxima de figura por punto de la malla de 16 x 16 (0..1)

    // ---- contenido
    public float EnemyFactor { get; set; } = 1f;      // multiplica los enemigos de las ruinas y los campos (0 = ninguno, hasta 3)
    public bool Chests { get; set; } = true;          // cofres con tesoro
    public bool Npcs { get; set; } = true;            // NPC especiales
    public float FieldFindChance { get; set; } = 0.5f;   // probabilidad máxima de hallazgo por celda de 192 x 192 de un campo (0..1)

    /// <summary>Pasa lo que afecta a la generación al planificador de ciudades y ruinas.</summary>
    public void Apply(CityOptions o)
    {
        o.RuinChance = Chance;
        o.RuinWeights = new[] { CityWeight, TempleWeight, StatuesWeight, TechWeight };
        o.TempleVariants = TempleVariants;
        o.StatuesVariants = StatuesVariants;
        o.TechVariants = TechVariants;
        o.RuinMinDecay = MinDecay;
        o.RuinMaxDecay = MaxDecay;
        o.RuinSizeFactor = SizeFactor;
    }

    /// <summary>Copia lo que no afecta a la generación (se puede cambiar al volver a abrir un mundo guardado).</summary>
    public void ApplyView(RuinSettings other)
    {
        EnemyFactor = other.EnemyFactor;
        Chests = other.Chests;
        Npcs = other.Npcs;
        FieldFindChance = other.FieldFindChance;
    }

    public void Validate(List<string> warnings)
    {
        float F(string name, float v, float min, float max)
        {
            float c = float.IsNaN(v) ? min : Math.Clamp(v, min, max);
            if (c != v) warnings.Add($"Ruins.{name}: {v} fuera de [{min}, {max}], se usa {c}");
            return c;
        }
        int I(string name, int v, int min, int max)
        {
            int c = Math.Clamp(v, min, max);
            if (c != v) warnings.Add($"Ruins.{name}: {v} fuera de [{min}, {max}], se usa {c}");
            return c;
        }
        int[] W(string name, int[]? v, int count)
        {
            var r = new int[count];
            for (int i = 0; i < count; i++) r[i] = v != null && i < v.Length ? Math.Max(0, v[i]) : 1;
            if (v == null || v.Length != count) warnings.Add($"Ruins.{name}: debe tener {count} pesos; se completan con 1 o se recortan");
            return r;
        }

        Chance = F(nameof(Chance), Chance, 0f, 1f);
        CityWeight = I(nameof(CityWeight), CityWeight, 0, 1000);
        TempleWeight = I(nameof(TempleWeight), TempleWeight, 0, 1000);
        StatuesWeight = I(nameof(StatuesWeight), StatuesWeight, 0, 1000);
        TechWeight = I(nameof(TechWeight), TechWeight, 0, 1000);
        if (CityWeight + TempleWeight + StatuesWeight + TechWeight == 0) { CityWeight = 40; warnings.Add("Ruins: todos los pesos de clase son 0; se usa CityWeight 40"); }
        TempleVariants = W(nameof(TempleVariants), TempleVariants, 5);
        StatuesVariants = W(nameof(StatuesVariants), StatuesVariants, 3);
        TechVariants = W(nameof(TechVariants), TechVariants, 5);
        MinDecay = F(nameof(MinDecay), MinDecay, 0.05f, 0.95f);
        MaxDecay = F(nameof(MaxDecay), MaxDecay, 0.05f, 0.95f);
        if (MaxDecay < MinDecay) { MaxDecay = MinDecay; warnings.Add("Ruins.MaxDecay no puede ser menor que MinDecay; se iguala"); }
        SizeFactor = F(nameof(SizeFactor), SizeFactor, 1f, 1.5f);
        FieldChance = F(nameof(FieldChance), FieldChance, 0f, 1f);
        FieldMinRadius = I(nameof(FieldMinRadius), FieldMinRadius, 100, 1500);
        FieldMaxRadius = I(nameof(FieldMaxRadius), FieldMaxRadius, 100, 1500);
        if (FieldMaxRadius < FieldMinRadius) { FieldMaxRadius = FieldMinRadius; warnings.Add("Ruins.FieldMaxRadius no puede ser menor que FieldMinRadius; se iguala"); }
        FieldDensity = F(nameof(FieldDensity), FieldDensity, 0f, 1f);
        EnemyFactor = F(nameof(EnemyFactor), EnemyFactor, 0f, 3f);
        FieldFindChance = F(nameof(FieldFindChance), FieldFindChance, 0f, 1f);
    }
}
