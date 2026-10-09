using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using GF.Core;
using GF.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GF.World.Voxel;

/// <summary>Buffers de un chunk: malla opaca, translúcida (agua) y de sprites en cruz, todas opcionales, más los Billboards.</summary>
public sealed class ChunkMesh : IDisposable
{
    private readonly VertexBuffer? _vb, _wvb, _svb;
    private readonly IndexBuffer? _ib, _wib, _sib;
    private readonly int _triangles, _waterTriangles, _spriteTriangles;

    /// <summary>Origen del chunk en celdas del mundo (entero, exacto). Los vértices son LOCALES a este origen.</summary>
    public CellCoord Origin { get; }
    public Vector3 Size { get; }
    /// <summary>Caja local al chunk que incluye los sprites que sobresalen de él (para recortar por frustum).</summary>
    public Vector3 BoundsMin { get; }
    public Vector3 BoundsMax { get; }
    public BillboardInstance[] Billboards { get; }
    public bool HasWater => _wvb != null;
    public bool HasSprites => _svb != null;
    /// <summary>Instante (segundos de juego) en que apareció este chunk; el fundido de entrada cuenta desde aquí.</summary>
    public double CreatedAt { get; set; }
    /// <summary>true cuando el fundido de entrada ha terminado (o no hay fundido): el chunk es del todo opaco.</summary>
    public bool FadedIn { get; set; }

    public ChunkMesh(GraphicsDevice device, MeshData data, CellCoord origin, Vector3 size)
    {
        Origin = origin;
        Size = size;
        BoundsMin = Vector3.Min(Vector3.Zero, data.ExtentMin);
        BoundsMax = Vector3.Max(size, data.ExtentMax);
        Billboards = data.Billboards;
        if (data.Indices.Length > 0)
        {
            (_vb, _ib) = Create(device, data.Vertices, data.Indices);
            _triangles = data.Indices.Length / 3;
        }
        if (data.WaterIndices.Length > 0)
        {
            (_wvb, _wib) = Create(device, data.WaterVertices, data.WaterIndices);
            _waterTriangles = data.WaterIndices.Length / 3;
        }
        if (data.SpriteIndices.Length > 0)
        {
            (_svb, _sib) = Create(device, data.SpriteVertices, data.SpriteIndices);
            _spriteTriangles = data.SpriteIndices.Length / 3;
        }
    }

    private static (VertexBuffer, IndexBuffer) Create(GraphicsDevice device, VertexPositionColorTexture[] v, int[] i)
    {
        var vb = new VertexBuffer(device, VertexPositionColorTexture.VertexDeclaration, v.Length, BufferUsage.WriteOnly);
        vb.SetData(v);
        var ib = new IndexBuffer(device, IndexElementSize.ThirtyTwoBits, i.Length, BufferUsage.WriteOnly);
        ib.SetData(i);
        return (vb, ib);
    }

    public void DrawOpaque(GraphicsDevice device) => Draw(device, _vb, _ib, _triangles);
    public void DrawWater(GraphicsDevice device) => Draw(device, _wvb, _wib, _waterTriangles);
    public void DrawSprites(GraphicsDevice device) => Draw(device, _svb, _sib, _spriteTriangles);

    private static void Draw(GraphicsDevice device, VertexBuffer? vb, IndexBuffer? ib, int triangles)
    {
        if (vb == null) return;
        device.SetVertexBuffer(vb);
        device.Indices = ib;
        device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, triangles);
    }

    public void Dispose()
    {
        _vb?.Dispose(); _ib?.Dispose(); _wvb?.Dispose(); _wib?.Dispose(); _svb?.Dispose(); _sib?.Dispose();
    }
}

/// <summary>
/// Mantiene una malla por chunk.
/// - Chunks que se cargan (y los vecinos ya mallados): se mallan en el ThreadPool sobre un ChunkSnapshot,
///   del más cercano al más lejano; la subida a GPU se hace en el hilo principal con presupuesto por frame.
/// - Chunks editados por el jugador (ChunkChanged): se mallan de forma síncrona en el mismo frame; sus 26 vecinos, en segundo plano
///   (la luz de una edición llega hasta 15 bloques).
/// - Cada petición lleva un número de versión: un resultado obsoleto se descarta.
/// - ORIGEN FLOTANTE: los vértices son locales al chunk y la cámara es de doble precisión; cada chunk se coloca con una
///   matriz de traslación relativa a la cámara.
/// - Dibujo en pasadas: opaco, [gancho para entidades], sprites (recorte por alfa: en cruz y billboards) y agua con mezcla alfa.
/// </summary>
public sealed class VoxelWorldRenderer : IDisposable
{
    private readonly record struct MeshResult(ChunkCoord Coord, int Version, MeshData Data);

