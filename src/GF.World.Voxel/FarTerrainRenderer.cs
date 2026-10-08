using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using GF.Core;
using GF.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GF.World.Voxel;

/// <summary>
/// Terreno lejano por niveles de detalle (LOD). Un quadtree de tiles de 32x32 celdas alrededor de la cámara: el tile de nivel 0
/// cubre 128 bloques (muestras cada 4) y cada nivel duplica el lado y la separación entre muestras. Cerca hay mucho detalle y
/// lejos poco, y el detalle va llegando según te acercas. Las mallas se generan en hilos de fondo a partir de un
/// IFarTerrainSource (sin chunks), se guardan en una caché y se suben a la GPU con presupuesto de tiempo.
/// - Mientras llega un tile, se dibuja su antecesor más cercano ya listo (o sus hijos), así que no quedan huecos.
/// - Solo existe en un ANILLO: entre el agujero central (Hole: la zona donde ya hay chunks) y FarDistance. Los tiles enteros dentro del
///   agujero no se seleccionan y las celdas que caen dentro no se generan ni se dibujan. El agujero lo da el juego a partir de los chunks
///   realmente cargados, así que si los chunks van por detrás, el terreno lejano rellena lo que falte. Cuando el agujero cambia, solo
///   se rehacen los tiles que lo tocan (la malla anterior se sigue dibujando hasta que llega la nueva).
/// - Se dibuja DESPUÉS de los chunks y 0,3 bloques por debajo de la superficie real, para que las celdas que asoman un poco bajo los
///   chunks (en el borde del agujero) queden tapadas por la prueba de profundidad.
/// - Origen flotante: cada tile se coloca con una matriz relativa a la cámara.
/// </summary>
public sealed class FarTerrainRenderer : IDisposable
{
    public const int BaseTileBlocks = 128;   // lado de un tile de nivel 0
    public const int TileCells = 32;         // celdas por lado: muestras cada BaseTileBlocks/TileCells = 4 bloques en el nivel 0
    private const int MaxCachedTiles = 400;

    private sealed class Tile : IDisposable
    {
        public VertexBuffer Vertices { get; }
        public IndexBuffer Indices { get; }
        public int Triangles { get; }
        public float MinY { get; }
        public float MaxY { get; }
        public long LastUsed;
        /// <summary>Parte del agujero central con la que se construyó esta malla; si ya no coincide, hay que rehacerla.</summary>
        public FarHole BuiltClip { get; }

        public Tile(GraphicsDevice device, FarTileData data, FarHole clip)
        {
            BuiltClip = clip;
            Vertices = new VertexBuffer(device, VertexPositionColor.VertexDeclaration, data.Vertices.Length, BufferUsage.WriteOnly);
            Vertices.SetData(data.Vertices);
            Indices = new IndexBuffer(device, IndexElementSize.SixteenBits, data.Indices.Length, BufferUsage.WriteOnly);
            Indices.SetData(data.Indices);
            Triangles = data.TriangleCount;
            MinY = data.MinY;
            MaxY = data.MaxY;
        }

        public void Dispose() { Vertices.Dispose(); Indices.Dispose(); }
    }

    private readonly record struct DrawItem(FarTileKey Key, Tile Tile, float OffsetY);
    private readonly record struct Built(FarTileKey Key, FarTileData Data, FarHole Clip);

    private readonly GraphicsDevice _device;
    private readonly IFarTerrainSource _source;
    private readonly double _zExtent;
    private readonly BasicEffect _effect;
    private readonly Dictionary<FarTileKey, Tile> _tiles = new();
    private readonly HashSet<FarTileKey> _pending = new();
    private readonly HashSet<FarTileKey> _fallbackSeen = new();
    private readonly List<(FarTileKey Key, double Distance)> _wanted = new();
    private readonly List<(FarTileKey Key, double Distance)> _toRequest = new();
    private readonly List<DrawItem> _draw = new();
    private readonly ConcurrentQueue<Built> _results = new();
    private readonly ConcurrentQueue<Exception> _failures = new();
    private int _running;
    private long _frame, _buildTicks, _builtCount;

    /// <param name="zExtent">Extensión del mundo en Z, en bloques (de 0 a zExtent).</param>
    public FarTerrainRenderer(GraphicsDevice device, IFarTerrainSource source, double zExtent)
    {
        _device = device;
        _source = source;
        _zExtent = zExtent;
        _effect = new BasicEffect(device) { VertexColorEnabled = true, TextureEnabled = false, LightingEnabled = false };
    }

