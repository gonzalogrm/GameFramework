using System.Collections.Concurrent;
using GF.Core;
using GF.World.Map;

namespace MiniCraft;

public sealed class CityOptions
{
    public float Chance { get; set; } = 0.8f;     // probabilidad de intentar una ciudad por celda
    public int CellSize { get; set; } = 1024;     // debe ser múltiplo del tamaño de chunk (8, 16, 32)
    public int MinRadius { get; set; } = 40;
    public int MaxRadius { get; set; } = 80;
    public int MaxSlope { get; set; } = 8;        // desnivel máximo (bloques) dentro de la ciudad
    public int Attempts { get; set; } = 16;       // posiciones candidatas por celda
    public int MaxFloors { get; set; } = 4;
    public int ReserveMargin { get; set; } = 6;   // margen sin árboles alrededor
    public bool Log { get; set; } = true;         // escribe cada ciudad encontrada en la consola
}

public sealed record City(int Id, int CenterX, int CenterZ, int Radius, int GroundY, int MaxFloors);

/// <summary>Decide dónde hay ciudades. Puro y thread-safe: depende solo de (semilla, celda) y del terreno.</summary>
public sealed class CityPlanner
{
    private enum Reject { None, Water, Slope, Height }

    private readonly int _seed, _worldHeight, _cellsX, _cellsZ;
    private readonly MiniCraftClimate _climate;
    private readonly WorldScale _scale;
    private readonly CityOptions _opt;
    private readonly ConcurrentDictionary<(int X, int Z), Lazy<City?>> _cache = new();
    private int _noChance, _noSite, _found, _rejWater, _rejSlope, _rejHeight;

    public CityPlanner(int seed, MiniCraftClimate climate, WorldScale scale, TerrainColumnCache columns, CityOptions? options = null)
    {
        _seed = seed; _climate = climate; _scale = scale;
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
                if (lazy.IsValueCreated && lazy.Value is { } city) yield return city;
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
            GetCity(cx, cz);
        if (_opt.Log)
            Console.WriteLine($"[ciudades] {_found} ciudades en {CellCount} celdas | sin intento por azar {_noChance} | sin sitio llano {_noSite} " +
                              $"(rechazos: agua {_rejWater}, desnivel>{_opt.MaxSlope} {_rejSlope}, altura {_rejHeight})");
    }

    public City? CityAt(int wx, int wz)
    {
        if ((uint)wz >= (uint)_scale.HeightBlocks) return null;
        int cx = _scale.WrapX(wx) / _opt.CellSize, cz = wz / _opt.CellSize;
        if (cx >= _cellsX || cz >= _cellsZ) return null;
        return GetCity(cx, cz);
    }

    private City? GetCity(int cx, int cz) =>
        _cache.GetOrAdd((cx, cz), k => new Lazy<City?>(() => Compute(k.X, k.Z))).Value;

    /// <summary>¿Esta columna está dentro de una ciudad (más su margen)? Árboles y decoración no deben aparecer.</summary>
    public bool IsReserved(int wx, int wz)
    {
        var c = CityAt(wx, wz);
        if (c == null) return false;
        long dx = (long)Math.Round(_scale.DeltaX(c.CenterX, wx)), dz = wz - c.CenterZ;
        long r = c.Radius + _opt.ReserveMargin;
        return dx * dx + dz * dz <= r * r;
    }

    private City? Compute(int cx, int cz)
    {
        var o = _opt;
        var rng = new SplitMixRandom(Hashing.Combine(_seed, cx, 0xC17E, cz));
        if (!rng.Chance(o.Chance)) { Interlocked.Increment(ref _noChance); return null; }

        int radius = rng.Next(o.MinRadius, o.MaxRadius + 1);
        int margin = o.MaxRadius + o.ReserveMargin;
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
                int floors = Math.Min(o.MaxFloors, (_worldHeight - 3 - y) / 4);
                if (floors < 1) why = Reject.Height;
                else
                {
                    ulong h = Hashing.Combine(_seed, cx, 0xC17E1D, cz);
                    Interlocked.Increment(ref _found);
                    if (o.Log) Console.WriteLine($"[ciudad] celda ({cx},{cz}): centro x {x} z {z}, radio {radius}, suelo {y}");
                    return new City((int)(h ^ (h >> 32)), x, z, radius, y, floors);
                }
            }
            if (why == Reject.Water) Interlocked.Increment(ref _rejWater);
            else if (why == Reject.Slope) Interlocked.Increment(ref _rejSlope);
            else Interlocked.Increment(ref _rejHeight);
        }
        Interlocked.Increment(ref _noSite);
        return null;
    }

    private Reject TryLevel(int cx, int cz, int radius, int minGround, out int groundY)
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
            if (max - min > _opt.MaxSlope) return Reject.Slope;
            sum += h; count++;
        }
        if (count == 0) return Reject.Slope;
        groundY = (int)Math.Round(sum / (double)count);
        return Reject.None;
    }
}
