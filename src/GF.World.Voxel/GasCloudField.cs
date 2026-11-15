using GF.Core;

namespace GF.World.Voxel;

/// <summary>¿Puede el gas ocupar esta celda del mundo? (false = sólida o chunk sin cargar).</summary>
public delegate bool GasOpenQuery(int x, int y, int z);

/// <summary>Parámetros de una nube de gas por autómata celular (granadas de humo, esporas...).</summary>
public sealed class GasCloudOptions
{
    /// <summary>Concentración a la que una celda se ve con opacidad total (MaxAlpha). Más baja = la nube se ve más densa.</summary>
    public float FullDensity { get; set; } = 10f;
    /// <summary>0..1: velocidad de difusión (1 = la máxima estable).</summary>
    public float Spread { get; set; } = 1f;
    /// <summary>Fracción de gas que se pierde por segundo (la nube se disipa del todo con el tiempo).</summary>
    public float Dissipation { get; set; } = 0.03f;
    /// <summary>0..0.5: el gas sube (&gt; 0) o baja (&lt; 0) algo más fácil que se expande hacia los lados.</summary>
    public float Buoyancy { get; set; } = 0.15f;
    /// <summary>Capacidad del pool: celdas de gas activas a la vez (todas las nubes).</summary>
    public int MaxCells { get; set; } = 12000;
    /// <summary>Concentración mínima: no se crea una celda con menos, y se elimina por debajo de la mitad.</summary>
    public float MinConcentration { get; set; } = 0.1f;
    public float TickInterval { get; set; } = 0.15f;
    public float MaxAlpha { get; set; } = 0.6f;
    /// <summary>Cuánto esconde (ver FogOptions.Concealment) y alcance mínimo de visión de un enemigo dentro.</summary>
    public float Concealment { get; set; } = 0.35f;
    public float MinSight { get; set; } = 0.1f;
}

/// <summary>
/// Nube de gas como AUTÓMATA CELULAR 3D disperso. Se suelta una cantidad de gas en unas pocas celdas (Add) y cada TickInterval cada celda reparte gas a sus 6 vecinas
/// libres que tengan menos (difusión que conserva el total: lo que sale de una entra en otra), con algo de flotabilidad; el gas no atraviesa bloques opacos ni chunks sin
/// cargar, y se disipa poco a poco hasta desaparecer. Solo existen las celdas con gas (diccionario + pool de tamaño fijo con lista de libres: no reserva memoria al
/// expandirse). Es solo un campo de densidad: no afecta a colisión ni pathfinding; DensityAt / Sight sirven a las mecánicas del juego. Solo hilo principal.
/// </summary>
public sealed class GasCloudField
{
    private struct Cell { public int X, Y, Z; public float Cur, Next; }

    private static readonly int[] Dx = { 1, -1, 0, 0, 0, 0 }, Dy = { 0, 0, 1, -1, 0, 0 }, Dz = { 0, 0, 0, 0, 1, -1 };

    private readonly GasCloudOptions _o;
    private readonly GasOpenQuery _isOpen;
    private readonly Cell[] _cells;
    private readonly int[] _free, _active;
    private readonly Dictionary<long, int> _map;
    private int _freeCount, _activeCount;
    private float _acc;

    public GasCloudField(GasCloudOptions options, GasOpenQuery isOpen)
    {
        _o = options;
        _isOpen = isOpen;
        int cap = Math.Max(64, options.MaxCells);
        _cells = new Cell[cap];
        _free = new int[cap];
        _active = new int[cap];
        for (int i = 0; i < cap; i++) _free[i] = cap - 1 - i;
        _freeCount = cap;
        _map = new Dictionary<long, int>(cap);
    }

    public GasCloudOptions Options => _o;
    public int ActiveCount => _activeCount;
    /// <summary>Gas que queda en total (suma de concentraciones).</summary>
    public float TotalAmount { get; private set; }
    public string DebugText => $"Humo: {_activeCount} celdas (pool {_activeCount}/{_cells.Length}), {TotalAmount:0} de gas";