    private const int MaxBillboards = 8000;   // por frame (los vértices caben en índices de 16 bits)

    private readonly GraphicsDevice _device;
    private readonly World<ushort> _world;
    private readonly BlockRegistry _blocks;
    private readonly TextureAtlas _atlas;
    private readonly SpriteAtlas _sprites;
    private readonly BasicEffect _effect;
    private readonly AlphaTestEffect _spriteEffect;
    private readonly BasicEffect _lineEffect;
    // Efecto propio (ChunkFade.fx). Si es null se usan BasicEffect y AlphaTestEffect, sin desvanecimiento.
    private readonly Effect? _fade;
    private readonly EffectParameter? _pWorld, _pViewProjection, _pTexture, _pCutoff, _pChunkFade, _pFogColor, _pFogStart, _pFogEnd,
        _pFadeStart, _pFadeEnd, _pViewport;
    private readonly List<ChunkMesh> _fading = new();   // chunks cuyo fundido de entrada aún no ha terminado

    private readonly Dictionary<ChunkCoord, ChunkMesh> _meshes = new();
    private readonly HashSet<ChunkCoord> _built = new();      // chunks que ya tienen (o tuvieron) una malla, aunque vacía
    private readonly HashSet<ChunkCoord> _dirty = new();      // pendientes de mallar en segundo plano
    private readonly HashSet<ChunkCoord> _urgent = new();     // editados: mallar ya
    private readonly HashSet<ChunkCoord> _inFlight = new();   // trabajos lanzados y no consumidos
    private readonly Dictionary<ChunkCoord, int> _versions = new();
    private readonly List<ChunkCoord> _stale = new();
    private readonly List<(double Dist, ChunkCoord Coord)> _picked = new();
    private readonly List<(ChunkMesh Mesh, Vector3 Rel)> _visible = new();
    private readonly List<(ChunkMesh Mesh, Vector3 Rel)> _waterDraw = new();
    private readonly VertexPositionColorTexture[] _bbVerts = new VertexPositionColorTexture[MaxBillboards * 4];
    private readonly short[] _bbIdx = new short[MaxBillboards * 6];
    private readonly ConcurrentQueue<MeshResult> _results = new();
    private readonly ConcurrentQueue<Exception> _failures = new();
    private int _versionCounter;
    private long _meshTicks, _meshCount;

    public int MaxMeshingJobs { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);
    /// <summary>Tope de seguridad de mallas subidas a la GPU por frame (el límite real es UploadBudgetMs).</summary>
    public int MaxUploadsPerFrame { get; set; } = 16;
    /// <summary>Tiempo máximo por frame dedicado a subir mallas a la GPU.</summary>
    public double UploadBudgetMs { get; set; } = 2.0;
    /// <summary>
    /// Si se asigna, un chunk solo se malla en segundo plano cuando devuelve true. Úsalo con ChunkManager.AreNeighborsSettled:
    /// evita mallar el mismo chunk muchas veces mientras sus vecinos aún se están cargando.
    /// Las ediciones del jugador se mallan siempre al instante.
    /// </summary>
    public Func<ChunkCoord, bool>? CanMesh { get; set; }
    public Color FogColor { get; set; } = Color.CornflowerBlue;
    public float FogStart { get; set; } = 60f;
    public float FogEnd { get; set; } = 100f;
    /// <summary>Distancia (bloques) hasta la que se dibujan los sprites Billboard.</summary>
    public float BillboardDistance { get; set; } = 64f;

    /// <summary>Segundos de juego, para el fundido de entrada: asígnalo cada frame (gameTime.TotalGameTime.TotalSeconds).</summary>
    public double Time { get; set; }
    /// <summary>Duración del fundido de entrada de un chunk que acaba de aparecer. 0 = sin fundido.</summary>
    public double FadeInSeconds { get; set; } = 0.6;
    /// <summary>Distancias (bloques desde la cámara) donde los bloques empiezan y terminan de desvanecerse. Ver FadeBand.</summary>
    public float FadeStart { get; set; } = 1e8f;
    public float FadeEnd { get; set; } = 2e8f;
    public bool FadeActive => _fade != null;
    public int FadingChunks => _fading.Count;

