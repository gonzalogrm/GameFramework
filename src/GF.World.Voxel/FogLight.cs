using GF.Core;
using Microsoft.Xna.Framework;

namespace GF.World.Voxel;

/// <summary>Conecta la niebla con el mapa de luz del mundo (LightWorld).</summary>
public static class FogLight
{
    /// <summary>Luz total (ambiente, cielo y bloques emisores, por canal) de la celda, de 0 a 1 con un mínimo para que la niebla nunca desaparezca del todo en la oscuridad.</summary>
    public static FogLightQuery For(LightWorld light) => (int wx, int wy, int wz, out Vector3 result) =>
    {
        var s = light.Sample(new CellCoord(wx, wy, wz));
        if (!s.Known) { result = Vector3.One; return false; }
        var t = s.Total / LightField.Max;
        result = new Vector3(Math.Clamp(t.X, 0.08f, 1f), Math.Clamp(t.Y, 0.08f, 1f), Math.Clamp(t.Z, 0.08f, 1f));
        return true;
    };
}
