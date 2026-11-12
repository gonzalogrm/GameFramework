using GF.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GF.World.Voxel;

/// <summary>
/// Pasa al efecto de los chunks (ChunkFade.fx) lo que necesita la niebla de suelo: la textura de ruido de las nubes, la posición de la cámara en unidades del ruido
/// (con la deriva del viento) y la intensidad. Se llama una vez por frame ANTES de dibujar los chunks. Si el efecto no tiene esos parámetros, no hace nada.
/// </summary>
public static class FogBinder
{
    private static Effect? _target;
    private static EffectParameter? _noise, _origin, _fog;
    private static Texture2D? _dummy;

    /// <summary>El efecto ChunkFade cargado (null si no hay).</summary>
    public static Effect? Target
    {
        get => _target;
        set
        {
            _target = value;
            _noise = value?.Parameters["FogNoise"];
            _origin = value?.Parameters["FogOrigin"];
            _fog = value?.Parameters["GroundFog"];
        }
    }

    public static void Update(Vec3d camera, double time)
    {
        var fx = _target;
        if (fx == null || _noise == null) return;

        var tex = Atmosphere.FogNoise;
        if (tex == null || tex.IsDisposed)   // aún sin ruido: una textura negra (sin niebla)
        {
            if (_dummy == null || _dummy.IsDisposed || !ReferenceEquals(_dummy.GraphicsDevice, fx.GraphicsDevice))
            {
                _dummy = new Texture2D(fx.GraphicsDevice, 1, 1);
                _dummy.SetData(new[] { Color.Black });
            }
            tex = _dummy;
        }
        _noise.SetValue(tex);

        double drift = 1.5 * Atmosphere.CloudSpeed * time;   // la niebla avanza con el viento, más despacio que las nubes
        double u = (camera.X - Math.Cos(Atmosphere.WindAngle) * drift) / Atmosphere.FogPeriod;
        double v = (camera.Z - Math.Sin(Atmosphere.WindAngle) * drift) / Atmosphere.FogPeriod;
        u -= Math.Floor(u);
        v -= Math.Floor(v);
        _origin?.SetValue(new Vector2((float)u, (float)v));
        _fog?.SetValue(Atmosphere.GroundFog);
    }
}