    /// <summary>Distancia máxima, en bloques, hasta la que se genera terreno.</summary>
    public double FarDistance { get; set; } = 1024;
    /// <summary>Un tile se divide en cuatro si la cámara está a menos de SplitFactor veces su lado. Más = más detalle y más tiles.</summary>
    public double SplitFactor { get; set; } = 1.6;
    /// <summary>Tiles generándose a la vez en hilos de fondo.</summary>
    public int MaxJobs { get; set; } = 2;
    public int MaxUploadsPerFrame { get; set; } = 4;
    public double UploadBudgetMs { get; set; } = 2.0;
    /// <summary>Desplazamiento vertical de todo el terreno lejano (negativo: queda bajo los chunks, que lo tapan).</summary>
    public float VerticalOffset { get; set; } = -0.5f;
    public Color FogColor { get; set; } = Color.CornflowerBlue;
    public float FogStart { get; set; } = 400f;
    public float FogEnd { get; set; } = 1000f;

    /// <summary>
    /// Zona central (en bloques del mundo) donde ya hay chunks y por tanto no hay terreno lejano. Asígnala antes de Update cada vez
    /// que cambie. default = sin agujero.
    /// </summary>
    public FarHole Hole { get; set; }

    public int TilesDrawn { get; private set; }
    public int CachedTiles => _tiles.Count;
    public int PendingTiles => _pending.Count;
    public double AverageBuildMs
    {
        get
        {
            long n = Interlocked.Read(ref _builtCount);
            return n == 0 ? 0 : Interlocked.Read(ref _buildTicks) * 1000.0 / Stopwatch.Frequency / n;
        }
    }

    public void Update(Vec3d cameraPosition)
    {
        if (_failures.TryDequeue(out var ex)) ExceptionDispatchInfo.Capture(ex).Throw();
        _frame++;

        // 1) Subir a la GPU los tiles terminados, con presupuesto de tiempo.
        long start = Stopwatch.GetTimestamp();
        int uploads = 0;
        while (uploads < MaxUploadsPerFrame && _results.TryDequeue(out var built))
        {
            _pending.Remove(built.Key);
            // Si el agujero se encogió mientras se generaba (los chunks se descargaron), esta malla carece de una parte que ya no
            // cubren los chunks: dejaría un vacío. Se descarta y el tile se vuelve a pedir con el agujero actual.
            if (!ClipFor(built.Key).Contains(built.Clip)) continue;
            if (_tiles.TryGetValue(built.Key, out var old)) old.Dispose();
            _tiles[built.Key] = new Tile(_device, built.Data, built.Clip) { LastUsed = _frame };
            uploads++;
            if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= UploadBudgetMs) break;
        }

        // 2) Qué tiles quiere el quadtree, y qué se dibuja mientras llegan.
        FarTileSelector.Select(cameraPosition.X, cameraPosition.Z, FarDistance, BaseTileBlocks, SplitFactor, _zExtent, _wanted, Hole);
        int top = FarTileSelector.TopLevel(FarDistance, BaseTileBlocks);

        _draw.Clear();
        _toRequest.Clear();
        _fallbackSeen.Clear();
        foreach (var (key, distance) in _wanted)
        {
            if (_tiles.TryGetValue(key, out var tile))
            {
                tile.LastUsed = _frame;
                _draw.Add(new DrawItem(key, tile, 0f));
                // El agujero se movió y este tile lo toca: hay que rehacerlo (mientras tanto se sigue dibujando el anterior).
                if (tile.BuiltClip != ClipFor(key) && !_pending.Contains(key)) _toRequest.Add((key, distance));
                continue;
            }

            if (!_pending.Contains(key)) _toRequest.Add((key, distance));

            // Sin hueco mientras llega: el antecesor más cercano ya listo (bajado un poco para que lo tapen los tiles finos
            // vecinos que sí están) o, si el tile quiere ser más grueso, sus cuatro hijos.
            bool covered = false;
            var ancestor = key;
            for (int level = key.Level; level < top && !covered; level++)
            {
                ancestor = ancestor.Parent;
                if (!_tiles.TryGetValue(ancestor, out var atile)) continue;
                if (_fallbackSeen.Add(ancestor))
                {
                    atile.LastUsed = _frame;
                    _draw.Add(new DrawItem(ancestor, atile, -2f * (ancestor.Level - key.Level)));
                }
                covered = true;
            }
            if (!covered && key.Level > 0 && key.Children.All(c => _tiles.ContainsKey(c)))
            {
                foreach (var child in key.Children)
                {
                    var ctile = _tiles[child];
                    ctile.LastUsed = _frame;
                    if (_fallbackSeen.Add(child)) _draw.Add(new DrawItem(child, ctile, 0f));
                }
            }
        }

