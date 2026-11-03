using System.Collections.Concurrent;
using GF.Core;
using GF.World.Map;

namespace MiniCraft;

public sealed class CityOptions
{
    public float Chance { get; set; } = 0.8f;     // probabilidad de intentar una ciudad por celda
    public int CellSize { get; set; } = 1024;     // debe ser múltiplo del tamaño de chunk (8, 16, 32)
    public int MinRadius { get; set; } = 52;
    public int MaxRadius { get; set; } = 80;
    public float SizeFactor { get; set; } = 1.6f;   // multiplica el radio de las CIUDADES (los poblados no)
    public int MaxSlope { get; set; } = 10;        // desnivel máximo (bloques) dentro de la ciudad
    public int Attempts { get; set; } = 16;       // posiciones candidatas por celda
    public int MaxFloors { get; set; } = 4;
    public int ReserveMargin { get; set; } = 16;
    public float RuinChance { get; set; } = 0.45f;                 // probabilidad de ruina en una celda sin ciudad (en una celda con ciudad, el 35 % de esto)
    public int[] RuinWeights { get; set; } = { 40, 22, 18, 20 };   // ciudad abandonada, templo, campo de estatuas, instalación tecnológica
    public int[]? TempleVariants { get; set; }                     // pesos por variante (null = todas por igual)
    public int[]? StatuesVariants { get; set; }
    public int[]? TechVariants { get; set; }
    public double RuinMinDecay { get; set; } = 0.25;               // deterioro de las ruinas: de uno a otro
    public double RuinMaxDecay { get; set; } = 0.9;
    public double RuinSizeFactor { get; set; } = 1.0;              // multiplica el radio de las ruinas
    public bool Log { get; set; } = true;         // escribe cada ciudad encontrada en la consola
}

/// <summary>Trazado de las calles de una ciudad: separación entre calles en X y Z, anchura y cuánto se desplazan (0 = cuadrícula regular).</summary>
public sealed record CityLayout(int PitchX, int PitchZ, int StreetW, int Jitter);

/// <summary>Fases del contorno irregular de una ciudad (salen de su celda: mismo mundo, mismo borde).</summary>
public sealed record CityShape(double P1, double P2, double P3);

/// <summary>Qué es un sitio: una ciudad (habitada o ruina), un templo abandonado, un campo de estatuas o una instalación tecnológica abandonada.</summary>
public enum SiteKind { City, Temple, Statues, Tech }

