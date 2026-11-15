namespace MiniCraft;

/// <summary>
/// Bloque "Gas" de worldsettings.json: bancos de gas tóxico (tinte rojo) que dañan al jugador y a las criaturas que los respiran. Usa el mismo sistema que la niebla
/// (FogSystem: ruido Perlin y pool de columnas) con otra semilla y valores propios: más raro, más tenue de noche y con daño. Se lee siempre del json actual.
/// </summary>
public sealed class GasSettings : FogSettings
{
    public GasSettings()
    {
        Threshold = 0.72f;      // más raro que la niebla
        MaxAlpha = 0.5f;
        NoiseScale = 36f;
        BankHeight = 5f;
        EvolutionSpeed = 0.02f;
        Concealment = 0.2f;     // también esconde
        MaxColumns = 4000;
    }

    protected override string Section => "Gas";

    public float DamagePerSecond { get; set; } = 2f;     // 0..50: daño por segundo en un banco de densidad 1 (proporcional a la densidad)
    public float DamageThreshold { get; set; } = 0.15f;  // 0..1: densidad minima para que haga daño (y para los eventos de entrar y salir)
    public float GraceSeconds { get; set; } = 10f;       // 0..120: segundos sin daño al abrir el mundo (para no aparecer dentro de un banco)
    public float[] Tint { get; set; } = { 0.9f, 0.22f, 0.18f };   // color RGB (0..1) del gas

    public void CopyFrom(GasSettings o)
    {
        CopyFrom((FogSettings)o);
        DamagePerSecond = o.DamagePerSecond; DamageThreshold = o.DamageThreshold; GraceSeconds = o.GraceSeconds; Tint = (float[])o.Tint.Clone();
    }

    public new void Validate(List<string> warnings)
    {
        base.Validate(warnings);
        float F(string name, float v, float min, float max)
        {
            float c = float.IsNaN(v) ? min : Math.Clamp(v, min, max);
            if (c != v) warnings.Add($"Gas.{name}: {v} fuera de [{min}, {max}], se usa {c}");
            return c;
        }
        DamagePerSecond = F(nameof(DamagePerSecond), DamagePerSecond, 0f, 50f);
        DamageThreshold = F(nameof(DamageThreshold), DamageThreshold, 0.01f, 1f);
        GraceSeconds = F(nameof(GraceSeconds), GraceSeconds, 0f, 120f);
        if (Tint is not { Length: 3 })
        {
            warnings.Add("Gas.Tint debe tener 3 valores (R, G, B); se usa el rojo por defecto");
            Tint = new[] { 0.9f, 0.22f, 0.18f };
        }
        for (int i = 0; i < 3; i++) Tint[i] = Math.Clamp(float.IsNaN(Tint[i]) ? 0f : Tint[i], 0f, 1f);
    }
}