    public int VisibleChunks { get; private set; }
    public int BillboardsDrawn { get; private set; }
    public int MeshedChunks => _meshes.Count;
    /// <summary>
    /// ¿Este chunk ya tiene su malla construida y subida (aunque esté vacía)? Es lo que realmente se ve; que sus DATOS estén
    /// cargados no basta, porque el mallado va después y en segundo plano. Se vuelve false al descargarlo.
    /// </summary>
    public bool HasMesh(ChunkCoord c) => _built.Contains(c);
    /// <summary>Cuántos chunks tienen ya su malla (cambia solo al construirse la primera vez o al descargarse).</summary>
    public int BuiltChunkCount => _built.Count;
    /// <summary>
    /// ¿Se ve el chunk del todo? Tiene su malla Y ha terminado de aparecer (o es un chunk vacío). Hasta entonces el terreno lejano
    /// no se puede quitar de debajo: los píxeles que aún se disuelven dejarían ver el vacío.
    /// </summary>
    public bool IsFullyVisible(ChunkCoord c) => _built.Contains(c) && (!_meshes.TryGetValue(c, out var mesh) || mesh.FadedIn);
    /// <summary>Sube cuando cambia algo que afecta a qué chunks se ven del todo (malla nueva, fundido terminado, descarga).</summary>
    public int VisibilityVersion { get; private set; }
    /// <summary>Chunks esperando o en proceso de mallado.</summary>
    public int PendingMeshes => _dirty.Count + _inFlight.Count;
    public double AverageMeshMs
    {
        get
        {
            long n = Interlocked.Read(ref _meshCount);
            return n == 0 ? 0 : Interlocked.Read(ref _meshTicks) * 1000.0 / Stopwatch.Frequency / n;
        }
    }

    /// <param name="sprites">Atlas de sprites 2D (cualquier tamaño). El renderer lo usa pero no lo libera.</param>
    /// <param name="fadeEffect">Efecto ChunkFade (Content/Effects/ChunkFade). null = sin desvanecimiento. El renderer lo usa pero no lo libera.</param>
    public VoxelWorldRenderer(GraphicsDevice device, World<ushort> world, BlockRegistry blocks, TextureAtlas atlas, SpriteAtlas sprites,
        Effect? fadeEffect = null)
    {
        _device = device; _world = world; _blocks = blocks; _atlas = atlas; _sprites = sprites;
        _effect = new BasicEffect(device) { TextureEnabled = true, VertexColorEnabled = true, LightingEnabled = false, Texture = atlas.Texture };
        _spriteEffect = new AlphaTestEffect(device)
        {
            Texture = sprites.Texture,
            VertexColorEnabled = true,
            AlphaFunction = CompareFunction.Greater,
            ReferenceAlpha = 128,   // los píxeles con alfa <= 128 no se dibujan
        };
        _lineEffect = new BasicEffect(device) { VertexColorEnabled = true };

        if (fadeEffect != null)
        {
            _fade = fadeEffect;
            _pWorld = Param(fadeEffect, "World");
            _pViewProjection = Param(fadeEffect, "ViewProjection");
            _pTexture = Param(fadeEffect, "AtlasTexture");
            _pCutoff = Param(fadeEffect, "AlphaCutoff");
            _pChunkFade = Param(fadeEffect, "ChunkFade");
            _pFogColor = Param(fadeEffect, "FogColor");
            _pFogStart = Param(fadeEffect, "FogStart");
            _pFogEnd = Param(fadeEffect, "FogEnd");
            _pFadeStart = Param(fadeEffect, "FadeStart");
            _pFadeEnd = Param(fadeEffect, "FadeEnd");
            _pViewport = Param(fadeEffect, "ViewportSize");
        }

        for (int q = 0; q < MaxBillboards; q++)
        {
            int i = q * 6, v = q * 4;
            _bbIdx[i] = (short)v; _bbIdx[i + 1] = (short)(v + 1); _bbIdx[i + 2] = (short)(v + 2);
            _bbIdx[i + 3] = (short)v; _bbIdx[i + 4] = (short)(v + 2); _bbIdx[i + 5] = (short)(v + 3);
        }

        world.ChunkLoaded += OnLoaded;
        world.ChunkChanged += OnChanged;
        world.ChunkUnloaded += OnUnloaded;
    }