public sealed record City(int Id, int CenterX, int CenterZ, int Radius, int GroundY, int MaxFloors, CityStyle Style, CityLayout Layout,
    CityType Type, CityShape Shape, bool Hamlet = false, int WorldHeight = 0, double Decay = 0, SiteKind Site = SiteKind.City, int Variant = 0)
{
    /// <summary>Una ruina (ciudad abandonada) en vez de una ciudad habitada: Decay (0 a 1) es lo deteriorada que está.</summary>
    public bool Abandoned => Decay > 0;

    /// <summary>Contenido ya decidido de los solares de esta ciudad (lo rellena CityStage al generar; es puro: se puede recalcular).</summary>
    public ConcurrentDictionary<(int, int, int, int), object> Plots { get; } = new();

    // ---- relieve: la ciudad sigue el terreno (suavizado y atenuado) en vez de aplanarlo del todo
    public const int ReliefStep = 8;          // separación de la malla del relieve suavizado, en bloques
    public const double ReliefKeep = 0.55;    // fracción del relieve original que se conserva (0 = todo plano, 1 = terreno original)
    public const int MaxRelief = 7;           // desnivel máximo respecto a GroundY

    /// <summary>Altura del terreno ORIGINAL en una columna absoluta (la pone el planificador: es la misma que usa TerrainStage).</summary>
    public Func<int, int, int> OriginalHeight { get; init; } = static (_, _) => 0;
    private readonly ConcurrentDictionary<(int, int), int> _relief = new();

    private int ReliefAt(int gx, int gz)
    {
        if (_relief.TryGetValue((gx, gz), out int cached)) return cached;
        int x = CenterX + gx * ReliefStep, z = CenterZ + gz * ReliefStep;
        // media de 5 muestras: quita el detalle fino y deja las colinas
        int sum = OriginalHeight(x, z) + OriginalHeight(x - 3, z) + OriginalHeight(x + 3, z) + OriginalHeight(x, z - 3) + OriginalHeight(x, z + 3);
        int v = (int)Math.Round(sum / 5.0);
        _relief[(gx, gz)] = v;
        return v;
    }

    /// <summary>Altura del terreno original suavizada (interpolación bilinear de la malla), en (rx, rz) relativos al centro.</summary>
    public double SmoothHeight(int rx, int rz)
    {
        double fx = (double)rx / ReliefStep, fz = (double)rz / ReliefStep;
        int gx = (int)Math.Floor(fx), gz = (int)Math.Floor(fz);
        double tx = fx - gx, tz = fz - gz;
        double a = ReliefAt(gx, gz), b = ReliefAt(gx + 1, gz), c = ReliefAt(gx, gz + 1), d = ReliefAt(gx + 1, gz + 1);
        return (a * (1 - tx) + b * tx) * (1 - tz) + (c * (1 - tx) + d * tx) * tz;
    }

    /// <summary>Altura a la que se deja el suelo en (rx, rz): GroundY más una parte del relieve original suavizado, con tope.</summary>
    public int Target(int rx, int rz)
    {
        int delta = (int)Math.Round((SmoothHeight(rx, rz) - GroundY) * ReliefKeep);
        return GroundY + Math.Clamp(delta, -MaxRelief, MaxRelief);
    }

    public int PlazaHalf => Type.PlazaHalf;

    /// <summary>Mayor distancia al centro a la que puede haber ciudad (el contorno nunca la supera).</summary>
    public double Extent => Radius * (1 + Type.Irregularity);

    /// <summary>Radio de la ciudad en la dirección de (rx, rz), relativos al centro: un círculo deformado por tres ondas (irregularidad según el tipo).</summary>
    public double BoundaryRadius(int rx, int rz)
    {
        double th = Math.Atan2(rz, rx), irr = Type.Irregularity;
        double m = 1 + irr * (0.45 * Math.Sin(2 * th + Shape.P1) + 0.25 * Math.Sin(3 * th + Shape.P2) + 0.15 * Math.Sin(5 * th + Shape.P3)
                              + 0.10 * Math.Sin(8 * th + Shape.P1 * 1.7 + Shape.P2) + 0.05 * Math.Sin(13 * th + Shape.P3 * 2.3 + Shape.P1));   // ondas grandes y otras finas: borde menos redondo
        return Radius * m;
    }

    /// <summary>¿Cae (rx, rz), relativos al centro, dentro del contorno?</summary>
    public bool Contains(int rx, int rz) => Math.Sqrt((double)rx * rx + (double)rz * rz) <= BoundaryRadius(rx, rz);

    /// <summary>
    /// Cuánto se funde el suelo de la ciudad con el terreno original: 0 en el interior y 1 en el contorno (suave, en una banda de hasta 18 bloques).
    /// 'dist' y 'bound': distancia al centro y radio del contorno en esa dirección.
    /// </summary>
    public double FadeAt(double dist, double bound)
    {
        double band = Math.Min(18.0, bound * 0.35);
        double u = Math.Clamp((dist - (bound - band)) / band, 0.0, 1.0);
        return u * u * (3 - 2 * u);
    }

    public double Fade(int rx, int rz) => FadeAt(Math.Sqrt((double)rx * rx + (double)rz * rz), BoundaryRadius(rx, rz));
}

/// <summary>Decide dónde hay ciudades. Puro y thread-safe: depende solo de (semilla, celda) y del terreno.</summary>
public sealed class CityPlanner
{
    private enum Reject { None, Water, Slope, Height }

