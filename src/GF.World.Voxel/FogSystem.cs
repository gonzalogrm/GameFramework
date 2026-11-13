using System.Diagnostics;
using GF.Core;
using Microsoft.Xna.Framework;

namespace GF.World.Voxel;

/// <summary>Busca el suelo bajo una columna del mundo para asentar un banco de niebla. hintY = altura de la cámara (para elegir superficie en cuevas y laderas).</summary>
public delegate bool FogGroundQuery(int wx, int wz, int hintY, out int groundY);

/// <summary>Luz (RGB 0..1) en una celda del mundo, para teñir la niebla (de noche, en cuevas, junto a lámparas). false = no se conoce.</summary>
public delegate bool FogLightQuery(int wx, int wy, int wz, out Vector3 light);

/// <summary>Parámetros de la niebla de bloques (ver FogSystem). Todas las distancias en bloques.</summary>
public sealed class FogOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>Radio alrededor de la cámara donde puede haber niebla (se desvanece hacia el borde).</summary>
    public float Radius { get; set; } = 64f;
    /// <summary>Lado de cada bloque de niebla.</summary>
    public int CellSize { get; set; } = 2;
    /// <summary>Altura del banco sobre el suelo.</summary>
    public float BankHeight { get; set; } = 6f;
    /// <summary>Valor del ruido (0..1) por debajo del cual no hay niebla.</summary>
    public float Threshold { get; set; } = 0.55f;
    /// <summary>Opacidad máxima de un bloque en el centro de un banco (0..1).</summary>
    public float MaxAlpha { get; set; } = 0.45f;
    /// <summary>Tamaño típico de un banco (el del ruido de menor frecuencia).</summary>
    public float NoiseScale { get; set; } = 48f;
    /// <summary>Velocidad a la que evoluciona el ruido (unidades de ruido por segundo; el eje del tiempo).</summary>
    public float EvolutionSpeed { get; set; } = 0.03f;
    /// <summary>Cuánto tarda un bloque en aparecer o desaparecer: densidad por segundo.</summary>
    public float FadeSpeed { get; set; } = 0.6f;
    /// <summary>0..1: cuánto depende la altura del banco de su densidad. 0 = todos del alto BankHeight; 1 = el borde tenue es plano y solo el centro denso alcanza BankHeight.</summary>
    public float HeightVariation { get; set; } = 0.65f;
    /// <summary>Cuánto esconde la niebla: la visibilidad entre dos puntos es exp(-Concealment x suma de densidad a lo largo del rayo, en bloques). 0 = no esconde.</summary>
    public float Concealment { get; set; } = 0.12f;
    /// <summary>Fracción mínima del alcance de visión de un enemigo dentro de niebla muy densa (0 = puede quedar ciego del todo).</summary>
    public float MinSight { get; set; } = 0.2f;
    public float RefreshInterval { get; set; } = 0.25f;
    /// <summary>Capacidad del pool: columnas de niebla activas a la vez.</summary>
    public int MaxColumns { get; set; } = 6000;
    /// <summary>Tope de búsquedas de suelo por refresco (reparte el coste al moverse).</summary>
    public int MaxQueriesPerRefresh { get; set; } = 128;
    /// <summary>Columnas activas cuyo suelo se vuelve a comprobar por refresco (por si se edita el terreno).</summary>
    public int RecheckPerRefresh { get; set; } = 32;

    public int Layers => Math.Max(1, (int)Math.Ceiling(BankHeight / Math.Max(1, CellSize)));
}

/// <summary>Una columna de bloques de niebla (Layers bloques apilados desde el suelo). Vive en un pool: se reutiliza, no se crea ni se destruye.</summary>
public struct FogColumn
{
    /// <summary>Celda de la malla de niebla: coordenada del mundo / CellSize.</summary>
    public int Ix, Iz;
    /// <summary>Primera celda sobre el suelo: donde empieza el banco.</summary>
    public int GroundY;
    /// <summary>Densidad actual (0..1): se acerca poco a poco a Target, así los bloques aparecen y desaparecen suavemente.</summary>
    public float Density;
    /// <summary>Densidad que pide el ruido ahora (0 = por debajo del umbral).</summary>
    public float Target;
    /// <summary>Luz de la columna (RGB 0..1), suavizada; solo si el sistema tiene LightQuery.</summary>
    public Vector3 Light;
    internal Vector3 LightTarget;
    internal int Seen;
    internal bool Dirty;   // el terreno cambió cerca: hay que volver a buscar su suelo
}

