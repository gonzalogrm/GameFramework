using GF.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GF.World.Voxel;

/// <summary>
/// Dibuja las celdas de un GasCloudField como cubos de un bloque semitransparentes (opacidad = densidad x MaxAlpha), sin escribir profundidad. Solo se emiten las caras
/// que miran a la cámara y se descartan las celdas fuera de pantalla. Como los cubos coinciden con la cuadrícula de bloques, el rasterizador usa un sesgo de profundidad
/// negativo (la nube gana a las caras de los bloques en el mismo plano: sin parpadeo y sin encoger los cubos).
/// </summary>
public sealed class GasCloudRenderer : IDisposable
{
    private const int MaxQuads = 16000;

    private readonly GraphicsDevice _device;
    private readonly BasicEffect _fx;
    private readonly RasterizerState _raster = new() { CullMode = CullMode.None, DepthBias = -0.00002f, SlopeScaleDepthBias = -1f };
    private readonly VertexPositionColor[] _verts = new VertexPositionColor[MaxQuads * 4];
    private readonly short[] _idx = new short[MaxQuads * 6];
    private int _quads;

    public Vector3 BaseColor { get; set; } = new(0.62f, 0.62f, 0.64f);
    /// <summary>Luz del día (multiplica el color).</summary>
    public Vector3 Tint { get; set; } = Vector3.One;
    public int CellsDrawn { get; private set; }

    public GasCloudRenderer(GraphicsDevice device)
    {
        _device = device;
        _fx = new BasicEffect(device) { VertexColorEnabled = true, TextureEnabled = false, LightingEnabled = false, FogEnabled = false };
        for (int q = 0; q < MaxQuads; q++)
        {
            int i = q * 6, v = q * 4;
            _idx[i] = (short)v; _idx[i + 1] = (short)(v + 1); _idx[i + 2] = (short)(v + 2);
            _idx[i + 3] = (short)v; _idx[i + 4] = (short)(v + 2); _idx[i + 5] = (short)(v + 3);
        }
    }

    public void Draw(ICamera3D camera, GasCloudField field)
    {
        CellsDrawn = 0;
        if (field.ActiveCount == 0) return;

        var origin = camera.Position;
        var view = camera.ViewRelativeTo(origin);
        var proj = camera.Projection;
        _fx.World = Matrix.Identity;
        _fx.View = view;
        _fx.Projection = proj;
        var right = new Vector3(view.M11, view.M21, view.M31);
        var up = new Vector3(view.M12, view.M22, view.M32);
        var fwd = new Vector3(-view.M13, -view.M23, -view.M33);
        float tanX = 1f / MathF.Max(0.1f, proj.M11), tanY = 1f / MathF.Max(0.1f, proj.M22);

        _device.BlendState = BlendState.NonPremultiplied;
        _device.DepthStencilState = DepthStencilState.DepthRead;
        _device.RasterizerState = _raster;

        var color = BaseColor * Tint;
        float maxAlpha = field.Options.MaxAlpha;
        _quads = 0;

        for (int i = 0; i < field.ActiveCount; i++)
        {
            field.GetCell(i, out int cx, out int cy, out int cz, out float density);
            int alpha = (int)(density * maxAlpha * 255f);
            if (alpha < 4) continue;

            float x0 = (float)(cx - origin.X), y0 = (float)(cy - origin.Y), z0 = (float)(cz - origin.Z);
            var center = new Vector3(x0 + 0.5f, y0 + 0.5f, z0 + 0.5f);
            float df = Vector3.Dot(center, fwd);
            if (df < -1.8f) continue;
            if (MathF.Abs(Vector3.Dot(center, right)) > df * tanX + 2f) continue;
            if (MathF.Abs(Vector3.Dot(center, up)) > df * tanY + 2f) continue;
            CellsDrawn++;
            Box(x0, x0 + 1f, y0, y0 + 1f, z0, z0 + 1f, color, alpha);
        }
        Flush();

        _device.BlendState = BlendState.Opaque;
        _device.DepthStencilState = DepthStencilState.Default;
        _device.RasterizerState = RasterizerState.CullCounterClockwise;
    }

    /// <summary>Un cubo: solo las caras que miran a la cámara, que está en el origen.</summary>
    private void Box(float x0, float x1, float ya, float yb, float z0, float z1, Vector3 c, int alpha)
    {
        if (z0 > 0f) Quad(new Vector3(x0, ya, z0), new Vector3(x1, ya, z0), new Vector3(x1, yb, z0), new Vector3(x0, yb, z0), c, 0.82f, alpha);
        else if (z1 < 0f) Quad(new Vector3(x0, ya, z1), new Vector3(x1, ya, z1), new Vector3(x1, yb, z1), new Vector3(x0, yb, z1), c, 0.82f, alpha);
        if (x0 > 0f) Quad(new Vector3(x0, ya, z0), new Vector3(x0, ya, z1), new Vector3(x0, yb, z1), new Vector3(x0, yb, z0), c, 0.88f, alpha);
        else if (x1 < 0f) Quad(new Vector3(x1, ya, z0), new Vector3(x1, ya, z1), new Vector3(x1, yb, z1), new Vector3(x1, yb, z0), c, 0.88f, alpha);
        if (ya > 0f) Quad(new Vector3(x0, ya, z0), new Vector3(x1, ya, z0), new Vector3(x1, ya, z1), new Vector3(x0, ya, z1), c, 0.75f, alpha);
        else if (yb < 0f) Quad(new Vector3(x0, yb, z0), new Vector3(x1, yb, z0), new Vector3(x1, yb, z1), new Vector3(x0, yb, z1), c, 1f, alpha);
    }

    private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 color, float shade, int alpha)
    {
        if (_quads >= MaxQuads) Flush();
        var col = new Color((int)(color.X * shade * 255f), (int)(color.Y * shade * 255f), (int)(color.Z * shade * 255f), alpha);
        int v = _quads * 4;
        _verts[v] = new VertexPositionColor(a, col);
        _verts[v + 1] = new VertexPositionColor(b, col);
        _verts[v + 2] = new VertexPositionColor(c, col);
        _verts[v + 3] = new VertexPositionColor(d, col);
        _quads++;
    }

    private void Flush()
    {
        if (_quads == 0) return;
        _fx.CurrentTechnique.Passes[0].Apply();
        _device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, _verts, 0, _quads * 4, _idx, 0, _quads * 2);
        _quads = 0;
    }

    public void Dispose()
    {
        _fx.Dispose();
        _raster.Dispose();
    }
}