    private readonly int _seed, _worldHeight, _cellsX, _cellsZ;
    private readonly MiniCraftClimate _climate;
    private readonly WorldScale _scale;
    private readonly TerrainColumnCache _columns;
    private readonly CityOptions _opt;
    private readonly ConcurrentDictionary<(int X, int Z), Lazy<City[]>> _cache = new();
    private int _noChance, _noSite, _found, _rejWater, _rejSlope, _rejHeight, _hamlets, _ruins;

    public CityPlanner(int seed, MiniCraftClimate climate, WorldScale scale, TerrainColumnCache columns, CityOptions? options = null)
    {
        _seed = seed; _climate = climate; _scale = scale; _columns = columns;
        _opt = options ?? new CityOptions();
        _worldHeight = climate.Settings.WorldHeight;
        _cellsX = scale.WidthBlocks / _opt.CellSize;
        _cellsZ = scale.HeightBlocks / _opt.CellSize;
    }

    public int CellCount => _cellsX * _cellsZ;

    /// <summary>Ciudades ya planificadas: para marcarlas en el mapa. No fuerza ninguna planificación nueva.</summary>
    public IEnumerable<City> Known
    {
        get
        {
            foreach (var lazy in _cache.Values)
                if (lazy.IsValueCreated)
                    foreach (var city in lazy.Value) yield return city;
        }
    }

    /// <summary>
    /// Planifica TODAS las celdas del mundo (barato: solo muestrea el clima, no genera chunks). Llamar en segundo plano al abrir el mundo
    /// (Task.Run(planner.PlanAll)): así el mapa muestra todas las ciudades y la consola da el resumen.
    /// </summary>
    public void PlanAll()
    {
        for (int cz = 0; cz < _cellsZ; cz++)
        for (int cx = 0; cx < _cellsX; cx++)
            GetCities(cx, cz);
        if (_opt.Log)
            Console.WriteLine($"[ciudades] {_found} ciudades y {_hamlets} poblados y {_ruins} ruinas en {CellCount} celdas | sin intento por azar {_noChance} | sin sitio llano {_noSite} " +
                              $"(rechazos: agua {_rejWater}, desnivel>{_opt.MaxSlope} {_rejSlope}, altura {_rejHeight})");
    }

    /// <summary>La ciudad y los poblados de la celda donde cae la columna (vacío si no hay). Un chunk cae siempre en una sola celda.</summary>
    public City[] CitiesAt(int wx, int wz)
    {
        if ((uint)wz >= (uint)_scale.HeightBlocks) return Array.Empty<City>();
        int cx = _scale.WrapX(wx) / _opt.CellSize, cz = wz / _opt.CellSize;
        if (cx >= _cellsX || cz >= _cellsZ) return Array.Empty<City>();
        return GetCities(cx, cz);
    }

    /// <summary>La ciudad o poblado cuya zona (contorno más margen) contiene la columna, o null.</summary>
    public City? CityAt(int wx, int wz)
    {
        foreach (var c in CitiesAt(wx, wz))
        {
            int dx = (int)Math.Round(_scale.DeltaX(c.CenterX, wx)), dz = wz - c.CenterZ;
            if (Math.Sqrt((double)dx * dx + (double)dz * dz) <= c.BoundaryRadius(dx, dz) + _opt.ReserveMargin) return c;
        }
        return null;
    }

    private City[] GetCities(int cx, int cz) =>
        _cache.GetOrAdd((cx, cz), k => new Lazy<City[]>(() => Compute(k.X, k.Z))).Value;