        // 3) Lanzar la generación de los que faltan, primero los más cercanos.
        _toRequest.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        foreach (var (key, _) in _toRequest)
        {
            if (Volatile.Read(ref _running) >= MaxJobs) break;
            Launch(key);
        }

        Evict();
    }

    private FarHole ClipFor(FarTileKey key)
    {
        long size = (long)BaseTileBlocks << key.Level;
        return Hole.ClipTo((long)key.X * size, (long)key.Z * size, size);
    }

    private void Launch(FarTileKey key)
    {
        _pending.Add(key);
        Interlocked.Increment(ref _running);
        var source = _source;
        var hole = Hole;
        var clip = ClipFor(key);
        Task.Run(() =>
        {
            try
            {
                long t0 = Stopwatch.GetTimestamp();
                var data = FarTileBuilder.Build(source, key, BaseTileBlocks, TileCells, hole);
                Interlocked.Add(ref _buildTicks, Stopwatch.GetTimestamp() - t0);
                Interlocked.Increment(ref _builtCount);
                _results.Enqueue(new Built(key, data, clip));
            }
            catch (Exception e) { _failures.Enqueue(e); }
            finally { Interlocked.Decrement(ref _running); }
        });
    }

    /// <summary>Descarta los tiles menos usados recientemente cuando la caché se pasa del límite.</summary>
    private void Evict()
    {
        if (_tiles.Count <= MaxCachedTiles) return;
        var victims = _tiles.Where(kv => kv.Value.LastUsed < _frame)
                            .OrderBy(kv => kv.Value.LastUsed)
                            .Take(_tiles.Count - MaxCachedTiles)
                            .Select(kv => kv.Key)
                            .ToList();
        foreach (var key in victims)
        {
            _tiles[key].Dispose();
            _tiles.Remove(key);
        }
    }

    /// <summary>Dibujar tras la pasada opaca de los chunks (la prueba de profundidad oculta el terreno lejano donde hay bloques).</summary>
    public void Draw(ICamera3D camera)
    {
        TilesDrawn = 0;
        if (_draw.Count == 0) return;

        var origin = camera.Position;
        var view = camera.ViewRelativeTo(origin);
        var projection = camera.Projection;
        var frustum = new BoundingFrustum(view * projection);

        _effect.View = view;
        _effect.Projection = projection;
        _effect.FogEnabled = true;
        _effect.FogColor = FogColor.ToVector3();
        _effect.FogStart = FogStart;
        _effect.FogEnd = FogEnd;

        _device.BlendState = BlendState.Opaque;
        _device.DepthStencilState = DepthStencilState.Default;
        _device.RasterizerState = RasterizerState.CullNone;

        foreach (var item in _draw)
        {
            long size = (long)BaseTileBlocks << item.Key.Level;
            double tileX = (double)item.Key.X * size, tileZ = (double)item.Key.Z * size;
            var rel = new Vector3((float)(tileX - origin.X), VerticalOffset + item.OffsetY - (float)origin.Y, (float)(tileZ - origin.Z));
            var box = new BoundingBox(rel + new Vector3(0, item.Tile.MinY, 0), rel + new Vector3(size, item.Tile.MaxY, size));
            if (!frustum.Intersects(box)) continue;

            _effect.World = Matrix.CreateTranslation(rel);
            _effect.CurrentTechnique.Passes[0].Apply();
            _device.SetVertexBuffer(item.Tile.Vertices);
            _device.Indices = item.Tile.Indices;
            _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, item.Tile.Triangles);
            TilesDrawn++;
        }
    }

    public void Dispose()
    {
        foreach (var tile in _tiles.Values) tile.Dispose();
        _tiles.Clear();
        _effect.Dispose();
    }
}