    private static EffectParameter Param(Effect effect, string name) =>
        effect.Parameters[name] ?? throw new InvalidOperationException($"El efecto no tiene el parámetro '{name}'.");

    private void OnLoaded(ChunkCoord c)
    {
        _dirty.Add(c);
        // Un chunk nuevo cambia las caras visibles, el AO y la luz de sus 26 vecinos. Solo hace falta avisar a los que
        // ya tienen malla (o la están calculando): los demás tomarán los datos nuevos cuando les toque.
        MarkNeighborsDirty(c);
    }

    /// <summary>Edición: el chunk se malla ya; los vecinos en segundo plano, porque la luz de un cambio llega hasta 15 bloques.</summary>
    private void OnChanged(ChunkCoord c)
    {
        _urgent.Add(c);
        MarkNeighborsDirty(c);
    }

    private void MarkNeighborsDirty(ChunkCoord c)
    {
        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0 && dz == 0) continue;
            var nc = new ChunkCoord(c.X + dx, c.Y + dy, c.Z + dz);
            if (_world.GetChunk(nc) != null && (_built.Contains(nc) || _inFlight.Contains(nc))) _dirty.Add(nc);
        }
    }

    private void OnUnloaded(ChunkCoord c)
    {
        _dirty.Remove(c);
        _urgent.Remove(c);
        _built.Remove(c);
        _versions.Remove(c);   // invalida cualquier resultado en vuelo
        VisibilityVersion++;
        if (_meshes.Remove(c, out var mesh))
        {
            _fading.Remove(mesh);
            mesh.Dispose();
        }
    }

    public void Update(Vec3d cameraPosition)
    {
        if (_failures.TryDequeue(out var ex)) ExceptionDispatchInfo.Capture(ex).Throw();

        // Los chunks cuyo fundido de entrada ha terminado pasan a verse del todo.
        for (int i = _fading.Count - 1; i >= 0; i--)
        {
            if (Time - _fading[i].CreatedAt < FadeInSeconds) continue;
            _fading[i].FadedIn = true;
            _fading.RemoveAt(i);
            VisibilityVersion++;
        }

        // 1) Ediciones del jugador: síncrono.
        if (_urgent.Count > 0)
        {
            foreach (var c in _urgent) BuildNow(c);
            _urgent.Clear();
        }

        // 2) Resultados de los hilos de fondo: subir a la GPU con presupuesto de tiempo.
        long start = Stopwatch.GetTimestamp();
        int uploads = 0;
        while (uploads < MaxUploadsPerFrame && _results.TryDequeue(out var r))
        {
            _inFlight.Remove(r.Coord);
            if (!_versions.TryGetValue(r.Coord, out int current) || current != r.Version) continue;   // obsoleto
            if (_world.GetChunk(r.Coord) == null) continue;

            Apply(r.Coord, r.Data);
            uploads++;
            if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= UploadBudgetMs) break;
        }

        // 3) Lanzar nuevos trabajos: los más cercanos a la cámara, hasta llenar los hilos libres.
        int slots = MaxMeshingJobs - _inFlight.Count;
        if (slots <= 0) return;

        PickNearest(cameraPosition, slots);
        foreach (var (_, next) in _picked)
        {
            _dirty.Remove(next);
            var chunk = _world.GetChunk(next)!;
            var snapshot = ChunkSnapshot<ushort>.Capture(_world, chunk);   // en el hilo principal
            chunk.IsDirty = false;

            int version = ++_versionCounter;
            _versions[next] = version;
            _inFlight.Add(next);

            var blocks = _blocks; var atlas = _atlas; var sprites = _sprites;
            Task.Run(() =>
            {
                try
                {
                    long t0 = Stopwatch.GetTimestamp();
                    var data = ChunkMesher.Build(snapshot, blocks, atlas, sprites);
                    Interlocked.Add(ref _meshTicks, Stopwatch.GetTimestamp() - t0);
                    Interlocked.Increment(ref _meshCount);
                    _results.Enqueue(new MeshResult(next, version, data));
                }
                catch (Exception e) { _failures.Enqueue(e); }
            });
        }
    }

    private void BuildNow(ChunkCoord c)
    {
        var chunk = _world.GetChunk(c);
        if (chunk == null) return;
        var data = ChunkMesher.Build(ChunkSnapshot<ushort>.Capture(_world, chunk), _blocks, _atlas, _sprites);
        chunk.IsDirty = false;
        _versions[c] = ++_versionCounter;   // descarta cualquier trabajo en vuelo anterior
        _dirty.Remove(c);
        Apply(c, data);
    }

    private void Apply(ChunkCoord c, MeshData data)
    {
        _built.Add(c);
        VisibilityVersion++;

        double createdAt = Time;
        bool fadedIn = _fade == null || FadeInSeconds <= 0;
        if (_meshes.Remove(c, out var old))
        {
            createdAt = old.CreatedAt;   // una malla rehecha (edición, vecino nuevo) no vuelve a fundirse desde cero
            fadedIn = old.FadedIn;
            _fading.Remove(old);
            old.Dispose();
        }
        if (data.IsEmpty) return;

        var shape = _world.Shape;
        var mesh = new ChunkMesh(_device, data, shape.Origin(c), new Vector3(shape.SizeX, shape.SizeY, shape.SizeZ))
        {
            CreatedAt = createdAt,
            FadedIn = fadedIn,
        };
        _meshes[c] = mesh;
        if (!fadedIn) _fading.Add(mesh);
    }

    /// <summary>
    /// Elige hasta 'slots' chunks pendientes, los más cercanos a la cámara, recorriendo la lista una sola vez. Solo consulta
    /// CanMesh (cuesta 26 búsquedas) para los candidatos que entrarían en la selección.
    /// </summary>
    private void PickNearest(Vec3d cameraPosition, int slots)
    {
        _picked.Clear();
        _stale.Clear();

        foreach (var c in _dirty)
        {
            if (_world.GetChunk(c) == null) { _stale.Add(c); continue; }
            if (_inFlight.Contains(c)) continue;   // se reintentará cuando termine el trabajo actual

            double d = (ChunkCenter(c) - cameraPosition).LengthSquared;
            int pos = _picked.Count;
            while (pos > 0 && _picked[pos - 1].Dist > d) pos--;
            if (pos >= slots) continue;                       // hay ya 'slots' candidatos más cercanos
            if (CanMesh != null && !CanMesh(c)) continue;     // vecinos aún sin cargar: esperar

            _picked.Insert(pos, (d, c));
            if (_picked.Count > slots) _picked.RemoveAt(_picked.Count - 1);
        }
        foreach (var c in _stale) _dirty.Remove(c);
    }

    /// <summary>
    /// Dibuja con ORIGEN FLOTANTE: el origen de render es la posición de la cámara (en double). La vista, el frustum y la
    /// matriz de mundo de cada chunk se calculan RELATIVOS a ese origen.
    /// Con el efecto ChunkFade, los bloques se disuelven (tramado) hacia FadeEnd y aparecen poco a poco al construirse su malla.
    /// </summary>
    /// <param name="afterOpaque">Se invoca tras la pasada opaca y antes de los sprites y el agua (aquí se dibujan el terreno lejano y las entidades).</param>
    public void Draw(ICamera3D camera, Action? afterOpaque = null)
    {
        var origin = camera.Position;
        var view = camera.ViewRelativeTo(origin);
        var projection = camera.Projection;
        var viewProjection = view * projection;
        var frustum = new BoundingFrustum(viewProjection);

        PrepareFallbackEffects(view, projection);

        // Pasada 1: opaco.
        _device.BlendState = BlendState.Opaque;
        _device.DepthStencilState = DepthStencilState.Default;
        _device.RasterizerState = RasterizerState.CullCounterClockwise;
        _device.SamplerStates[0] = SamplerState.PointClamp;
        ConfigureFade(_atlas.Texture, 0f, viewProjection);

        VisibleChunks = 0;
        _visible.Clear();
        foreach (var mesh in _meshes.Values)
        {
            var rel = new Vector3(
                (float)(mesh.Origin.X - origin.X),
                (float)(mesh.Origin.Y - origin.Y),
                (float)(mesh.Origin.Z - origin.Z));
            if (!frustum.Intersects(new BoundingBox(rel + mesh.BoundsMin, rel + mesh.BoundsMax))) continue;

            SetChunk(rel, mesh, _effect);
            mesh.DrawOpaque(_device);
            VisibleChunks++;
            _visible.Add((mesh, rel));
        }

        afterOpaque?.Invoke();

        // Pasada 2: sprites (recorte por alfa, sin ordenar, a doble cara).
        DrawSprites(view, viewProjection);

        // Pasada 3: agua, de lejos a cerca, leyendo profundidad sin escribirla y visible desde ambos lados.
        _waterDraw.Clear();
        foreach (var item in _visible) if (item.Mesh.HasWater) _waterDraw.Add(item);
        if (_waterDraw.Count > 0)
        {
            _waterDraw.Sort((a, b) =>
                (b.Rel + b.Mesh.Size * 0.5f).LengthSquared().CompareTo((a.Rel + a.Mesh.Size * 0.5f).LengthSquared()));
            _device.BlendState = BlendState.NonPremultiplied;
            _device.DepthStencilState = DepthStencilState.DepthRead;
            _device.RasterizerState = RasterizerState.CullNone;
            _device.SamplerStates[0] = SamplerState.PointClamp;
            ConfigureFade(_atlas.Texture, 0f, viewProjection);
            foreach (var (mesh, rel) in _waterDraw)
            {
                SetChunk(rel, mesh, _effect);
                mesh.DrawWater(_device);
            }
        }

        _device.BlendState = BlendState.Opaque;
        _device.DepthStencilState = DepthStencilState.Default;
        _device.RasterizerState = RasterizerState.CullCounterClockwise;
    }

    private void DrawSprites(Matrix view, Matrix viewProjection)
    {
        _device.BlendState = BlendState.Opaque;
        _device.DepthStencilState = DepthStencilState.Default;
        _device.RasterizerState = RasterizerState.CullNone;
        _device.SamplerStates[0] = SamplerState.PointClamp;
        ConfigureFade(_sprites.Texture, 0.5f, viewProjection);   // alfa <= 0.5: recortado

        // Sprites en cruz: forman parte de la malla de cada chunk.
        foreach (var (mesh, rel) in _visible)
        {
            if (!mesh.HasSprites) continue;
            SetChunk(rel, mesh, _spriteEffect);
            mesh.DrawSprites(_device);
        }

        // Billboards: giran para mirar a la cámara (solo alrededor del eje vertical). Se componen cada frame, solo cerca.
        // Los ejes de la cámara en el mundo son las columnas de la matriz de vista.
        var right = new Vector3(view.M11, view.M21, view.M31);
        right.Y = 0f;
        right = right.LengthSquared() < 1e-6f ? Vector3.UnitX : Vector3.Normalize(right);

        float maxDist2 = BillboardDistance * BillboardDistance;
        int quads = 0;
        foreach (var (mesh, rel) in _visible)
        {
            if (_fade != null && !mesh.FadedIn) continue;   // aparecen cuando su chunk termina de aparecer
            var list = mesh.Billboards;
            for (int i = 0; i < list.Length && quads < MaxBillboards; i++)
            {
                var b = list[i];
                var p = rel + b.Position;
                if (p.LengthSquared() > maxDist2) continue;
                AddBillboard(quads++, p, right, b);
            }
        }

        BillboardsDrawn = quads;
        if (quads == 0) return;
        if (_fade != null)
        {
            _pWorld!.SetValue(Matrix.Identity);
            _pChunkFade!.SetValue(1f);
            _fade.CurrentTechnique.Passes[0].Apply();
        }
        else
        {
            _spriteEffect.World = Matrix.Identity;
            _spriteEffect.CurrentTechnique.Passes[0].Apply();
        }
        _device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, _bbVerts, 0, quads * 4, _bbIdx, 0, quads * 2);
    }

    // ------------------------------------------------------------------ efectos

    /// <summary>Respaldo (sin efecto propio): BasicEffect para bloques y agua, AlphaTestEffect para sprites.</summary>
    private void PrepareFallbackEffects(Matrix view, Matrix projection)
    {
        _effect.View = view;
        _effect.Projection = projection;
        _effect.FogEnabled = true;
        _effect.FogColor = FogColor.ToVector3();
        _effect.FogStart = FogStart;
        _effect.FogEnd = FogEnd;

        _spriteEffect.View = view;
        _spriteEffect.Projection = projection;
        _spriteEffect.FogEnabled = true;
        _spriteEffect.FogColor = FogColor.ToVector3();
        _spriteEffect.FogStart = FogStart;
        _spriteEffect.FogEnd = FogEnd;
    }

    /// <summary>Parámetros de ChunkFade comunes a toda una pasada (textura, recorte, niebla, franja de desvanecimiento, pantalla).</summary>
    private void ConfigureFade(Texture2D texture, float cutoff, Matrix viewProjection)
    {
        if (_fade == null) return;
        _pViewProjection!.SetValue(viewProjection);
        _pTexture!.SetValue(texture);
        _pCutoff!.SetValue(cutoff);
        _pFogColor!.SetValue(FogColor.ToVector3());
        _pFogStart!.SetValue(FogStart);
        _pFogEnd!.SetValue(FogEnd);
        _pFadeStart!.SetValue(FadeStart);
        _pFadeEnd!.SetValue(FadeEnd);
        _pViewport!.SetValue(new Vector2(_device.Viewport.Width, _device.Viewport.Height));
    }

    /// <summary>Coloca un chunk: su traslación relativa a la cámara y su fundido de aparición; deja el efecto listo para dibujar.</summary>
    private void SetChunk(Vector3 rel, ChunkMesh mesh, Effect fallback)
    {
        var world = Matrix.CreateTranslation(rel);
        if (_fade != null)
        {
            float appear = mesh.FadedIn ? 1f : FadeCurve.ChunkFade(Time, mesh.CreatedAt, FadeInSeconds);
            _pWorld!.SetValue(world);
            _pChunkFade!.SetValue(appear);
            _fade.CurrentTechnique.Passes[0].Apply();
        }
        else
        {
            ((IEffectMatrices)fallback).World = world;
            fallback.CurrentTechnique.Passes[0].Apply();
        }
    }

    private void AddBillboard(int quad, Vector3 bottomCenter, Vector3 right, BillboardInstance b)
    {
        var half = right * (b.Width * 0.5f);
        var up = new Vector3(0f, b.Height, 0f);
        var bl = bottomCenter - half;
        var br = bottomCenter + half;
        int v = quad * 4;
        _bbVerts[v] = new VertexPositionColorTexture(bl, Color.White, new Vector2(b.UvMin.X, b.UvMax.Y));
        _bbVerts[v + 1] = new VertexPositionColorTexture(br, Color.White, new Vector2(b.UvMax.X, b.UvMax.Y));
        _bbVerts[v + 2] = new VertexPositionColorTexture(br + up, Color.White, new Vector2(b.UvMax.X, b.UvMin.Y));
        _bbVerts[v + 3] = new VertexPositionColorTexture(bl + up, Color.White, new Vector2(b.UvMin.X, b.UvMin.Y));
    }

    /// <summary>Dibuja el contorno de una celda (resaltado del bloque apuntado), relativo al origen de render.</summary>
    public void DrawOutline(ICamera3D camera, CellCoord cell)
    {
        const float e = 0.003f;
        var origin = camera.Position;
        var min = new Vector3(
            (float)(cell.X - origin.X) - e,
            (float)(cell.Y - origin.Y) - e,
            (float)(cell.Z - origin.Z) - e);
        var max = min + new Vector3(1 + 2 * e);
        var c = new Color(0, 0, 0, 255);
        var p = new Vector3[8];
        for (int i = 0; i < 8; i++)
            p[i] = new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
        int[] edges = { 0,1, 2,3, 4,5, 6,7, 0,2, 1,3, 4,6, 5,7, 0,4, 1,5, 2,6, 3,7 };
        var v = new VertexPositionColor[24];
        for (int i = 0; i < 24; i++) v[i] = new VertexPositionColor(p[edges[i]], c);

        _lineEffect.World = Matrix.Identity;
        _lineEffect.View = camera.ViewRelativeTo(origin);
        _lineEffect.Projection = camera.Projection;
        _lineEffect.CurrentTechnique.Passes[0].Apply();
        _device.DrawUserPrimitives(PrimitiveType.LineList, v, 0, 12);
    }

    private Vec3d ChunkCenter(ChunkCoord c)
    {
        var s = _world.Shape; var o = s.Origin(c);
        return new Vec3d(o.X + s.SizeX * 0.5, o.Y + s.SizeY * 0.5, o.Z + s.SizeZ * 0.5);
    }

    public void Dispose()
    {
        _versions.Clear();   // los resultados en vuelo se descartarán
        foreach (var m in _meshes.Values) m.Dispose();
        _meshes.Clear();
        _effect.Dispose();
        _spriteEffect.Dispose();
        _lineEffect.Dispose();
    }
}