    /// <summary>¿Esta columna está dentro de una ciudad o poblado (más su margen)? Árboles y decoración no deben aparecer.</summary>
    public bool IsReserved(int wx, int wz)
    {
        foreach (var c in CitiesAt(wx, wz))
        {
            int dx = (int)Math.Round(_scale.DeltaX(c.CenterX, wx)), dz = wz - c.CenterZ;
            double over = Math.Sqrt((double)dx * dx + (double)dz * dz) - c.BoundaryRadius(dx, dz);   // > 0: fuera del contorno
            if (over <= 1) return true;
            if (over < SkirtWidth)
            {
                // Hacia fuera la reserva se va aclarando al azar: los árboles y las plantas del entorno se acercan poco a poco, sin una línea recta.
                ulong h = Hashing.Combine(_seed, _scale.WrapX(wx), 0xFADE, wz);
                if ((h >> 40) * (1f / (1 << 24)) < 1.0 - (over - 1) / (SkirtWidth - 1)) return true;
            }
        }
        return false;
    }

    private const double SkirtWidth = 14;

    /// <summary>
    /// Trazado de las calles: cuadrícula RECTA en la que la separación entre calles es distinta en cada una (pitch 24-28, desplazamiento de ±3 a ±6),
    /// así que las manzanas salen de tamaños muy variados (de 7 a unos 37 bloques de lado).
    /// </summary>
    private static CityLayout MakeLayout(SplitMixRandom lr, CityType type) =>
        new(lr.Next(24, 29), lr.Next(24, 29), lr.Next(3, 6), lr.Next(3, 7));

    private City[] Compute(int cx, int cz)
    {
        var main = ComputeMain(cx, cz);
        var list = new List<City>();
        if (main != null)
        {
            list.Add(main);
            PlanHamlets(cx, cz, main, list);
        }
        PlanRuins(cx, cz, list);
        return list.ToArray();
    }

    /// <summary>
    /// Ruinas: ciudades abandonadas de distintos tamaños y estados (Decay de 0,25 a 0,9). Aparecen sobre todo en celdas sin ciudad, en terreno llano o de meseta
    /// (las últimas pruebas aceptan terreno algo más movido), en cualquier bioma. Se construyen igual que una ciudad y luego CityStage las deteriora.
    /// </summary>
    /// <summary>Variante dentro de la clase según los pesos de los ajustes (0 = nunca; null o más cortos que las variantes = 1).</summary>
    private static int PickVariant(SplitMixRandom rng, SiteKind site, CityOptions o)
    {
        int count = RuinSites.VariantCount(site);
        int[]? w = site == SiteKind.Temple ? o.TempleVariants : site == SiteKind.Statues ? o.StatuesVariants : o.TechVariants;
        int Weight(int i) => w != null && i < w.Length ? Math.Max(0, w[i]) : 1;
        int total = 0;
        for (int i = 0; i < count; i++) total += Weight(i);
        if (total <= 0) return rng.Next(count);
        int roll = rng.Next(total);
        for (int i = 0; i < count; i++)
        {
            roll -= Weight(i);
            if (roll < 0) return i;
        }
        return 0;
    }

