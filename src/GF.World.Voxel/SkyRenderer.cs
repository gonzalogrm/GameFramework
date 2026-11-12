using GF.Core;
using GF.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GF.World.Voxel;

/// <summary>
/// El cielo: una cúpula con degradado (del color del horizonte, que es el de la niebla, a un cenit más oscuro), estrellas de noche y dos capas de nubes que
/// avanzan con el viento. Se dibuja DESPUÉS de los chunks y del terreno lejano, con la cúpula al fondo de la profundidad (solo rellena lo que no tapa nada).
/// Todo se tiñe a partir del color del horizonte que da el ciclo día/noche, así que amanece, atardece y anochece solo. Es barato: ~300 vértices de cúpula,
/// dos mallas de nubes de 24 x 24 celdas y una textura de ruido de 128 x 128 generada una vez.
/// </summary>
internal sealed class SkyRenderer : IDisposable
{
    private const int Rings = 12, Segments = 24, StarCount = 160, CloudCells = 24, CloudTexSize = 128;

    private readonly GraphicsDevice _device;
    private readonly BasicEffect _skyFx, _cloudFx;
    private readonly Vector3[] _dir = new Vector3[Rings * Segments];
    private readonly VertexPositionColor[] _dome = new VertexPositionColor[Rings * Segments];
    private readonly short[] _domeIdx = new short[(Rings - 1) * Segments * 6];
    private VertexPositionColor[] _stars = Array.Empty<VertexPositionColor>();
    private short[] _starIdx = Array.Empty<short>();
    private int _starSeed = int.MinValue;
    private Texture2D? _cloudTex;
    private float _cloudCoverage = -1f;
    private readonly VertexPositionColorTexture[] _cloudVerts = new VertexPositionColorTexture[(CloudCells + 1) * (CloudCells + 1)];
    private readonly short[] _cloudIdx = new short[CloudCells * CloudCells * 6];

    public SkyRenderer(GraphicsDevice device)
    {
        _device = device;
        _skyFx = new BasicEffect(device) { VertexColorEnabled = true, TextureEnabled = false, LightingEnabled = false, FogEnabled = false };
        _cloudFx = new BasicEffect(device) { VertexColorEnabled = true, TextureEnabled = true, LightingEnabled = false, FogEnabled = false };

        for (int r = 0; r < Rings; r++)
        {
            float elev = -0.3f + (MathHelper.PiOver2 + 0.3f) * r / (Rings - 1);
            float ce = MathF.Cos(elev), se = MathF.Sin(elev);
            for (int s = 0; s < Segments; s++)
            {
                float az = MathHelper.TwoPi * s / Segments;
                _dir[r * Segments + s] = new Vector3(MathF.Cos(az) * ce, se, MathF.Sin(az) * ce);
            }
        }
        int k = 0;
        for (int r = 0; r < Rings - 1; r++)
        for (int s = 0; s < Segments; s++)
        {
            int a = r * Segments + s, b = r * Segments + (s + 1) % Segments, c = (r + 1) * Segments + s, d = (r + 1) * Segments + (s + 1) % Segments;
            _domeIdx[k++] = (short)a; _domeIdx[k++] = (short)c; _domeIdx[k++] = (short)b;
            _domeIdx[k++] = (short)b; _domeIdx[k++] = (short)c; _domeIdx[k++] = (short)d;
        }
        k = 0;
        for (int z = 0; z < CloudCells; z++)
        for (int x = 0; x < CloudCells; x++)
        {
            int a = z * (CloudCells + 1) + x, b = a + 1, c = a + CloudCells + 1, d = c + 1;
            _cloudIdx[k++] = (short)a; _cloudIdx[k++] = (short)c; _cloudIdx[k++] = (short)b;
            _cloudIdx[k++] = (short)b; _cloudIdx[k++] = (short)c; _cloudIdx[k++] = (short)d;
        }
    }

    private static float Luminance(Color c) => (0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B) / 255f;

    public void Draw(ICamera3D camera, Color horizon, float fogStart, float fogEnd)
    {
        var origin = camera.Position;
        var view = camera.ViewRelativeTo(origin);
        var proj = camera.Projection;
        float far = proj.M43 / (proj.M33 + 1f);
        if (float.IsNaN(far) || far < 50f) far = 500f;
        float radius = far * 0.98f;
        float lum = Luminance(horizon);

        _device.BlendState = BlendState.Opaque;
        _device.DepthStencilState = DepthStencilState.DepthRead;   // solo rellena lo que no tapa nada
        _device.RasterizerState = RasterizerState.CullNone;

        _skyFx.World = Matrix.CreateScale(radius);
        _skyFx.View = view;
        _skyFx.Projection = proj;

        if (Atmosphere.SkyGradient)
        {
            var zenith = new Color((int)(horizon.R * 0.62f), (int)(horizon.G * 0.74f), (int)(horizon.B * 0.92f));
            for (int i = 0; i < _dome.Length; i++)
            {
                float t = MathF.Pow(MathF.Max(0f, _dir[i].Y), 0.5f);
                _dome[i] = new VertexPositionColor(_dir[i], Color.Lerp(horizon, zenith, t));
            }
            _skyFx.CurrentTechnique.Passes[0].Apply();
            _device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, _dome, 0, _dome.Length, _domeIdx, 0, _domeIdx.Length / 3);
        }

