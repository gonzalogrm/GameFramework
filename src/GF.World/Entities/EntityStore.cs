using GF.Core;
using GF.World.Map;

namespace GF.World.Entities;

/// <summary>
/// Almacén de entidades con tres índices: por id, por columna de chunks (para activar/desactivar y consultas
/// por radio) y por región (para la simulación aproximada cercana y los marcadores del mapa).
///
/// Simulación por niveles:
///  - Active: la columna de chunks de la entidad está cargada.
///  - Approximate: sin chunks cargados pero con destino. Avanza en línea recta con "puesta al día" por tiempo
///    transcurrido: las cercanas al foco (jugador) cada NearInterval; el resto por turnos con un presupuesto por frame.
///  - Dormant: sin destino y sin chunks; no cuesta nada.
/// Al cargarse un chunk, las entidades de su columna se ponen al día antes de activarse.
///
/// Hilos: solo hilo principal.
/// </summary>
public sealed class EntityStore
{
    private readonly ChunkShape _shape;
    private readonly WorldScale _scale;
    private readonly Dictionary<long, Entity> _all = new();
    private readonly Dictionary<ChunkCoord, HashSet<Entity>> _byColumn = new();
    private readonly Dictionary<(int, int), HashSet<Entity>> _byRegion = new();
    private readonly Dictionary<ChunkCoord, int> _loadedColumns = new();
    private readonly HashSet<Entity> _active = new();
    private readonly HashSet<Entity> _approx = new();
    private readonly List<Entity> _scratch = new();
    private readonly List<Entity> _rayScratch = new();
    private Entity[] _farSnapshot = Array.Empty<Entity>();
    private int _farCursor;
    private double _nearTimer;
    private long _nextId = 1;

    public EntityStore(ChunkShape chunkShape, WorldScale scale)
    {
        _shape = chunkShape;
        _scale = scale;
    }

    /// <summary>
    /// Tipos de entidad. Al crear una entidad se le asigna su definición (y con ella su prototipo de propiedades). Asígnalo antes de
    /// crear o importar entidades; sin él las entidades funcionan igual, pero sin propiedades.
    /// </summary>
    public Registry<EntityDef>? Definitions { get; set; }

    /// <summary>Reloj de simulación en segundos.</summary>
    public double Time { get; private set; }

    /// <summary>Radio, en regiones, alrededor del foco donde la simulación aproximada es frecuente.</summary>
    public int NearRegionRadius { get; set; } = 3;
    public double NearInterval { get; set; } = 0.5;
    /// <summary>Máximo de entidades aproximadas lejanas que se ponen al día por llamada a Update.</summary>
    public int MaxFarPerUpdate { get; set; } = 500;

    public int Count => _all.Count;

    /// <summary>¿Sigue esta entidad en el almacén? (false si se eliminó)</summary>
    public bool Contains(Entity entity) => _all.TryGetValue(entity.Id, out var found) && ReferenceEquals(found, entity);
    public int ActiveCount => _active.Count;
    public int ApproximateCount => _approx.Count;
    public IReadOnlyCollection<Entity> All => _all.Values;
    public IReadOnlyCollection<Entity> Active => _active;

    public event Action<Entity>? Activated;
    public event Action<Entity>? Deactivated;
    /// <summary>Se lanza al llegar al destino (aproximadas) o al llamar a NotifyArrived (activas). El manejador puede fijar otro destino.</summary>
    public event Action<Entity>? Arrived;

    // ------------------------------------------------------------------ ciclo de vida

    public Entity Spawn(ushort typeId, Vec3d position, double speed, string? tag = null) =>
        Create(_nextId++, typeId, position, speed, tag, null);

    public void Remove(Entity e)
    {
        if (!_all.Remove(e.Id)) return;
        RemoveFrom(_byColumn, e.Column, e);
        RemoveFrom(_byRegion, e.Region, e);
        var old = e.Tier;
        _active.Remove(e);
        _approx.Remove(e);
        e.Tier = SimulationTier.Dormant;
        if (old == SimulationTier.Active) Deactivated?.Invoke(e);
    }

    public void Clear()
    {
        foreach (var e in _all.Values.ToList()) Remove(e);
        _farSnapshot = Array.Empty<Entity>();
        _farCursor = 0;
    }

    private Entity Create(long id, ushort typeId, Vec3d position, double speed, string? tag, Vec3d? destination)
    {
        var e = new Entity(id, typeId) { Speed = speed, Tag = tag };
        if (Definitions != null && typeId < Definitions.Count) e.Def = Definitions.Get(typeId);
        _all[id] = e;
        e.Position = Normalize(position);
        e.Column = ColumnOf(e.Position);
        e.Region = RegionOf(e.Position);
        AddTo(_byColumn, e.Column, e);
        AddTo(_byRegion, e.Region, e);
        e.LastSim = Time;
        if (destination.HasValue) e.Destination = Normalize(destination.Value);
        RefreshTier(e);
        return e;
    }