    private void PlanRuins(int cx, int cz, List<City> list)
    {
        var o = _opt;
        var rng = new SplitMixRandom(Hashing.Combine(_seed, cx, 0xDEAD, cz));
        if (!rng.Chance(list.Count == 0 ? o.RuinChance : o.RuinChance * 0.35f)) return;

        // Clase de ruina según los pesos de los ajustes (por defecto 40 % ciudad abandonada, 22 % templo, 18 % estatuas, 20 % instalación) y variante según los suyos.
        var wts = o.RuinWeights;
        int wCity = Math.Max(0, wts[0]), wTemple = Math.Max(0, wts[1]), wStat = Math.Max(0, wts[2]), wTech = Math.Max(0, wts[3]), wTotal = wCity + wTemple + wStat + wTech;
        int kindRoll = wTotal > 0 ? rng.Next(wTotal) : 0;
        var site = wTotal == 0 || kindRoll < wCity ? SiteKind.City
                 : kindRoll < wCity + wTemple ? SiteKind.Temple
                 : kindRoll < wCity + wTemple + wStat ? SiteKind.Statues : SiteKind.Tech;
        int variant = site == SiteKind.City ? 0 : PickVariant(rng, site, o);
        int pick = rng.Next(100);
        var type = pick < 45 ? CityTypes.All[0] : pick < 75 ? CityTypes.All[1] : pick < 90 ? CityTypes.All[2] : CityTypes.All[4];   // aldea, pueblo, ciudad, fortaleza
        if (site != SiteKind.City) type = CityTypes.Site;
        int radius = Math.Max(30, (int)(rng.Next(o.MinRadius, o.MaxRadius + 1) * type.RadiusScale * o.SizeFactor * 0.8 * o.RuinSizeFactor));
        if (site != SiteKind.City) { var rr = RuinSites.RadiusRange(site, variant); radius = (int)(rng.Next(rr.Min, rr.Max + 1) * o.RuinSizeFactor); }   // el tamaño depende de la clase y de la variante
        double ext = radius * (1 + type.Irregularity);
        int m = (int)ext + o.ReserveMargin, span = o.CellSize - 2 * m;
        if (span <= 0) return;
        int minGround = _climate.SeaLevel + _climate.Settings.BeachHeight + 1;

        for (int attempt = 0; attempt < 12; attempt++)
        {
            int x = cx * o.CellSize + m + rng.Next(span), z = cz * o.CellSize + m + rng.Next(span);
            if (list.Any(other => Math.Sqrt(Math.Pow(x - other.CenterX, 2) + Math.Pow(z - other.CenterZ, 2)) < other.Extent + ext + 24)) continue;
            if (TryLevel(x, z, site == SiteKind.City ? radius : (int)(radius * 0.8), minGround, out int y, attempt < 8 ? o.MaxSlope : o.MaxSlope + 6) != Reject.None) continue;
            int floors = Math.Min(Math.Min(o.MaxFloors, type.FloorsCap), (_worldHeight - 3 - y) / 4);
            if (floors < 1) continue;
            if (site != SiteKind.City && y + RuinSites.HeightFor(site, variant) > _worldHeight - 1) continue;   // el templo o el coloso tienen que caber bajo el techo del mundo

            ushort biome = _climate.Classify(_climate.Sample(x, z));
            var style = CityStyles.For(biome, new SplitMixRandom(Hashing.Combine(_seed, cx, 0xDEA0, cz)));
            style = style with { Walls = type.WallMode == 2 || (type.WallMode == 1 && style.Walls) };
            var sr = new SplitMixRandom(Hashing.Combine(_seed, cx, 0xDEA1, cz));
            var shape = new CityShape(sr.NextFloat() * 6.2832, sr.NextFloat() * 6.2832, sr.NextFloat() * 6.2832);
            var layout = MakeLayout(new SplitMixRandom(Hashing.Combine(_seed, cx, 0xDEA2, cz)), type);
            double decay = o.RuinMinDecay + rng.NextFloat() * (o.RuinMaxDecay - o.RuinMinDecay);
            ulong h = Hashing.Combine(_seed, cx, 0xDEA3, cz);
            Interlocked.Increment(ref _ruins);
            if (o.Log) Console.WriteLine($"[ruina] celda ({cx},{cz}): centro x {x} z {z}, radio {radius}, {(site == SiteKind.City ? type.Name : site == SiteKind.Temple ? "templo" : site == SiteKind.Statues ? "estatuas" : "instalación tecnológica")}{(site == SiteKind.City ? "" : " (" + RuinSites.VariantName(site, variant) + ")")}, estado {(int)(decay * 100)} %, estilo {style.Name}");
            list.Add(new City((int)(h ^ (h >> 32)), x, z, radius, y, floors, style, layout, type, shape, WorldHeight: _worldHeight, Decay: decay, Site: site, Variant: variant)
            {
                OriginalHeight = (px, pz) => _columns.HeightAt(px, pz),
            });
            return;
        }
    }

