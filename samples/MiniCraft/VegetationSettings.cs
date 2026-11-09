namespace MiniCraft;

/// <summary>
/// Bloque "Vegetation" de worldsettings.json: multiplicadores por separado (0..5; 1 = normal, 0 = ninguno) de cada clase de planta, hongo, roca y cristal.
/// Se aplican SIEMPRE con el json actual, también al continuar un mundo (no cambian el terreno), y solo a los chunks que se generen después.
/// Plantas = árboles, arbustos, hierbas, flores y matorrales; los HONGOS, las rocas y los cristales no dependen de Master.
/// </summary>
public sealed class VegetationSettings
{
    public float Master { get; set; } = 1f;       // multiplica a la vez árboles, arbustos, hierbas, hierba alta, flores y matorrales (no hongos, rocas ni cristales)
    public float Trees { get; set; } = 1f;        // árboles (siempre de bloques)
    public float Shrubs { get; set; } = 1f;       // arbustos y matorrales (de bloques y sprites)
    public float SpriteShare { get; set; } = 0.5f;   // 0..1: fracción de arbustos y matorrales que son sprites 2D (0 = todo bloques, 1 = todo sprites)
    public float Grass { get; set; } = 1f;        // hierba baja (sprites)
    public float TallGrass { get; set; } = 1f;    // hierba alta (sprites)
    public float Flowers { get; set; } = 1f;      // flores (sprites)
    public float Bushes { get; set; } = 1f;       // matorral grande decorativo (sprite)
    public float Fungi { get; set; } = 1f;        // setas gigantes de bloques y setas sprite en la superficie
    public float Rocks { get; set; } = 1f;        // rocas sprite y agujas de piedra
    public float Crystals { get; set; } = 1f;     // afloramientos de cristal y agujas de cristal en la superficie
    public float CaveFungi { get; set; } = 1f;    // hongos dentro de las cuevas
    public float CaveCrystals { get; set; } = 1f; // cristales dentro de las cuevas

    public void CopyFrom(VegetationSettings o)
    {
        Master = o.Master; Trees = o.Trees; Shrubs = o.Shrubs; SpriteShare = o.SpriteShare; Grass = o.Grass; TallGrass = o.TallGrass;
        Flowers = o.Flowers; Bushes = o.Bushes; Fungi = o.Fungi; Rocks = o.Rocks; Crystals = o.Crystals; CaveFungi = o.CaveFungi; CaveCrystals = o.CaveCrystals;
    }

    public void Validate(List<string> warnings)
    {
        float F(string name, float v, float min, float max)
        {
            float c = float.IsNaN(v) ? min : Math.Clamp(v, min, max);
            if (c != v) warnings.Add($"Vegetation.{name}: {v} fuera de [{min}, {max}], se usa {c}");
            return c;
        }
        Master = F(nameof(Master), Master, 0f, 5f);
        Trees = F(nameof(Trees), Trees, 0f, 5f);
        Shrubs = F(nameof(Shrubs), Shrubs, 0f, 5f);
        SpriteShare = F(nameof(SpriteShare), SpriteShare, 0f, 1f);
        Grass = F(nameof(Grass), Grass, 0f, 5f);
        TallGrass = F(nameof(TallGrass), TallGrass, 0f, 5f);
        Flowers = F(nameof(Flowers), Flowers, 0f, 5f);
        Bushes = F(nameof(Bushes), Bushes, 0f, 5f);
        Fungi = F(nameof(Fungi), Fungi, 0f, 5f);
        Rocks = F(nameof(Rocks), Rocks, 0f, 5f);
        Crystals = F(nameof(Crystals), Crystals, 0f, 5f);
        CaveFungi = F(nameof(CaveFungi), CaveFungi, 0f, 5f);
        CaveCrystals = F(nameof(CaveCrystals), CaveCrystals, 0f, 5f);
    }
}