    private static long Key(int x, int y, int z) => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);

    private int Rent(int x, int y, int z)
    {
        if (_freeCount == 0) return -1;
        int slot = _free[--_freeCount];
        _cells[slot] = new Cell { X = x, Y = y, Z = z };
        _active[_activeCount++] = slot;
        _map[Key(x, y, z)] = slot;
        return slot;
    }

    private void Release(int activeIndex)
    {
        int slot = _active[activeIndex];
        _map.Remove(Key(_cells[slot].X, _cells[slot].Y, _cells[slot].Z));
        _active[activeIndex] = _active[--_activeCount];
        _free[_freeCount++] = slot;
    }

    /// <summary>Suelta 'amount' de gas repartido por igual entre las celdas libres a menos de 'radius' bloques de 'pos' (un número determinado de voxels iniciales).</summary>
    public void Add(Vec3d pos, float amount, float radius = 0f)
    {
        int cx = (int)Math.Floor(pos.X), cy = (int)Math.Floor(pos.Y), cz = (int)Math.Floor(pos.Z);
        var cells = new List<(int X, int Y, int Z)>();
        int r = (int)Math.Ceiling(radius);
        for (int dz = -r; dz <= r; dz++)
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
            if (dx * dx + dy * dy + dz * dz <= radius * radius && _isOpen(cx + dx, cy + dy, cz + dz)) cells.Add((cx + dx, cy + dy, cz + dz));
        if (cells.Count == 0)   // sin hueco alrededor: la primera celda libre por encima
            for (int k = 0; k < 4 && cells.Count == 0; k++) if (_isOpen(cx, cy + k, cz)) cells.Add((cx, cy + k, cz));
        if (cells.Count == 0) return;

        float each = amount / cells.Count;
        foreach (var (x, y, z) in cells)
        {
            if (!_map.TryGetValue(Key(x, y, z), out int slot)) slot = Rent(x, y, z);
            if (slot < 0) return;
            _cells[slot].Cur += each;
            _cells[slot].Next += each;
        }
    }

    public void Clear()
    {
        while (_activeCount > 0) Release(_activeCount - 1);
        TotalAmount = 0f;
    }

    public void Update(float dt)
    {
        if (_activeCount == 0) { _acc = 0f; return; }
        _acc += dt;
        int steps = 0;
        while (_acc >= _o.TickInterval && steps < 2) { Step(_o.TickInterval); _acc -= _o.TickInterval; steps++; }
        if (_acc > _o.TickInterval * 2f) _acc = 0f;   // se va atrasando: se descarta el resto
    }

    private void Step(float dt)
    {
        // Coeficiente de difusión: con 6 vecinas y flotabilidad b, el reparto máximo (6 x K x (1 + b)) no debe superar el gas de la celda.
        float k = 0.14f * Math.Clamp(_o.Spread, 0f, 1f);
        float up = 1f + _o.Buoyancy, down = 1f - _o.Buoyancy, min = _o.MinConcentration;
        int n = _activeCount;   // las celdas nuevas de este paso no emiten hasta el siguiente

        for (int i = 0; i < n; i++)
        {
            int si = _active[i];
            float v = _cells[si].Cur;
            if (v < min) continue;
            int x = _cells[si].X, y = _cells[si].Y, z = _cells[si].Z;
            for (int d = 0; d < 6; d++)
            {
                int nx = x + Dx[d], ny = y + Dy[d], nz = z + Dz[d];
                float other = 0f;
                bool exists = _map.TryGetValue(Key(nx, ny, nz), out int sj);
                if (exists) other = _cells[sj].Cur;
                float diff = v - other;
                if (diff <= 0f) continue;

                float flow = k * (d == 2 ? up : d == 3 ? down : 1f) * diff;
                if (!exists)
                {
                    if (flow < min || !_isOpen(nx, ny, nz)) continue;
                    sj = Rent(nx, ny, nz);
                    if (sj < 0) continue;   // pool lleno: el gas se queda donde está
                }
                _cells[si].Next -= flow;
                _cells[sj].Next += flow;
            }
        }

        float decay = MathF.Exp(-_o.Dissipation * dt), keep = min * 0.5f, total = 0f;
        for (int i = _activeCount - 1; i >= 0; i--)
        {
            ref var c = ref _cells[_active[i]];
            c.Cur = c.Next * decay;
            c.Next = c.Cur;
            if (c.Cur < keep) { Release(i); continue; }
            total += c.Cur;
        }
        TotalAmount = total;
    }

    /// <summary>La celda activa número i (0..ActiveCount-1) y su densidad (0..1), para dibujarla.</summary>
    public void GetCell(int i, out int x, out int y, out int z, out float density)
    {
        ref var c = ref _cells[_active[i]];
        x = c.X; y = c.Y; z = c.Z;
        density = Math.Min(1f, c.Cur / _o.FullDensity);
    }

    /// <summary>Densidad del gas (0..1) en una posición del mundo.</summary>
    public float DensityAt(Vec3d p)
    {
        if (_activeCount == 0) return 0f;
        if (!_map.TryGetValue(Key((int)Math.Floor(p.X), (int)Math.Floor(p.Y), (int)Math.Floor(p.Z)), out int slot)) return 0f;
        return Math.Min(1f, _cells[slot].Cur / _o.FullDensity);
    }

    /// <summary>Visibilidad (1 = despejado, 0 = opaco) entre dos puntos a través del gas. Solo la vista: no bloquea movimiento.</summary>
    public float Transmittance(Vec3d from, Vec3d to)
    {
        if (_activeCount == 0 || _o.Concealment <= 0f) return 1f;
        double dx = to.X - from.X, dy = to.Y - from.Y, dz = to.Z - from.Z;
        double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len < 0.01) return 1f;
        int steps = Math.Max(1, (int)Math.Ceiling(len / 0.5));
        double inv = 1.0 / steps;
        float stepLen = (float)(len * inv), sum = 0f;
        for (int i = 0; i < steps; i++)
        {
            double t = (i + 0.5) * inv;
            sum += DensityAt(new Vec3d(from.X + dx * t, from.Y + dy * t, from.Z + dz * t)) * stepLen;
        }
        return MathF.Exp(-_o.Concealment * sum);
    }

    public float Sight(Vec3d from, Vec3d to) => Math.Max(_o.MinSight, Transmittance(from, to));
}
