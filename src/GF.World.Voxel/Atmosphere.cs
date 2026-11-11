namespace GF.World.Voxel;

/// <summary>
/// Parámetros de la atmósfera del mundo actual: nubes, estrellas y degradado del cielo (SkyRenderer los lee cada frame). El juego los fija al abrir cada planeta;
/// así cada mundo tiene su propia cobertura de nubes, su viento y su cielo.
/// </summary>
public static class Atmosphere
{
    /// <summary>Fracción de cielo cubierta de nubes (0..1).</summary>
    public static float CloudCoverage { get; set; } = 0.45f;
    /// <summary>Altura (en bloques del mundo) de la capa de nubes.</summary>
    public static float CloudAltitude { get; set; } = 150f;
    /// <summary>Multiplicador de la velocidad de las nubes (1 = unos 3 bloques por segundo).</summary>
    public static float CloudSpeed { get; set; } = 1f;
    /// <summary>Dirección del viento, en radianes sobre el plano X/Z.</summary>
    public static float WindAngle { get; set; }
    /// <summary>Estrellas de noche.</summary>
    public static bool Stars { get; set; } = true;
    /// <summary>Degradado del cielo (cenit más oscuro que el horizonte). Sin él, el cielo es plano.</summary>
    public static bool SkyGradient { get; set; } = true;
    /// <summary>Semilla de la distribución de estrellas (distinta en cada planeta).</summary>
    public static int StarSeed { get; set; }
}
