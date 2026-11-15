namespace MiniCraft;

/// <summary>Bloque "ToxicGrenade": una nube de gas rojo (autómata celular 3D) que daña a quien la respira. Mismas claves que "Smoke".</summary>
public sealed class ToxicGrenadeSettings : SmokeSettings
{
    public ToxicGrenadeSettings()
    {
        Amount = 250f;
        Dissipation = 0.035f;
        MaxAlpha = 0.55f;
        Concealment = 0.2f;
        Tint = new[] { 0.9f, 0.22f, 0.18f };
        DamagePerSecond = 3f;
    }

    protected override string Section => "ToxicGrenade";
}

/// <summary>Bloque "Spores": nube de esporas verdes, más lenta, duradera y de daño leve. Mismas claves que "Smoke".</summary>
public sealed class SporeSettings : SmokeSettings
{
    public SporeSettings()
    {
        Amount = 400f;
        Spread = 0.8f;
        Dissipation = 0.015f;
        Buoyancy = 0.05f;
        MaxAlpha = 0.5f;
        Concealment = 0.15f;
        Tint = new[] { 0.55f, 0.8f, 0.3f };
        DamagePerSecond = 0.7f;
        SlowFactor = 0.55f;   // las esporas ralentizan
    }

    protected override string Section => "Spores";
}