    // ------------------------------------------------------------------ movimiento

    /// <summary>Mueve una entidad (cualquier tier). La posición se normaliza: X se envuelve, Z se limita a los polos.</summary>
    public void Move(Entity e, Vec3d position) => MoveCore(e, position);

    public void SetDestination(Entity e, Vec3d? destination)
    {
        e.Destination = destination.HasValue ? Normalize(destination.Value) : null;
        e.LastSim = Time;
        RefreshTier(e);
    }

    /// <summary>Las entidades activas llaman a esto cuando su IA detecta que han llegado.</summary>
    public void NotifyArrived(Entity e)
    {
        e.Destination = null;
        Arrived?.Invoke(e);
        RefreshTier(e);
    }

    private void MoveCore(Entity e, Vec3d position)
    {
        position = Normalize(position);
        e.Position = position;

        var col = ColumnOf(position);
        var reg = RegionOf(position);
        if (reg != e.Region)
        {
            RemoveFrom(_byRegion, e.Region, e);
            e.Region = reg;
            AddTo(_byRegion, reg, e);
        }
        if (col != e.Column)
        {
            RemoveFrom(_byColumn, e.Column, e);
            e.Column = col;
            AddTo(_byColumn, col, e);
            RefreshTier(e);
        }
    }

    private Vec3d Normalize(Vec3d p) =>
        new(_scale.WrapX(p.X), p.Y, Math.Clamp(p.Z, 0.0, _scale.HeightBlocks - 0.001));

    private ChunkCoord ColumnOf(Vec3d p) =>
        new(IntMath.FloorDiv((int)Math.Floor(p.X), _shape.SizeX), 0, IntMath.FloorDiv((int)Math.Floor(p.Z), _shape.SizeZ));

    private (int X, int Z) RegionOf(Vec3d p)
    {
        var r = _scale.RegionOf((int)Math.Floor(p.X), (int)Math.Floor(p.Z));
        return (r.X, r.Z);
    }

    // ------------------------------------------------------------------ niveles de simulación

    private SimulationTier ComputeTier(Entity e) =>
        _loadedColumns.ContainsKey(e.Column) ? SimulationTier.Active
        : e.Destination.HasValue ? SimulationTier.Approximate
        : SimulationTier.Dormant;

    private void RefreshTier(Entity e) => SetTier(e, ComputeTier(e));

    private void SetTier(Entity e, SimulationTier tier)
    {
        if (e.Tier == tier) return;
        var old = e.Tier;
        if (old == SimulationTier.Active) _active.Remove(e);
        if (old == SimulationTier.Approximate) _approx.Remove(e);

        e.Tier = tier;
        if (tier == SimulationTier.Active) _active.Add(e);
        if (tier == SimulationTier.Approximate) { _approx.Add(e); e.LastSim = Time; }

        if (old == SimulationTier.Active) Deactivated?.Invoke(e);
        if (tier == SimulationTier.Active) Activated?.Invoke(e);
    }

    /// <summary>Engancha el almacén a un mundo: sus chunks cargados/descargados activan/desactivan entidades. Llamar antes de cargar chunks.</summary>
    public void Attach<TCell>(IWorld<TCell> world) where TCell : unmanaged
    {
        world.ChunkLoaded += NotifyChunkLoaded;
        world.ChunkUnloaded += NotifyChunkUnloaded;
    }

    public void NotifyChunkLoaded(ChunkCoord chunk)
    {
        var key = ColumnKey(chunk);
        _loadedColumns.TryGetValue(key, out int n);
        _loadedColumns[key] = n + 1;
        if (n > 0 || !_byColumn.TryGetValue(key, out var set)) return;

        foreach (var e in new List<Entity>(set))
        {
            if (e.Tier == SimulationTier.Approximate) AdvanceTo(e, Time);   // ponerla al día antes de activarla
            RefreshTier(e);
        }
    }

    public void NotifyChunkUnloaded(ChunkCoord chunk)
    {
        var key = ColumnKey(chunk);
        if (!_loadedColumns.TryGetValue(key, out int n)) return;
        if (n > 1) { _loadedColumns[key] = n - 1; return; }

        _loadedColumns.Remove(key);
        if (!_byColumn.TryGetValue(key, out var set)) return;
        foreach (var e in new List<Entity>(set)) RefreshTier(e);
    }

    private ChunkCoord ColumnKey(ChunkCoord chunk) => new(_scale.WrapChunkX(chunk.X), 0, chunk.Z);

    // ------------------------------------------------------------------ simulación aproximada

