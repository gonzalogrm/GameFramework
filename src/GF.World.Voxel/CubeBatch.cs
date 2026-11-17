using GF.Core;
using GF.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GF.World.Voxel;

/// <summary>
/// Lote de cubos semitransparentes de color plano (niebla, gas, humo): solo emite las caras que miran a la cámara (de 1 a 3 por cubo) en un array reutilizado
/// y las dibuja con BasicEffect, mezcla alfa y lectura de profundidad sin escritura. Origen flotante: todo se da RELATIVO a la cámara (que está en el origen).
///     batch.Begin(camera);  ... if (batch.InView(centro, 1.8f, 2f)) batch.Box(x0, x1, y0, y1, z0, z1, color, alfa);  ...  batch.End();
/// El sesgo de profundidad negativo hace que los cubos ganen a las caras de bloques en el mismo plano (sin parpadeo y sin encoger los cubos).
/// </summary>
internal sealed class CubeBatch : IDisposable
{
    private const int MaxQuads = 16000;   // 64.000 vértices: caben en índices de 16 bits

    private readonly GraphicsDevice _device;
    private readonly BasicEffect _fx;
    private readonly VertexPositionColor[] _verts = new VertexPositionColor[MaxQuads * 4];
    private readonly short[] _idx = new short[MaxQuads * 6];
    private readonly RasterizerState _raster = new() { CullMode = CullMode.None, DepthBias = -0.00002f, SlopeScaleDepthBias = -1f };
    private int _quads;
    private Vector3 _right, _up, _forward;
    private float _tanX, _tanY;

    public CubeBatch(GraphicsDevice device)
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

    /// <summary>Posición de la cámara en el mundo (doble precisión): réstala a las coordenadas de cada cubo.</summary>
    public Vec3d Origin { get; private set; }

    public void Begin(ICamera3D camera)
    {
        Origin = camera.Position;
        var view = camera.ViewRelativeTo(Origin);
        var proj = camera.Projection;
        _fx.World = Matrix.Identity;
        _fx.View = view;
        _fx.Projection = proj;

        // Ejes de la cámara en el mundo (columnas de la vista) y tangentes del campo de visión, para descartar sin recorte exacto.
        _right = new Vector3(view.M11, view.M21, view.M31);
        _up = new Vector3(view.M12, view.M22, view.M32);
        _forward = new Vector3(-view.M13, -view.M23, -view.M33);
        _tanX = 1f / MathF.Max(0.1f, proj.M11);
        _tanY = 1f / MathF.Max(0.1f, proj.M22);

        _device.BlendState = BlendState.NonPremultiplied;
        _device.DepthStencilState = DepthStencilState.DepthRead;
        _device.RasterizerState = _raster;
        _quads = 0;
    }

    /// <summary>¿Puede verse algo con este centro (relativo a la cámara)? 'behind' = holgura por detrás de la cámara; 'side' = holgura lateral y vertical.</summary>
    public bool InView(Vector3 center, float behind, float side)
    {
        float df = Vector3.Dot(center, _forward);
        if (df < -behind) return false;
        if (MathF.Abs(Vector3.Dot(center, _right)) > df * _tanX + side) return false;
        if (MathF.Abs(Vector3.Dot(center, _up)) > df * _tanY + side) return false;
        return true;
    }

    /// <summary>Un cubo (caja): solo las caras que miran a la cámara, que está en el origen.</summary>
    public void Box(float x0, float x1, float ya, float yb, float z0, float z1, Vector3 c, int alpha)
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

    /// <summary>Dibuja lo pendiente y deja el dispositivo en el estado normal de los chunks.</summary>
    public void End()
    {
        Flush();
        _device.BlendState = BlendState.Opaque;
        _device.DepthStencilState = DepthStencilState.Default;
        _device.RasterizerState = RasterizerState.CullCounterClockwise;
    }

    public void Dispose()
    {
        _fx.Dispose();
        _raster.Dispose();
    }
}
