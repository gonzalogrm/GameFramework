using GF.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GF.World.Voxel;

/// <summary>
/// Dibuja la niebla de FogSystem como bloques semitransparentes (cubos de CellSize, apilados en capas desde el suelo, más tenues cuanto más arriba). Sin
/// texturas ni shader propio: CubeBatch (BasicEffect con color de vértice y mezcla alfa), leyendo la profundidad sin escribirla (los bloques opacos la tapan, la niebla no
/// tapa nada). De cada cubo solo se emiten las caras que miran a la cámara (de 1 a 3), las columnas fuera de pantalla se descartan antes de tocar sus capas y
/// todo va a un array reutilizado, sin reservas por frame. Origen flotante: posiciones relativas a la cámara (que es de doble precisión).
/// </summary>
public sealed class FogRenderer : IDisposable
{
    private readonly CubeBatch _batch;

    /// <summary>Color de la niebla a plena luz (blanquecino azulado).</summary>
    public Vector3 BaseColor { get; set; } = new(0.86f, 0.89f, 0.93f);
    /// <summary>Tinte global (día/noche) cuando la niebla NO tiene LightQuery; con LightQuery manda la luz de cada columna y esto se ignora.</summary>
    public Vector3 Tint { get; set; } = Vector3.One;
    /// <summary>Luz mínima por canal cuando hay LightQuery (un gas puede brillar un poco de noche o en cuevas).</summary>
    public float MinLight { get; set; }

    public int ColumnsDrawn { get; private set; }
    public int CubesDrawn { get; private set; }
    public string Stats => $"Niebla dibujada: {ColumnsDrawn} columnas, {CubesDrawn} cubos";

    public FogRenderer(GraphicsDevice device) => _batch = new CubeBatch(device);

    public void Draw(ICamera3D camera, FogSystem fog)
    {
        ColumnsDrawn = CubesDrawn = 0;
        if (!fog.Options.Enabled || fog.ActiveCount == 0) return;

        _batch.Begin(camera);
        var origin = _batch.Origin;

        int cs = fog.Options.CellSize, layers = fog.Layers;
        float size = cs, radius = cs * 1.8f + layers * cs * 0.5f;
        bool useLight = fog.UsesLight;
        var globalColor = BaseColor * Tint;
        var minLight = new Vector3(MinLight);

        for (int i = 0; i < fog.ActiveCount; i++)
        {
            ref readonly var c = ref fog.Active(i);
            if (c.Density <= 0.004f) continue;

            float x0 = (float)(c.Ix * (double)cs - origin.X);
            float z0 = (float)(c.Iz * (double)cs - origin.Z);
            float y0 = (float)(c.GroundY - origin.Y);
            var center = new Vector3(x0 + size * 0.5f, y0 + layers * cs * 0.5f, z0 + size * 0.5f);
            if (!_batch.InView(center, radius, radius * 1.6f)) continue;
            ColumnsDrawn++;

            var color = useLight ? BaseColor * Vector3.Max(c.Light, minLight) : globalColor;
            float x1 = x0 + size, z1 = z0 + size;

            for (int layer = 0; layer < layers; layer++)
            {
                float a = fog.LayerAlpha(in c, layer);
                if (a < 0.004f) continue;
                float ya = y0 + layer * cs;
                CubesDrawn++;
                _batch.Box(x0, x1, ya, ya + cs, z0, z1, color, (int)(a * 255f));
            }
        }
        _batch.End();
    }

    public void Dispose() => _batch.Dispose();
}