    /// <param name="focus">Posición del jugador/cámara: las entidades aproximadas cercanas se actualizan con más frecuencia.</param>
    public void Update(double dt, Vec3d focus)
    {
        Time += dt;
        _nearTimer += dt;
        if (_nearTimer >= NearInterval)
        {
            _nearTimer = 0;
            StepNear(focus);
        }
        StepFar();
    }

    private void StepNear(Vec3d focus)
    {
        var fr = _scale.RegionOf((int)Math.Floor(focus.X), (int)Math.Floor(focus.Z));
        _scratch.Clear();
        for (int dz = -NearRegionRadius; dz <= NearRegionRadius; dz++)
        {
            int rz = fr.Z + dz;
            if (rz < 0 || rz >= _scale.MapHeight) continue;
            for (int dx = -NearRegionRadius; dx <= NearRegionRadius; dx++)
            {
                int rx = ((fr.X + dx) % _scale.MapWidth + _scale.MapWidth) % _scale.MapWidth;
                if (!_byRegion.TryGetValue((rx, rz), out var set)) continue;
                foreach (var e in set) if (e.Tier == SimulationTier.Approximate) _scratch.Add(e);
            }
        }
        foreach (var e in _scratch)
            if (e.Tier == SimulationTier.Approximate) AdvanceTo(e, Time);
        _scratch.Clear();
    }

    private void StepFar()
    {
        if (_farCursor >= _farSnapshot.Length)
        {
            _farSnapshot = _approx.ToArray();
            _farCursor = 0;
        }
        int budget = MaxFarPerUpdate;
        while (_farCursor < _farSnapshot.Length && budget-- > 0)
        {
            var e = _farSnapshot[_farCursor++];
            if (e.Tier == SimulationTier.Approximate && _all.ContainsKey(e.Id)) AdvanceTo(e, Time);
        }
    }

    /// <summary>Avanza en línea recta hacia el destino durante el tiempo transcurrido desde la última puesta al día.</summary>
    private void AdvanceTo(Entity e, double time)
    {
        double dt = time - e.LastSim;
        e.LastSim = time;
        if (dt <= 0 || !e.Destination.HasValue) return;

        var d = e.Destination.Value;
        double dx = _scale.DeltaX(e.Position.X, d.X);
        double dz = d.Z - e.Position.Z;
        double dy = d.Y - e.Position.Y;
        double dist = Math.Sqrt(dx * dx + dz * dz);
        double step = e.Speed * dt;

        if (step >= dist)
        {
            MoveCore(e, d);
            e.Destination = null;
            Arrived?.Invoke(e);
            RefreshTier(e);
        }
        else
        {
            double k = step / dist;
            MoveCore(e, new Vec3d(e.Position.X + dx * k, e.Position.Y + dy * k, e.Position.Z + dz * k));
        }
    }

    // ------------------------------------------------------------------ consultas

    /// <summary>Añade a 'results' las entidades a distancia ≤ radius de center (distancia 3D, con envoltura en X).</summary>
    public void QueryRadius(Vec3d center, double radius, List<Entity> results)
    {
        int cx0 = IntMath.FloorDiv((int)Math.Floor(center.X - radius), _shape.SizeX);
        int cx1 = IntMath.FloorDiv((int)Math.Floor(center.X + radius), _shape.SizeX);
        if (cx1 - cx0 + 1 > _scale.ChunkCountX) { cx0 = 0; cx1 = _scale.ChunkCountX - 1; }
        int cz0 = Math.Max(0, IntMath.FloorDiv((int)Math.Floor(center.Z - radius), _shape.SizeZ));
        int cz1 = Math.Min(_scale.ChunkCountZ - 1, IntMath.FloorDiv((int)Math.Floor(center.Z + radius), _shape.SizeZ));
        double r2 = radius * radius;

        for (int cz = cz0; cz <= cz1; cz++)
        for (int cx = cx0; cx <= cx1; cx++)
        {
            if (!_byColumn.TryGetValue(new ChunkCoord(_scale.WrapChunkX(cx), 0, cz), out var set)) continue;
            foreach (var e in set)
            {
                double dx = _scale.DeltaX(center.X, e.Position.X);
                double dy = e.Position.Y - center.Y;
                double dz = e.Position.Z - center.Z;
                if (dx * dx + dy * dy + dz * dz <= r2) results.Add(e);
            }
        }
    }

    public IEnumerable<Entity> InRegion(int rx, int rz) =>
        _byRegion.TryGetValue((rx, rz), out var set) ? set : Enumerable.Empty<Entity>();

    public IEnumerable<Entity> InColumn(ChunkCoord column) =>
        _byColumn.TryGetValue(new ChunkCoord(_scale.WrapChunkX(column.X), 0, column.Z), out var set) ? set : Enumerable.Empty<Entity>();

    // ------------------------------------------------------------------ puntería e inspección

