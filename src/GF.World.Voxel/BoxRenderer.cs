using GF.Core;
using GF.Engine;
using GF.World.Map;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GF.World.Voxel;

/// <param name="Position">Centro de los pies, en coordenadas canónicas del mundo (X en [0, ancho)).</param>
/// <param name="Size">Ancho, alto y fondo de la entidad: de ahí sale la silueta de su sprite (ver CreatureSprites).</param>
/// <param name="Color">Color de la entidad (ya con su luz): tiñe el sprite.</param>
public readonly record struct EntityBox(Vec3d Position, Vector3 Size, Color Color);

/// <summary>
/// Dibuja las entidades como SPRITES con billboard (siempre de cara a la cámara, girando solo alrededor del eje vertical), al estilo roguelike, con origen flotante.
/// Cada tamaño de entidad tiene su silueta propia (CreatureSprites), generada por código y guardada en una caché de texturas. La X se resuelve por el camino más
/// corto, así que una entidad al otro lado de la costura este-oeste aparece donde debe. El nombre de la clase se conserva por compatibilidad.
/// </summary>
public sealed class BoxRenderer : IDisposable
{
    private sealed record Entry(Texture2D Texture, float Aspect);

    private readonly GraphicsDevice _device;
    private readonly WorldScale _scale;
    private readonly AlphaTestEffect _effect;
    private readonly SkyRenderer _sky;   // el cielo (cúpula, estrellas y nubes) se dibuja aquí: es el último paso tras el terreno y los chunks
    private readonly Dictionary<(int, int), Entry> _sprites = new();
    private readonly VertexPositionTexture[] _quad = new VertexPositionTexture[4];
    private readonly short[] _idx = { 0, 1, 2, 0, 2, 3 };

    public Color FogColor { get; set; } = Color.CornflowerBlue;
    public float FogStart { get; set; } = 60f;
    public float FogEnd { get; set; } = 100f;
    /// <summary>Balanceo suave de reposo de las criaturas, en fracción de su altura (0 = quietas).</summary>
    public float BobAmount { get; set; } = 0.03f;

    public BoxRenderer(GraphicsDevice device, WorldScale scale)
    {
        _device = device;
        _scale = scale;
        _sky = new SkyRenderer(device);
        _effect = new AlphaTestEffect(device)
        {
            VertexColorEnabled = false,
            AlphaFunction = CompareFunction.Greater,
            ReferenceAlpha = 128,   // los píxeles con alfa <= 128 no se dibujan
        };
    }

    private Entry GetSprite(Vector3 size)
    {
        var key = ((int)MathF.Round(size.X * 20f), (int)MathF.Round(size.Y * 20f));
        if (_sprites.TryGetValue(key, out var entry)) return entry;

        var pixels = CreatureSprites.Create(size, out int w, out int h);
        var texture = new Texture2D(_device, w, h);
        texture.SetData(pixels);
        return _sprites[key] = new Entry(texture, w / (float)h);
    }

    public void Draw(ICamera3D camera, IEnumerable<EntityBox> boxes)
    {
        _sky.Draw(camera, FogColor, FogStart, FogEnd);

        var origin = camera.Position;
        var view = camera.ViewRelativeTo(origin);
        // Los ejes de la cámara en el mundo son las columnas de la matriz de vista: el billboard gira solo alrededor del eje vertical.
        var right = new Vector3(view.M11, view.M21, view.M31);
        right.Y = 0f;
        right = right.LengthSquared() < 1e-6f ? Vector3.UnitX : Vector3.Normalize(right);

        _effect.World = Matrix.Identity;
        _effect.View = view;
        _effect.Projection = camera.Projection;
        _effect.FogEnabled = true;
        _effect.FogColor = FogColor.ToVector3();
        _effect.FogStart = FogStart;
        _effect.FogEnd = FogEnd;

        _device.BlendState = BlendState.Opaque;
        _device.DepthStencilState = DepthStencilState.Default;
        _device.RasterizerState = RasterizerState.CullNone;
        _device.SamplerStates[0] = SamplerState.PointClamp;

        float time = (float)(Environment.TickCount64 % 1_000_000) * 0.005f;
        foreach (var b in boxes)
        {
            var sprite = GetSprite(b.Size);
            float height = b.Size.Y, width = height * sprite.Aspect;
            float bob = MathF.Sin(time + (float)(b.Position.X * 3.1 + b.Position.Z * 1.7)) * BobAmount * height;
            var bottom = new Vector3(
                (float)_scale.DeltaX(origin.X, b.Position.X),
                (float)(b.Position.Y - origin.Y) + bob,
                (float)(b.Position.Z - origin.Z));
            var half = right * (width * 0.5f);
            var up = new Vector3(0f, height, 0f);

            _quad[0] = new VertexPositionTexture(bottom - half, new Vector2(0f, 1f));
            _quad[1] = new VertexPositionTexture(bottom + half, new Vector2(1f, 1f));
            _quad[2] = new VertexPositionTexture(bottom + half + up, new Vector2(1f, 0f));
            _quad[3] = new VertexPositionTexture(bottom - half + up, new Vector2(0f, 0f));

            _effect.Texture = sprite.Texture;
            _effect.DiffuseColor = b.Color.ToVector3();
            _effect.CurrentTechnique.Passes[0].Apply();
            _device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, _quad, 0, 4, _idx, 0, 2);
        }
    }

    public void Dispose()
    {
        foreach (var e in _sprites.Values) e.Texture.Dispose();
        _sprites.Clear();
        _sky.Dispose();
        _effect.Dispose();
    }
}