/// <summary>
/// Niebla hecha de bloques semitransparentes. Es solo un campo de densidad sobre el mundo: NO son celdas del mundo, así que no afectan a la colisión, al
/// pathfinding, a la luz ni al guardado, y se atraviesan (DensityAt permite consultarla desde el juego). Los bloques no se mueven: cada columna se queda en su
/// sitio y su densidad sube o baja según un ruido Perlin 3D (x, z, tiempo) que evoluciona despacio; por debajo del umbral es cero y la columna vuelve al pool.
/// Las columnas se asientan sobre el suelo (superficie, cima de una montaña, desierto, suelo de una cueva...) mediante FogGroundQuery.
/// POOL: un array de tamaño fijo con lista de libres; activar o liberar una columna no reserva memoria. Solo hilo principal.
/// </summary>
public sealed class FogSystem
{
    private readonly FogOptions _o;
    private readonly INoise _noise;
    private readonly FogGroundQuery _ground;
    private readonly FogColumn[] _cols;
    private readonly int[] _free;
    private readonly int[] _active;
    private readonly Dictionary<long, int> _map;
    private int _freeCount, _activeCount, _epoch, _recheckCursor;
    private float _timer;
    private double _refreshMs;

    public FogSystem(FogOptions options, INoise noise, FogGroundQuery ground)
    {
        _o = options;
        _noise = noise;
        _ground = ground;
        int cap = Math.Max(16, options.MaxColumns);
        _cols = new FogColumn[cap];
        _free = new int[cap];
        _active = new int[cap];
        for (int i = 0; i < cap; i++) _free[i] = cap - 1 - i;
        _freeCount = cap;
        _map = new Dictionary<long, int>(cap);
        _timer = options.RefreshInterval;   // refresca en la primera llamada
    }

    public FogOptions Options => _o;
    public int Layers => _o.Layers;
    public int Capacity => _cols.Length;
    public int ActiveCount => _activeCount;
    public int Queries { get; private set; }
    /// <summary>Si se asigna, cada columna toma el color de la luz de su celda (cuevas oscuras, lámparas de colores, noche). Sin ella, el dibujo usa un tinte global.</summary>
    public FogLightQuery? LightQuery { get; set; }
    public bool UsesLight => LightQuery != null;

    /// <summary>La columna activa número i (0..ActiveCount-1), para dibujarla.</summary>
    public ref readonly FogColumn Active(int i) => ref _cols[_active[i]];

    /// <summary>Factor de altura de la capa (0 = pegada al suelo): los bloques de arriba son más tenues.</summary>
    public float LayerFactor(int layer) => 1f - 0.8f * ((layer + 0.5f) / Layers);

    /// <summary>Opacidad (0..MaxAlpha) de la capa 'layer' de una columna.</summary>
    public float LayerAlpha(in FogColumn c, int layer) => c.Density * LayerFactor(layer) * _o.MaxAlpha * LayerReach(c.Density, layer);

    /// <summary>0..1: cuánto de la capa 'layer' llega el banco. Los bancos densos son más altos; la capa de más arriba aparece poco a poco (sin saltos).</summary>
    private float LayerReach(float density, int layer)
    {
        float reach = (1f - _o.HeightVariation + _o.HeightVariation * density) * Layers;
        return Math.Clamp(reach - layer, 0f, 1f);
    }

    /// <summary>El terreno cambió en esta celda del mundo (se puso o rompió un bloque): el suelo de su columna de niebla se vuelve a buscar en el próximo refresco.</summary>
    public void Invalidate(int wx, int wz)
    {
        int cs = Math.Max(1, _o.CellSize);
        if (_map.TryGetValue(Key((int)Math.Floor(wx / (double)cs), (int)Math.Floor(wz / (double)cs)), out int slot)) _cols[slot].Dirty = true;
    }

