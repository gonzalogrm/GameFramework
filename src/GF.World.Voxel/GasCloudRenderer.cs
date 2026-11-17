using GF.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GF.World.Voxel;

/// <summary>
/// Dibuja las celdas de un GasCloudField como cubos de un bloque semitransparentes (opacidad = densidad x MaxAlpha), sin escribir profundidad. Solo se emiten las caras
/// que miran a la cámara y se descartan las celdas fuera de pantalla (todo ello en CubeBatch). Como los cubos coinciden con la cuadrícula de bloques, el rasterizador usa un sesgo
/// de profundidad negativo (la nube gana a las caras de los bloques en el mismo plano: sin parpadeo y sin encoger los cubos).
/// </summary>
public sealed class GasCloudRenderer : IDisposable
{
    private readonly CubeBatch _batch;

    public Vector3 BaseColor { get; set; } = new(0.62f, 0.62f, 0.64f);
    /// <summary>Luz del día (multiplica el color).</summary>
    public Vector3 Tint { get; set; } = Vector3.One;
    public int CellsDrawn { get; private set; }

    public GasCloudRenderer(GraphicsDevice device) => _batch = new CubeBatch(device);

    public void Draw(ICamera3D camera, GasCloudField field)
    {
        CellsDrawn = 0;
        if (field.ActiveCount == 0) return;

        _batch.Begin(camera);
        var origin = _batch.Origin;
        var color = BaseColor * Tint;
        float maxAlpha = field.Options.MaxAlpha;

        for (int i = 0; i < field.ActiveCount; i++)
        {
            field.GetCell(i, out int cx, out int cy, out int cz, out float density);
            int alpha = (int)(density * maxAlpha * 255f);
            if (alpha < 4) continue;

            float x0 = (float)(cx - origin.X), y0 = (float)(cy - origin.Y), z0 = (float)(cz - origin.Z);
            if (!_batch.InView(new Vector3(x0 + 0.5f, y0 + 0.5f, z0 + 0.5f), 1.8f, 2f)) continue;
            CellsDrawn++;
            _batch.Box(x0, x0 + 1f, y0, y0 + 1f, z0, z0 + 1f, color, alpha);
        }
        _batch.End();
    }

    public void Dispose() => _batch.Dispose();
}