    /// <summary>
    /// Primera entidad que corta un rayo (caja alineada con los ejes: ancho x alto x ancho de su EntityDef). 'direction' debe estar
    /// normalizada. Solo mira las entidades cercanas, usando el índice espacial.
    /// </summary>
    public Entity? Raycast(Vec3d origin, Vec3d direction, double maxDistance, out double distance)
    {
        _rayScratch.Clear();
        QueryRadius(origin, maxDistance + 2.0, _rayScratch);

        Entity? best = null;
        double bestT = maxDistance;
        foreach (var e in _rayScratch)
        {
            double w = e.Def?.Width ?? 0.6, h = e.Def?.Height ?? 1.8;
            // Todo relativo al origen del rayo (y X por el camino corto, por la costura este-oeste).
            double cx = _scale.DeltaX(origin.X, e.Position.X), cy = e.Position.Y - origin.Y, cz = e.Position.Z - origin.Z;
            if (RayHitsBox(direction, cx - w / 2, cy, cz - w / 2, cx + w / 2, cy + h, cz + w / 2, out double t) && t < bestT)
            {
                best = e;
                bestT = t;
            }
        }
        _rayScratch.Clear();
        distance = bestT;
        return best;
    }

    private static bool RayHitsBox(Vec3d d, double minX, double minY, double minZ, double maxX, double maxY, double maxZ, out double t)
    {
        double tmin = 0, tmax = double.MaxValue;
        t = 0;
        if (!Slab(d.X, minX, maxX, ref tmin, ref tmax) ||
            !Slab(d.Y, minY, maxY, ref tmin, ref tmax) ||
            !Slab(d.Z, minZ, maxZ, ref tmin, ref tmax)) return false;
        t = tmin;
        return true;
    }

    private static bool Slab(double dir, double min, double max, ref double tmin, ref double tmax)
    {
        if (Math.Abs(dir) < 1e-12) return min <= 0 && 0 <= max;   // paralelo al plano: el origen (0) debe estar dentro de la losa
        double t1 = min / dir, t2 = max / dir;
        if (t1 > t2) (t1, t2) = (t2, t1);
        tmin = Math.Max(tmin, t1);
        tmax = Math.Min(tmax, t2);
        return tmin <= tmax;
    }

    /// <summary>Cuántas entidades tienen algún cambio de propiedad guardado (el resto comparte su prototipo sin gastar nada).</summary>
    public int CountWithOverrides()
    {
        int n = 0;
        foreach (var e in _all.Values) if (e.OverrideCount > 0) n++;
        return n;
    }

    // ------------------------------------------------------------------ persistencia

    public List<EntityRecord> Export(Registry<EntityDef> defs)
    {
        var list = new List<EntityRecord>(_all.Count);
        foreach (var e in _all.Values)
        {
            var d = e.Destination;
            List<PropertyRecord>? props = null;   // solo las propiedades cambiadas
            foreach (var (id, value) in e.RawOverrides)
                (props ??= new List<PropertyRecord>()).Add(new PropertyRecord(PropertyIds.NameOf(id), value.Number, value.Text));

            list.Add(new EntityRecord(e.Id, defs.Get(e.TypeId).Name, e.Position.X, e.Position.Y, e.Position.Z,
                d?.X, d?.Y, d?.Z, e.Speed, e.Tag, props));
        }
        return list;
    }

    /// <summary>Sustituye el contenido del almacén. Las entidades se activarán a medida que se carguen sus chunks.</summary>
    public void Import(IEnumerable<EntityRecord> records, Registry<EntityDef> defs, double time)
    {
        Clear();
        Definitions ??= defs;
        Time = time;
        foreach (var r in records)
        {
            if (!defs.TryGetId(r.Type, out ushort type)) continue;
            Vec3d? dest = r.DestX.HasValue && r.DestY.HasValue && r.DestZ.HasValue
                ? new Vec3d(r.DestX.Value, r.DestY.Value, r.DestZ.Value)
                : null;
            var entity = Create(r.Id, type, new Vec3d(r.X, r.Y, r.Z), r.Speed, r.Tag, dest);
            if (r.Props != null) foreach (var p in r.Props) entity.ImportProperty(p);
            _nextId = Math.Max(_nextId, r.Id + 1);
        }
    }

    // ------------------------------------------------------------------ utilidades

    private static void AddTo<TKey>(Dictionary<TKey, HashSet<Entity>> index, TKey key, Entity e) where TKey : notnull
    {
        if (!index.TryGetValue(key, out var set)) index[key] = set = new HashSet<Entity>();
        set.Add(e);
    }

    private static void RemoveFrom<TKey>(Dictionary<TKey, HashSet<Entity>> index, TKey key, Entity e) where TKey : notnull
    {
        if (!index.TryGetValue(key, out var set)) return;
        set.Remove(e);
        if (set.Count == 0) index.Remove(key);
    }
}
