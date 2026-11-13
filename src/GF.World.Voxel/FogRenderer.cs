using GF.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GF.World.Voxel;

/// <summary>
/// Dibuja la niebla de FogSystem como bloques semitransparentes (cubos de CellSize, apilados en capas desde el suelo, más tenues cuanto más arriba). Sin
/// texturas ni shader propio: BasicEffect con color de vértice y mezcla alfa, leyendo la profundidad sin escribirla (los bloques opacos la tapan, la niebla no
/// tapa nada). De cada cubo solo se emiten las caras que miran a la cámara (de 1 a 3), las columnas fuera de pantalla se descartan antes de tocar sus capas y
/// todo va a un array reutilizado, sin reservas por frame. Origen flotante: posiciones relativas a la cámara (que es de doble precisión).
/// </summary>
public sealed class FogRenderer : IDisposable
{
    private const int MaxQuads = 16000;   // 64.000 vértices: caben en índices de 16 bits

    private readonly GraphicsDevice _device;
    private readonly BasicEffect _fx;
    private readonly VertexPositionColor[] _verts = new VertexPositionColor[MaxQuads * 4];
    private readonly short[] _idx = new short[MaxQuads * 6];
    private int _quads;

    /// <summary>Color de la niebla a plena luz (blanquecino azulado).</summary>
    public Vector3 BaseColor { get; set; } = new(0.86f, 0.89f, 0.93f);
    /// <summary>Tinte global (día/noche) cuando la niebla NO tiene LightQuery; con LightQuery manda la luz de cada columna y esto se ignora.</summary>
    public Vector3 Tint { get; set; } = Vector3.One;

    public int ColumnsDrawn { get; private set; }
    public int CubesDrawn { get; private set; }
    public string Stats => $"Niebla dibujada: {ColumnsDrawn} columnas, {CubesDrawn} cubos";

    public FogRenderer(GraphicsDevice device)
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

    public void Draw(ICamera3D camera, FogSystem fog)
    {
        ColumnsDrawn = CubesDrawn = 0;
        if (!fog.Options.Enabled || fog.ActiveCount == 0) return;

        var origin = camera.Position;
        var view = camera.ViewRelativeTo(origin);
        var proj = camera.Projection;
        _fx.World = Matrix.Identity;
        _fx.View = view;
        _fx.Projection = proj;

        // Ejes de la cámara en el mundo (columnas de la vista) y tangentes del campo de visión, para descartar columnas sin recorte exacto.
        var right = new Vector3(view.M11, view.M21, view.M31);
        var up = new Vector3(view.M12, view.M22, view.M32);
        var fwd = new Vector3(-view.M13, -view.M23, -view.M33);
        float tanX = 1f / MathF.Max(0.1f, proj.M11), tanY = 1f / MathF.Max(0.1f, proj.M22);

        _device.BlendState = BlendState.NonPremultiplied;
        _device.DepthStencilState = DepthStencilState.DepthRead;
        _device.RasterizerState = RasterizerState.CullNone;

        int cs = fog.Options.CellSize, layers = fog.Layers;
        float size = cs, radius = cs * 1.8f + layers * cs * 0.5f;
        bool useLight = fog.UsesLight;
        var globalColor = BaseColor * Tint;
        _quads = 0;

        for (int i = 0; i < fog.ActiveCount; i++)
        {
            ref readonly var c = ref fog.Active(i);
            if (c.Density <= 0.004f) continue;

            float x0 = (float)(c.Ix * (double)cs - origin.X);
            float z0 = (float)(c.Iz * (double)cs - origin.Z);
            float y0 = (float)(c.GroundY - origin.Y);
            var center = new Vector3(x0 + size * 0.5f, y0 + layers * cs * 0.5f, z0 + size * 0.5f);
            float df = Vector3.Dot(center, fwd);
            if (df < -radius) continue;
            if (MathF.Abs(Vector3.Dot(center, right)) > df * tanX + radius * 1.6f) continue;
            if (MathF.Abs(Vector3.Dot(center, up)) > df * tanY + radius * 1.6f) continue;
            ColumnsDrawn++;

            var color = useLight ? BaseColor * c.Light : globalColor;
            float x1 = x0 + size, z1 = z0 + size;

            for (int layer = 0; layer < layers; layer++)
            {
                float a = fog.LayerAlpha(in c, layer);
                if (a < 0.004f) continue;
                float ya = y0 + layer * cs;
                Box(x0, x1, ya, ya + cs, z0, z1, color, (int)(a * 255f));
            }
        }
        Flush();

        _device.BlendState = BlendState.Opaque;
        _device.DepthStencilState = DepthStencilState.Default;
        _device.RasterizerState = RasterizerState.CullCounterClockwise;
    }

    /// <summary>Un cubo (caja): solo las caras que miran a la cámara, que está en el origen.</summary>
    private void Box(float x0, float x1, float ya, float yb, float z0, float z1, Vector3 c, int alpha)
    {
        CubesDrawn++;
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

    public void Dispose() => _fx.Dispose();
}