    private static int RuinHeight(SiteKind site) => site == SiteKind.Temple ? 10 : site == SiteKind.Tech ? 15 : 13;   // lo mismo que RuinSites.Height

    /// <summary>Poblados satélite (aldeas pequeñas) alrededor de la ciudad, en terreno llano y dentro de su misma celda.</summary>
    private void PlanHamlets(int cx, int cz, City main, List<City> list)
    {
        var o = _opt;
        var rng = new SplitMixRandom(Hashing.Combine(_seed, cx, 0xC1A5, cz));
        int want = main.Type.Name == "capital" ? rng.Next(2, 5) : main.Type.Name == "aldea" ? rng.Next(0, 2) : rng.Next(0, 4);
        var type = CityTypes.All[0];   // aldea
        int minGround = _climate.SeaLevel + _climate.Settings.BeachHeight + 1;

        for (int i = 0; i < want; i++)
        for (int attempt = 0; attempt < 8; attempt++)
        {
            int radius = Math.Max(32, (int)(rng.Next(o.MinRadius, o.MaxRadius + 1) * type.RadiusScale * 1.4 * (0.8 + 0.2 * rng.NextFloat())));
            double ext = radius * (1 + type.Irregularity);
            double dist = main.Extent + ext + 24 + rng.NextFloat() * 140, angle = rng.NextFloat() * Math.PI * 2;
            int x = main.CenterX + (int)(Math.Cos(angle) * dist), z = main.CenterZ + (int)(Math.Sin(angle) * dist);
            int m = (int)ext + o.ReserveMargin;
            if (x < cx * o.CellSize + m || x >= (cx + 1) * o.CellSize - m || z < cz * o.CellSize + m || z >= (cz + 1) * o.CellSize - m) continue;   // dentro de su celda
            if (list.Any(other => Math.Sqrt(Math.Pow(x - other.CenterX, 2) + Math.Pow(z - other.CenterZ, 2)) < other.Extent + ext + 16)) continue;   // sin solapes
            if (TryLevel(x, z, radius, minGround, out int y) != Reject.None) continue;
            int floors = Math.Min(Math.Min(o.MaxFloors, type.FloorsCap), (_worldHeight - 3 - y) / 4);
            if (floors < 1) continue;

            ushort biome = _climate.Classify(_climate.Sample(x, z));
            var style = CityStyles.For(biome, new SplitMixRandom(Hashing.Combine(_seed, cx, 0xC175 + 1 + i, cz))) with { Walls = false };
            var sr = new SplitMixRandom(Hashing.Combine(_seed, cx, 0xC1B0 + 1 + i, cz));
            var shape = new CityShape(sr.NextFloat() * 6.2832, sr.NextFloat() * 6.2832, sr.NextFloat() * 6.2832);
            var lr = new SplitMixRandom(Hashing.Combine(_seed, cx, 0xC1A7 + 1 + i, cz));
            var layout = MakeLayout(lr, type);
            ulong h = Hashing.Combine(_seed, cx, 0xC17E1D + 1 + i, cz);
            Interlocked.Increment(ref _hamlets);
            if (o.Log) Console.WriteLine($"[poblado] celda ({cx},{cz}): centro x {x} z {z}, radio {radius}, suelo {y}, estilo {style.Name}");
            list.Add(new City((int)(h ^ (h >> 32)), x, z, radius, y, floors, style, layout, type, shape, Hamlet: true, WorldHeight: _worldHeight)
            {
                OriginalHeight = (px, pz) => _columns.HeightAt(px, pz),
            });
            break;
        }
    }

