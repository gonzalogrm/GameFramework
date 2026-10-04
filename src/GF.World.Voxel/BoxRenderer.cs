using GF.Core;
using GF.Engine;
using GF.World.Map;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GF.World.Voxel;

/// <param name="Position">Centro de los pies, en coordenadas canónicas del mundo (X en [0, ancho)).</param>
public readonly record struct EntityBox(Vec3d Position, Vector3 Size, Color Color);

/// <summary>
/// Dibuja cajas de color (entidades de marcador de posición) con origen flotante. La X se resuelve por el camino
/// más corto, así que una entidad al otro lado de la costura este-oeste aparece donde debe.
/// </summary>
public sealed class BoxRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly WorldScale _scale;
    private readonly BasicEffect _effect;
    private readonly VertexBuffer _vb;
    private readonly IndexBuffer _ib;

    public Color FogColor { get; set; } = Color.CornflowerBlue;
    public float FogStart { get; set; } = 60f;
    public float FogEnd { get; set; } = 100f;

    public BoxRenderer(GraphicsDevice device, WorldScale scale)
    {
        _device = device;
        _scale = scale;
        _effect = new BasicEffect(device) { VertexColorEnabled = true, TextureEnabled = false, LightingEnabled = false };

        var verts = new VertexPositionColor[24];
        var idx = new short[36];
        for (int f = 0; f < 6; f++)
        {
            for (int k = 0; k < 4; k++) verts[f * 4 + k] = new VertexPositionColor(VoxelFaces.Corners[f][k], VoxelFaces.Shade[f]);
            int b = f * 4, i = f * 6;
            idx[i] = (short)b; idx[i + 1] = (short)(b + 2); idx[i + 2] = (short)(b + 1);
            idx[i + 3] = (short)b; idx[i + 4] = (short)(b + 3); idx[i + 5] = (short)(b + 2);
        }
        _vb = new VertexBuffer(device, VertexPositionColor.VertexDeclaration, verts.Length, BufferUsage.WriteOnly);
        _vb.SetData(verts);
        _ib = new IndexBuffer(device, IndexElementSize.SixteenBits, idx.Length, BufferUsage.WriteOnly);
        _ib.SetData(idx);
    }

    public void Draw(ICamera3D camera, IEnumerable<EntityBox> boxes)
    {
        var origin = camera.Position;
        _effect.View = camera.ViewRelativeTo(origin);
        _effect.Projection = camera.Projection;
        _effect.FogEnabled = true;
        _effect.FogColor = FogColor.ToVector3();
        _effect.FogStart = FogStart;
        _effect.FogEnd = FogEnd;

        _device.BlendState = BlendState.Opaque;
        _device.DepthStencilState = DepthStencilState.Default;
        _device.RasterizerState = RasterizerState.CullCounterClockwise;
        _device.SetVertexBuffer(_vb);
        _device.Indices = _ib;

        foreach (var b in boxes)
        {
            var rel = new Vector3(
                (float)_scale.DeltaX(origin.X, b.Position.X),
                (float)(b.Position.Y - origin.Y),
                (float)(b.Position.Z - origin.Z));
            _effect.World = Matrix.CreateScale(b.Size) * Matrix.CreateTranslation(rel - new Vector3(b.Size.X * 0.5f, 0f, b.Size.Z * 0.5f));
            _effect.DiffuseColor = b.Color.ToVector3();
            _effect.CurrentTechnique.Passes[0].Apply();
            _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 12);
        }
    }

    public void Dispose()
    {
        _vb.Dispose(); _ib.Dispose(); _effect.Dispose();
    }
}