        float night = Math.Clamp((0.25f - lum) / 0.2f, 0f, 1f);
        if (Atmosphere.Stars && night > 0.02f)
        {
            EnsureStars();
            _device.BlendState = BlendState.AlphaBlend;
            _skyFx.Alpha = night;
            _skyFx.CurrentTechnique.Passes[0].Apply();
            _device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, _stars, 0, _stars.Length, _starIdx, 0, _starIdx.Length / 3);
            _skyFx.Alpha = 1f;
        }

        if (Atmosphere.CloudCoverage > 0.01f) DrawClouds(origin, view, proj, horizon, lum, fogStart, fogEnd, far);
    }

    // ------------------------------------------------------------------ estrellas

    private void EnsureStars()
    {
        if (_starSeed == Atmosphere.StarSeed && _stars.Length > 0) return;
        _starSeed = Atmosphere.StarSeed;
        var rng = new Random(_starSeed ^ 0x57A25);
        _stars = new VertexPositionColor[StarCount * 4];
        _starIdx = new short[StarCount * 6];
        for (int i = 0; i < StarCount; i++)
        {
            float y = 0.08f + 0.92f * (float)rng.NextDouble(), az = MathHelper.TwoPi * (float)rng.NextDouble(), ce = MathF.Sqrt(1f - y * y);
            var dir = new Vector3(MathF.Cos(az) * ce, y, MathF.Sin(az) * ce);
            var t1 = Vector3.Normalize(Vector3.Cross(dir, Vector3.Up));
            var t2 = Vector3.Cross(dir, t1);
            float size = 0.004f * (0.6f + 0.9f * (float)rng.NextDouble());
            var col = Color.White * (0.5f + 0.5f * (float)rng.NextDouble());
            var c = dir * 0.99f;
            int v = i * 4;
            _stars[v] = new VertexPositionColor(c - t1 * size - t2 * size, col);
            _stars[v + 1] = new VertexPositionColor(c + t1 * size - t2 * size, col);
            _stars[v + 2] = new VertexPositionColor(c + t1 * size + t2 * size, col);
            _stars[v + 3] = new VertexPositionColor(c - t1 * size + t2 * size, col);
            int k = i * 6;
            _starIdx[k] = (short)v; _starIdx[k + 1] = (short)(v + 1); _starIdx[k + 2] = (short)(v + 2);
            _starIdx[k + 3] = (short)v; _starIdx[k + 4] = (short)(v + 2); _starIdx[k + 5] = (short)(v + 3);
        }
    }

    // ------------------------------------------------------------------ nubes

    private static float Hash01(int x, int y, int salt)
    {
        uint h = unchecked((uint)(x * 374761393 + y * 668265263 + salt * 1274126177));
        h = unchecked((h ^ (h >> 13)) * 1274126177u);
        h ^= h >> 16;
        return (h & 0xFFFF) / 65535f;
    }

    /// <summary>Ruido de valores 0..1 periódico (la textura se repite sin costuras). 'cells' = nº de celdas por lado de la textura.</summary>
    private static float Vn(float u, float v, int cells, int salt)
    {
        float x = u * cells, y = v * cells;
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        int x0 = ((ix % cells) + cells) % cells, x1 = (x0 + 1) % cells, y0 = ((iy % cells) + cells) % cells, y1 = (y0 + 1) % cells;
        float a = Hash01(x0, y0, salt), b = Hash01(x1, y0, salt), c = Hash01(x0, y1, salt), d = Hash01(x1, y1, salt);
        return (a + (b - a) * fx) * (1f - fy) + (c + (d - c) * fx) * fy;
    }

    private void EnsureCloudTexture()
    {
        float coverage = Atmosphere.CloudCoverage;
        if (_cloudTex != null && MathF.Abs(coverage - _cloudCoverage) < 0.005f) return;
        _cloudTex?.Dispose();
        _cloudCoverage = coverage;

        float threshold = 1f - Math.Clamp(coverage, 0f, 1f) * 0.8f;   // más cobertura = umbral más bajo
        var level = new Color[CloudTexSize * CloudTexSize];
        for (int y = 0; y < CloudTexSize; y++)
        for (int x = 0; x < CloudTexSize; x++)
        {
            float u = x / (float)CloudTexSize, v = y / (float)CloudTexSize;
            float n = 0.5f * Vn(u, v, 6, 1) + 0.3f * Vn(u, v, 12, 2) + 0.2f * Vn(u, v, 24, 3);
            float a = MathHelper.SmoothStep(0f, 1f, Math.Clamp((n - threshold + 0.14f) / 0.24f, 0f, 1f));
            float shade = 1f - 0.22f * MathHelper.SmoothStep(0f, 1f, Math.Clamp((n - threshold - 0.05f) / 0.3f, 0f, 1f));   // el centro denso, algo más gris
            int g = (int)(255 * shade);
            level[y * CloudTexSize + x] = new Color(g, g, g, (int)(a * 235f));
        }

        int levels = 1;
        for (int s = CloudTexSize; s > 1; s >>= 1) levels++;
        var tex = new Texture2D(_device, CloudTexSize, CloudTexSize, true, SurfaceFormat.Color);
        int size = CloudTexSize;
        for (int l = 0; l < levels; l++)
        {
            tex.SetData(l, null, level, 0, size * size);
            int next = Math.Max(1, size / 2);
            if (next == size) break;
            var smaller = new Color[next * next];
            for (int y = 0; y < next; y++)
            for (int x = 0; x < next; x++)
            {
                int r = 0, g = 0, b = 0, a = 0;
                for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < 2; dx++)
                {
                    var c = level[(y * 2 + dy) * size + x * 2 + dx];
                    r += c.R; g += c.G; b += c.B; a += c.A;
                }
                smaller[y * next + x] = new Color(r / 4, g / 4, b / 4, a / 4);
            }
            level = smaller;
            size = next;
        }
        _cloudTex = tex;
    }

    /// <summary>Dos capas de nubes (planos con textura de ruido) que avanzan con el viento. Cada vértice se funde con el color del horizonte según su distancia.</summary>
    private void DrawClouds(Vec3d origin, Matrix view, Matrix proj, Color horizon, float lum, float fogStart, float fogEnd, float far)
    {
        EnsureCloudTexture();
        float bright = Math.Clamp(0.25f + 1.1f * lum, 0f, 1f);   // de noche, nubes oscuras
        var warm = Color.Lerp(Color.White, horizon, 0.35f);       // y teñidas por el amanecer y el ocaso
        var tint = new Color((int)(warm.R * bright), (int)(warm.G * bright), (int)(warm.B * bright));
        double t = Environment.TickCount64 / 1000.0;
        float dirX = MathF.Cos(Atmosphere.WindAngle), dirZ = MathF.Sin(Atmosphere.WindAngle);
        float extent = MathF.Min(far, 2600f);
        float cell = 2f * extent / CloudCells;

        _device.BlendState = BlendState.NonPremultiplied;
        _device.SamplerStates[0] = SamplerState.LinearWrap;
        _cloudFx.World = Matrix.Identity;
        _cloudFx.View = view;
        _cloudFx.Projection = proj;
        _cloudFx.Texture = _cloudTex;

        for (int layer = 0; layer < 2; layer++)
        {
            float alt = Atmosphere.CloudAltitude + layer * 40f;
            float rel = (float)(alt - origin.Y);
            if (rel < 4f) continue;   // por encima de las nubes: no se dibujan
            float scale = layer == 0 ? 700f : 1100f, speed = 3f * Atmosphere.CloudSpeed * (layer == 0 ? 1f : 1.6f), alphaMul = layer == 0 ? 1f : 0.65f;
            double u0 = (origin.X + dirX * speed * t) / scale + layer * 0.37; u0 -= Math.Floor(u0);
            double v0 = (origin.Z + dirZ * speed * t) / scale + layer * 0.61; v0 -= Math.Floor(v0);
            int k = 0;
            for (int gz = 0; gz <= CloudCells; gz++)
            for (int gx = 0; gx <= CloudCells; gx++)
            {
                float x = gx * cell - extent, z = gz * cell - extent;
                float d = MathF.Sqrt(x * x + rel * rel + z * z);
                float f = Math.Clamp((d - fogStart) / MathF.Max(1f, fogEnd - fogStart), 0f, 1f);
                var c = Color.Lerp(tint, horizon, f);
                var col = new Color(c.R, c.G, c.B, (int)(255f * alphaMul * (1f - 0.85f * f)));
                _cloudVerts[k++] = new VertexPositionColorTexture(new Vector3(x, rel, z), col, new Vector2((float)u0 + x / scale, (float)v0 + z / scale));
            }
            _cloudFx.CurrentTechnique.Passes[0].Apply();
            _device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, _cloudVerts, 0, _cloudVerts.Length, _cloudIdx, 0, _cloudIdx.Length / 3);
        }
    }

    public void Dispose()
    {
        _skyFx.Dispose();
        _cloudFx.Dispose();
        _cloudTex?.Dispose();
    }
}
