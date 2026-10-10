using Microsoft.Xna.Framework;

namespace GF.World.Voxel;

/// <summary>
/// Ciclo día/noche: una hora del día (0..1) que avanza sola y, para cada hora, el color de la luz de cielo (multiplicador RGB), la luz
/// ambiente mínima y el color del horizonte (fondo y niebla). Es solo la curva: no toca el mundo. El renderer aplica SkyLight y
/// AmbientLight como parámetros del shader en cada frame, así que cambiar la hora NO remalla nada.
///   0 = medianoche, 0.25 = amanecer, 0.5 = mediodía, 0.75 = ocaso.
/// </summary>
public sealed class DayNightCycle
{
    private readonly record struct Key(float T, Vector3 Sky, Vector3 Ambient, Color Horizon);

    private static readonly Vector3 NightSky = new(0.07f, 0.09f, 0.20f), NightAmbient = new(0.035f, 0.04f, 0.07f);
    private static readonly Vector3 DaySky = Vector3.One, DayAmbient = new(0.12f);
    private static readonly Color NightHorizon = new(8, 12, 28), DayHorizon = new(135, 180, 235);

    private static readonly Key[] Keys =
    {
        new(0.00f, NightSky, NightAmbient, NightHorizon),
        new(0.20f, NightSky, NightAmbient, NightHorizon),
        new(0.27f, new Vector3(0.95f, 0.60f, 0.45f), new Vector3(0.09f, 0.08f, 0.08f), new Color(235, 150, 110)),   // amanecer
        new(0.35f, DaySky, DayAmbient, DayHorizon),
        new(0.65f, DaySky, DayAmbient, DayHorizon),
        new(0.73f, new Vector3(0.95f, 0.50f, 0.40f), new Vector3(0.09f, 0.08f, 0.08f), new Color(225, 115, 85)),    // ocaso
        new(0.80f, NightSky, NightAmbient, NightHorizon),
        new(1.00f, NightSky, NightAmbient, NightHorizon),
    };

    public DayNightCycle() => Evaluate();

    /// <summary>Duración de un día completo, en segundos reales.</summary>
    public double DayLengthSeconds { get; set; } = 600;
    /// <summary>Hora del día, 0..1.</summary>
    public double Time { get; set; } = 0.38;
    /// <summary>false = la hora no avanza (se puede fijar Time a mano).</summary>
    public bool Running { get; set; } = true;

    /// <summary>Multiplicador RGB de la luz de cielo: (1,1,1) de día, azul tenue de noche, anaranjado al amanecer y al ocaso.</summary>
    public Vector3 SkyLight { get; private set; }
    /// <summary>Brillo mínimo por canal (0..1).</summary>
    public Vector3 Ambient { get; private set; }
    /// <summary>Color del cielo en el horizonte: úsalo para el fondo y la niebla.</summary>
    public Color Horizon { get; private set; }
    /// <summary>0 = noche cerrada, 1 = día pleno.</summary>
    public float Daylight { get; private set; }

    /// <summary>Hora del reloj como "HH:mm".</summary>
    public string Clock
    {
        get
        {
            int minutes = (int)(Time * 24 * 60) % (24 * 60);
            return $"{minutes / 60:00}:{minutes % 60:00}";
        }
    }

    public void Update(double dt)
    {
        if (Running && DayLengthSeconds > 0)
        {
            Time += dt / DayLengthSeconds;
            Time -= Math.Floor(Time);
        }
        Evaluate();
    }

    public void Evaluate()
    {
        float t = (float)(Time - Math.Floor(Time));
        int i = 0;
        while (i < Keys.Length - 2 && t >= Keys[i + 1].T) i++;
        var a = Keys[i]; var b = Keys[i + 1];
        float f = Math.Clamp((t - a.T) / (b.T - a.T), 0f, 1f);
        f = f * f * (3f - 2f * f);

        SkyLight = Vector3.Lerp(a.Sky, b.Sky, f);
        Ambient = Vector3.Lerp(a.Ambient, b.Ambient, f);
        Horizon = Color.Lerp(a.Horizon, b.Horizon, f);
        Daylight = 0.2126f * SkyLight.X + 0.7152f * SkyLight.Y + 0.0722f * SkyLight.Z;
    }
}