    public string DebugText => _o.Enabled
        ? $"Niebla: {_activeCount} columnas activas (pool {_activeCount}/{_cols.Length}), {Layers} capas, {Queries} busquedas de suelo, refresco {_refreshMs:0.00} ms"
        : "Niebla: desactivada";

    /// <summary>Densidad de niebla (0..1) en una posición del mundo; 0 si no hay banco ahí. Para el juego (visibilidad, IA...): no bloquea nada.</summary>
    public float DensityAt(Vec3d p)
    {
        double cs = _o.CellSize;
        int ix = (int)Math.Floor(p.X / cs), iz = (int)Math.Floor(p.Z / cs);
        if (!_map.TryGetValue(Key(ix, iz), out int slot)) return 0f;
        ref var c = ref _cols[slot];
        double h = p.Y - c.GroundY;
        if (h < 0 || h >= _o.BankHeight) return 0f;
        int layer = Math.Min(Layers - 1, (int)(h / cs));
        return c.Density * LayerFactor(layer) * LayerReach(c.Density, layer);
    }

    /// <summary>
    /// Visibilidad (1 = despejado, 0 = opaco) entre dos puntos del mundo a través de la niebla: integra la densidad a lo largo del segmento. Es lo que usa la IA para
    /// que la niebla ESCONDA (un enemigo ve menos lejos si hay niebla entre él y el objetivo). La niebla no bloquea movimiento ni pathfinding: solo la vista.
    /// </summary>
    public float Transmittance(Vec3d from, Vec3d to)
    {
        if (!_o.Enabled || _activeCount == 0 || _o.Concealment <= 0f) return 1f;
        double dx = to.X - from.X, dy = to.Y - from.Y, dz = to.Z - from.Z;
        double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len < 0.01) return 1f;
        int steps = Math.Max(1, (int)Math.Ceiling(len / Math.Max(0.5, _o.CellSize * 0.5)));
        double inv = 1.0 / steps;
        float stepLen = (float)(len * inv), sum = 0f;
        for (int i = 0; i < steps; i++)
        {
            double t = (i + 0.5) * inv;
            sum += DensityAt(new Vec3d(from.X + dx * t, from.Y + dy * t, from.Z + dz * t)) * stepLen;
        }
        return MathF.Exp(-_o.Concealment * sum);
    }

    /// <summary>Multiplicador (MinSight..1) del alcance de visión entre dos puntos: la niebla lo reduce, nunca por debajo de MinSight.</summary>
    public float Sight(Vec3d from, Vec3d to) => Math.Max(_o.MinSight, Transmittance(from, to));

    private static long Key(int ix, int iz) => ((long)ix << 32) | (uint)iz;

    /// <param name="time">Segundos de juego (eje del tiempo del ruido).</param>
    public void Update(float dt, Vec3d camera, double time)
    {
        if (!_o.Enabled)
        {
            while (_activeCount > 0) Release(_activeCount - 1);
            return;
        }

        _timer += dt;
        if (_timer >= _o.RefreshInterval)
        {
            _timer = 0;
            long t0 = Stopwatch.GetTimestamp();
            Refresh(camera, time);
            _refreshMs += (Stopwatch.GetElapsedTime(t0).TotalMilliseconds - _refreshMs) * 0.1;
        }

        // Cada frame: la densidad de cada columna se acerca a su objetivo; las que llegan a cero vuelven al pool.
        float step = _o.FadeSpeed * dt;
        for (int i = _activeCount - 1; i >= 0; i--)
        {
            ref var c = ref _cols[_active[i]];
            c.Density += Math.Clamp(c.Target - c.Density, -step, step);
            c.Light = Vector3.Lerp(c.Light, c.LightTarget, Math.Min(1f, 4f * dt));
            if (c.Target <= 0f && c.Density <= 0.001f) Release(i);
        }
    }

    private void Refresh(Vec3d cam, double time)
    {
        double cs = _o.CellSize;
        int cix = (int)Math.Floor(cam.X / cs), ciz = (int)Math.Floor(cam.Z / cs);
        int rc = Math.Max(1, (int)Math.Ceiling(_o.Radius / cs));
        float scale = (float)cs / Math.Max(1f, _o.NoiseScale);
        float t = (float)(time * _o.EvolutionSpeed);
        int hint = (int)Math.Floor(cam.Y);
        float th = _o.Threshold, span = Math.Max(0.01f, 1f - th);
        int epoch = ++_epoch, queries = 0;

        for (int iz = ciz - rc; iz <= ciz + rc; iz++)
        for (int ix = cix - rc; ix <= cix + rc; ix++)
        {
            int dx = ix - cix, dz = iz - ciz, d2 = dx * dx + dz * dz;
            if (d2 > rc * rc) continue;

            float edge = 1f - FadeCurve.Smoothstep(0.65f, 1f, MathF.Sqrt(d2) / rc);   // se apaga hacia el borde del radio
            float v = _noise.Fbm3((ix + 0.5f) * scale, (iz + 0.5f) * scale, t, 2);       // -1..1, evoluciona con el tiempo
            float u = Math.Clamp(0.5f + v * 0.9f, 0f, 1f);
            float target = Math.Clamp((u - th) / span, 0f, 1f) * edge;               // 0 por debajo del umbral

            if (_map.TryGetValue(Key(ix, iz), out int slot))
            {
                ref var c = ref _cols[slot];
                c.Target = target;
                c.Seen = epoch;
                continue;
            }
            if (target <= 0f || queries >= _o.MaxQueriesPerRefresh || _freeCount == 0) continue;

            queries++;
            if (!_ground(ix * _o.CellSize, iz * _o.CellSize, hint, out int gy)) continue;
            slot = _free[--_freeCount];
            var light = Vector3.One;
            LightQuery?.Invoke(ix * _o.CellSize, gy, iz * _o.CellSize, out light);
            _cols[slot] = new FogColumn { Ix = ix, Iz = iz, GroundY = gy, Density = 0f, Target = target, Seen = epoch, Light = light, LightTarget = light };
            _active[_activeCount++] = slot;
            _map[Key(ix, iz)] = slot;
        }

        // Las que quedaron fuera del radio se apagan.
        for (int i = 0; i < _activeCount; i++)
        {
            ref var c = ref _cols[_active[i]];
            if (c.Seen != epoch) c.Target = 0f;
        }

        // Suelo: primero las columnas cuyo terreno se editó (Invalidate); después unas pocas por refresco, por si acaso. La búsqueda parte del suelo anterior,
        // así una columna se queda en SU superficie (la de la cueva, la de la ladera) aunque la cámara cambie de altura.
        for (int i = 0; i < _activeCount; i++)
        {
            ref var c = ref _cols[_active[i]];
            if (!c.Dirty) continue;
            c.Dirty = false;
            if (_ground(c.Ix * _o.CellSize, c.Iz * _o.CellSize, c.GroundY, out int gy)) c.GroundY = gy;
            else c.Target = 0f;
        }
        int n = Math.Min(_o.RecheckPerRefresh, _activeCount);
        for (int k = 0; k < n; k++)
        {
            if (_recheckCursor >= _activeCount) _recheckCursor = 0;
            ref var c = ref _cols[_active[_recheckCursor++]];
            if (_ground(c.Ix * _o.CellSize, c.Iz * _o.CellSize, c.GroundY, out int gy)) c.GroundY = gy;
            else c.Target = 0f;
            queries++;
        }
        // Luz de cada columna (del mapa de luz: cielo, ambiente y bloques emisores): el ciclo día/noche y las lámparas tiñen la niebla.
        var lq = LightQuery;
        if (lq != null)
            for (int i = 0; i < _activeCount; i++)
            {
                ref var c = ref _cols[_active[i]];
                if (lq(c.Ix * _o.CellSize, c.GroundY, c.Iz * _o.CellSize, out var l)) c.LightTarget = l;
            }
        Queries = queries;
    }

    private void Release(int activeIndex)
    {
        int slot = _active[activeIndex];
        _map.Remove(Key(_cols[slot].Ix, _cols[slot].Iz));
        _active[activeIndex] = _active[--_activeCount];
        _free[_freeCount++] = slot;
    }
}