    private City? ComputeMain(int cx, int cz)
    {
        var o = _opt;
        var rng = new SplitMixRandom(Hashing.Combine(_seed, cx, 0xC17E, cz));
        if (!rng.Chance(o.Chance)) { Interlocked.Increment(ref _noChance); return null; }

        var type = CityTypes.Pick(new SplitMixRandom(Hashing.Combine(_seed, cx, 0xC17F, cz)));
        int radius = Math.Max(28, (int)(rng.Next(o.MinRadius, o.MaxRadius + 1) * type.RadiusScale * o.SizeFactor));
        int margin = (int)(o.MaxRadius * CityTypes.MaxRadiusScale * o.SizeFactor * (1 + CityTypes.MaxIrregularity)) + o.ReserveMargin;
        int span = o.CellSize - 2 * margin;
        if (span <= 0) return null;
        int minGround = _climate.SeaLevel + _climate.Settings.BeachHeight + 1;

        for (int attempt = 0; attempt < o.Attempts; attempt++)
        {
            int x = cx * o.CellSize + margin + rng.Next(span);
            int z = cz * o.CellSize + margin + rng.Next(span);
            var why = TryLevel(x, z, radius, minGround, out int y);
            if (why == Reject.None)
            {
                int floors = Math.Min(Math.Min(o.MaxFloors, type.FloorsCap), (_worldHeight - 3 - y) / 4);
                if (floors < 1) why = Reject.Height;
                else
                {
                    ulong h = Hashing.Combine(_seed, cx, 0xC17E1D, cz);
                    ushort biome = _climate.Classify(_climate.Sample(x, z));
                    var style = CityStyles.For(biome, new SplitMixRandom(Hashing.Combine(_seed, cx, 0xC175, cz)));
                    style = style with { Walls = type.WallMode == 2 || (type.WallMode == 1 && style.Walls) };
                    var sr = new SplitMixRandom(Hashing.Combine(_seed, cx, 0xC1B0, cz));
                    var shape = new CityShape(sr.NextFloat() * 6.2832, sr.NextFloat() * 6.2832, sr.NextFloat() * 6.2832);
                    var lr = new SplitMixRandom(Hashing.Combine(_seed, cx, 0xC1A7, cz));
                    var layout = MakeLayout(lr, type);
                    Interlocked.Increment(ref _found);
                    if (o.Log) Console.WriteLine($"[ciudad] celda ({cx},{cz}): centro x {x} z {z}, radio {radius}, suelo {y}, {type.Name}, estilo {style.Name}{(style.Walls ? " con muralla" : "")}, calles {layout.PitchX}x{layout.PitchZ}");
                    return new City((int)(h ^ (h >> 32)), x, z, radius, y, floors, style, layout, type, shape, WorldHeight: _worldHeight)
                    {
                        OriginalHeight = (px, pz) => _columns.HeightAt(px, pz),
                    };
                }
            }
            if (why == Reject.Water) Interlocked.Increment(ref _rejWater);
            else if (why == Reject.Slope) Interlocked.Increment(ref _rejSlope);
            else Interlocked.Increment(ref _rejHeight);
        }
        Interlocked.Increment(ref _noSite);
        return null;
    }

    private Reject TryLevel(int cx, int cz, int radius, int minGround, out int groundY, int maxSlope = -1)
    {
        const int Step = 12;
        int min = int.MaxValue, max = int.MinValue, count = 0;
        long sum = 0;
        groundY = 0;
        for (int dz = -radius; dz <= radius; dz += Step)
        for (int dx = -radius; dx <= radius; dx += Step)
        {
            if (dx * dx + dz * dz > radius * radius) continue;
            int h = MiniCraftClimate.ToBlockHeight(_climate.Sample(cx + dx, cz + dz).Height);   // misma altura que usan los chunks
            if (h < minGround) return Reject.Water;   // agua, playa o fondo marino
            min = Math.Min(min, h); max = Math.Max(max, h);
            if (max - min > (maxSlope >= 0 ? maxSlope : _opt.MaxSlope)) return Reject.Slope;
            sum += h; count++;
        }
        if (count == 0) return Reject.Slope;
        groundY = (int)Math.Round(sum / (double)count);
        return Reject.None;
    }
}
