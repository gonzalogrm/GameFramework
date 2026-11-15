using GF.World.Voxel;

namespace MiniCraft;

/// <summary>
/// Bloque "Smoke" de worldsettings.json: granadas de humo. El humo es un autómata celular 3D (ver GasCloudField): se suelta una cantidad de gas en unos pocos voxels
/// y se expande por difusión hasta disiparse. Esconde de los enemigos. Se lee siempre del json actual.
/// </summary>
public class SmokeSettings
{
    protected virtual string Section => "Smoke";

    public bool Enabled { get; set; } = true;
    public float Amount { get; set; } = 300f;          // 10..5000: gas que suelta una granada (mas = nube mas grande y duradera)
    public float InitialRadius { get; set; } = 1.5f;   // 0..4: radio (bloques) de los voxels iniciales; el gas empieza repartido entre ellos
    public float FullDensity { get; set; } = 10f;      // 1..100: concentracion con opacidad total (menos = nube mas densa)
    public float Spread { get; set; } = 1f;            // 0.1..1: velocidad de expansion
    public float Dissipation { get; set; } = 0.03f;    // 0.001..0.5: fraccion de gas que se pierde por segundo
    public float Buoyancy { get; set; } = 0.15f;       // 0..0.5: cuanto sube el humo
    public int MaxCells { get; set; } = 12000;         // 256..60000: capacidad del pool de celdas de gas
    public float MaxAlpha { get; set; } = 0.6f;        // 0..1: opacidad maxima de una celda
    public float Concealment { get; set; } = 0.35f;    // 0..2: cuanto esconde de los enemigos
    public float MinSight { get; set; } = 0.1f;        // 0..1: fraccion minima del alcance de vision de un enemigo dentro del humo
    public float[] Tint { get; set; } = { 0.62f, 0.62f, 0.64f };   // color RGB (0..1)
    public float DamagePerSecond { get; set; }         // 0..50: daño por segundo a densidad 1 (0 = inofensivo, como el humo); ignora la armadura
    public float DamageThreshold { get; set; } = 0.15f; // 0.01..1: densidad minima para dañar y para los eventos de entrar y salir
    public float FuseSeconds { get; set; } = 1.6f;     // 0.2..10: segundos hasta que la granada suelta el gas
    public float ThrowSpeed { get; set; } = 16f;       // 4..24: velocidad de lanzamiento

    public GasCloudOptions ToOptions() => new()
    {
        FullDensity = FullDensity, Spread = Spread, Dissipation = Dissipation, Buoyancy = Buoyancy, MaxCells = MaxCells, MaxAlpha = MaxAlpha,
        Concealment = Concealment, MinSight = MinSight,
    };

    public void CopyFrom(SmokeSettings o)
    {
        Enabled = o.Enabled; Amount = o.Amount; InitialRadius = o.InitialRadius; FullDensity = o.FullDensity; Spread = o.Spread; Dissipation = o.Dissipation;
        Buoyancy = o.Buoyancy; MaxCells = o.MaxCells; MaxAlpha = o.MaxAlpha; Concealment = o.Concealment; MinSight = o.MinSight; Tint = (float[])o.Tint.Clone();
        DamagePerSecond = o.DamagePerSecond; DamageThreshold = o.DamageThreshold; FuseSeconds = o.FuseSeconds; ThrowSpeed = o.ThrowSpeed;
    }

    public void Validate(List<string> warnings)
    {
        float F(string name, float v, float min, float max)
        {
            float c = float.IsNaN(v) ? min : Math.Clamp(v, min, max);
            if (c != v) warnings.Add($"{Section}.{name}: {v} fuera de [{min}, {max}], se usa {c}");
            return c;
        }
        Amount = F(nameof(Amount), Amount, 10f, 5000f);
        InitialRadius = F(nameof(InitialRadius), InitialRadius, 0f, 4f);
        FullDensity = F(nameof(FullDensity), FullDensity, 1f, 100f);
        Spread = F(nameof(Spread), Spread, 0.1f, 1f);
        Dissipation = F(nameof(Dissipation), Dissipation, 0.001f, 0.5f);
        Buoyancy = F(nameof(Buoyancy), Buoyancy, 0f, 0.5f);
        int cells = Math.Clamp(MaxCells, 256, 60000);
        if (cells != MaxCells) warnings.Add($"{Section}.MaxCells: {MaxCells} fuera de [256, 60000], se usa {cells}");
        MaxCells = cells;
        MaxAlpha = F(nameof(MaxAlpha), MaxAlpha, 0f, 1f);
        Concealment = F(nameof(Concealment), Concealment, 0f, 2f);
        MinSight = F(nameof(MinSight), MinSight, 0f, 1f);
        DamagePerSecond = F(nameof(DamagePerSecond), DamagePerSecond, 0f, 50f);
        DamageThreshold = F(nameof(DamageThreshold), DamageThreshold, 0.01f, 1f);
        FuseSeconds = F(nameof(FuseSeconds), FuseSeconds, 0.2f, 10f);
        ThrowSpeed = F(nameof(ThrowSpeed), ThrowSpeed, 4f, 24f);
        if (Tint is not { Length: 3 })
        {
            warnings.Add($"{Section}.Tint debe tener 3 valores (R, G, B); se usa el color por defecto");
            Tint = new[] { 0.62f, 0.62f, 0.64f };
        }
        for (int i = 0; i < 3; i++) Tint[i] = Math.Clamp(float.IsNaN(Tint[i]) ? 0f : Tint[i], 0f, 1f);
    }
}
