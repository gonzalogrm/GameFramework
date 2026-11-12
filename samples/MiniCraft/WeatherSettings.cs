namespace MiniCraft;

/// <summary>
/// Bloque "Weather" de worldsettings.json: nubes, estrellas y degradado del cielo. Se leen SIEMPRE del json actual (también al continuar un mundo).
/// Sin CloudCoverage (-1) cada planeta tiene su propia cobertura de nubes y su viento, que salen de su semilla.
/// </summary>
public sealed class WeatherSettings
{
    public float CloudCoverage { get; set; } = -1f;    // 0..1 (0 = cielo despejado); -1 = según el planeta
    public float CloudSpeed { get; set; } = 1f;        // 0..5: multiplicador de la velocidad de las nubes (1 = unos 3 bloques por segundo)
    public float CloudAltitude { get; set; } = 150f;   // 60..400: altura de las nubes, en bloques
    public bool Stars { get; set; } = true;            // estrellas de noche
    public bool SkyGradient { get; set; } = true;      // degradado del cielo (cenit más oscuro que el horizonte)
    public float GroundFog { get; set; } = 1f;         // 0..3: niebla de suelo en bancos (0 = ninguna)

    public void Validate(List<string> warnings)
    {
        float F(string name, float v, float min, float max)
        {
            float c = float.IsNaN(v) ? min : Math.Clamp(v, min, max);
            if (c != v) warnings.Add($"Weather.{name}: {v} fuera de [{min}, {max}], se usa {c}");
            return c;
        }
        if (CloudCoverage >= 0f) CloudCoverage = F(nameof(CloudCoverage), CloudCoverage, 0f, 1f); else CloudCoverage = -1f;
        CloudSpeed = F(nameof(CloudSpeed), CloudSpeed, 0f, 5f);
        CloudAltitude = F(nameof(CloudAltitude), CloudAltitude, 60f, 400f);
        GroundFog = F(nameof(GroundFog), GroundFog, 0f, 3f);
    }
}
