using GF.World.Voxel;

namespace MiniCraft;

/// <summary>
/// Bloque "Fog" de worldsettings.json: niebla de bloques semitransparentes sobre el suelo, con un ruido Perlin que evoluciona (ver FogSystem). No cambia el terreno:
/// se lee siempre del json actual, también al continuar un mundo.
/// </summary>
public class FogSettings
{
    protected virtual string Section => "Fog";

    public bool Enabled { get; set; } = true;
    public float Radius { get; set; } = 64f;          // 16..256: radio alrededor del jugador donde puede haber niebla
    public int CellSize { get; set; } = 2;            // 1..8: lado de cada bloque de niebla
    public float BankHeight { get; set; } = 6f;       // 1..32: altura del banco sobre el suelo
    public float Threshold { get; set; } = 0.55f;     // 0..0.95: por debajo de este valor del ruido no hay niebla (mayor = menos bancos)
    public float MaxAlpha { get; set; } = 0.45f;      // 0..1: opacidad maxima de un bloque
    public float NoiseScale { get; set; } = 48f;      // 8..500: tamaño típico de un banco
    public float EvolutionSpeed { get; set; } = 0.03f; // 0..1: velocidad a la que evoluciona el ruido
    public float FadeSpeed { get; set; } = 0.6f;      // 0.05..5: lo rapido que aparece y desaparece un bloque
    public float Concealment { get; set; } = 0.12f;   // 0..1: cuanto esconde la niebla (0 = no esconde de los enemigos)
    public float MinSight { get; set; } = 0.2f;       // 0..1: fraccion minima del alcance de vision de un enemigo en niebla muy densa
    public float HeightVariation { get; set; } = 0.65f; // 0..1: los bancos densos son mas altos (0 = todos de BankHeight)
    public int MaxColumns { get; set; } = 6000;       // 256..60000: capacidad del pool

    public FogOptions ToOptions() => new()
    {
        Enabled = Enabled, Radius = Radius, CellSize = CellSize, BankHeight = BankHeight, Threshold = Threshold, MaxAlpha = MaxAlpha,
        NoiseScale = NoiseScale, EvolutionSpeed = EvolutionSpeed, FadeSpeed = FadeSpeed, HeightVariation = HeightVariation, Concealment = Concealment, MinSight = MinSight, MaxColumns = MaxColumns,
    };

    public void CopyFrom(FogSettings o)
    {
        Enabled = o.Enabled; Radius = o.Radius; CellSize = o.CellSize; BankHeight = o.BankHeight; Threshold = o.Threshold; MaxAlpha = o.MaxAlpha;
        NoiseScale = o.NoiseScale; EvolutionSpeed = o.EvolutionSpeed; FadeSpeed = o.FadeSpeed; HeightVariation = o.HeightVariation; Concealment = o.Concealment; MinSight = o.MinSight; MaxColumns = o.MaxColumns;
    }

    public void Validate(List<string> warnings)
    {
        var v = new SettingsValidator(Section, warnings);
        Radius = v.F(nameof(Radius), Radius, 16f, 256f);
        CellSize = v.I(nameof(CellSize), CellSize, 1, 8);
        BankHeight = v.F(nameof(BankHeight), BankHeight, 1f, 32f);
        Threshold = v.F(nameof(Threshold), Threshold, 0f, 0.95f);
        MaxAlpha = v.F(nameof(MaxAlpha), MaxAlpha, 0f, 1f);
        NoiseScale = v.F(nameof(NoiseScale), NoiseScale, 8f, 500f);
        EvolutionSpeed = v.F(nameof(EvolutionSpeed), EvolutionSpeed, 0f, 1f);
        FadeSpeed = v.F(nameof(FadeSpeed), FadeSpeed, 0.05f, 5f);
        HeightVariation = v.F(nameof(HeightVariation), HeightVariation, 0f, 1f);
        Concealment = v.F(nameof(Concealment), Concealment, 0f, 1f);
        MinSight = v.F(nameof(MinSight), MinSight, 0f, 1f);
        MaxColumns = v.I(nameof(MaxColumns), MaxColumns, 256, 60000);
    }
}
