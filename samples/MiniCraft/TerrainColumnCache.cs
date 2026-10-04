using System.Collections.Concurrent;
using GF.Core;
using GF.World.Map;

namespace MiniCraft;

/// <summary>Altura y bioma de cada columna de bloques de una columna de chunks (tamaño*tamaño valores, índice x + z*tamaño).</summary>
public sealed class ColumnData
{
    public int[] Height { get; }
    public ushort[] Biome { get; }
    public int MinHeight { get; }
    public int MaxHeight { get; }

    public ColumnData(int[] height, ushort[] biome)
    {
        Height = height;
        Biome = biome;
        MinHeight = height.Min();
        MaxHeight = height.Max();
    }
}

/// <summary>
/// Caché compartida (thread-safe) de altura y bioma por columna de chunks. Antes, cada chunk volvía a evaluar el ruido de
/// sus 256 columnas (y cada columna de chunks tiene VerticalChunks chunks), y los árboles y la decoración muestreaban otra
/// vez; ahora cada columna se calcula UNA vez y todo lo demás lee de un array. El resultado es el mismo (función pura), así que
/// dos hilos que la calculen a la vez no se estorban. Acotada: descarta las más antiguas.
/// Las coordenadas de chunk se canonizan en X (el mundo se envuelve).
/// </summary>
public sealed class TerrainColumnCache
{
    private readonly MiniCraftClimate _climate;
    private readonly WorldScale _scale;
    private readonly int _size;
    private readonly int _capacity;
    private readonly ConcurrentDictionary<(int X, int Z), ColumnData> _columns = new();
    private readonly ConcurrentQueue<(int X, int Z)> _order = new();
    private int _count;

    public TerrainColumnCache(MiniCraftClimate climate, WorldScale scale, int capacity = 8192)
    {
        _climate = climate;
        _scale = scale;
        _size = scale.ChunkSizeX;
        _capacity = capacity;
    }

    public ColumnData GetColumn(int chunkX, int chunkZ)
    {
        int cx = _scale.WrapChunkX(chunkX);
        var key = (cx, chunkZ);
        if (_columns.TryGetValue(key, out var data)) return data;

        data = Compute(cx, chunkZ);
        if (_columns.TryAdd(key, data))
        {
            _order.Enqueue(key);
            if (Interlocked.Increment(ref _count) > _capacity) Trim();
            return data;
        }
        return _columns[key];   // otro hilo la añadió antes (idéntica)
    }

    public int HeightAt(int wx, int wz)
    {
        int cx = IntMath.FloorDiv(wx, _size), cz = IntMath.FloorDiv(wz, _size);
        return GetColumn(cx, cz).Height[(wx - cx * _size) + (wz - cz * _size) * _size];
    }

    public ushort BiomeAt(int wx, int wz)
    {
        int cx = IntMath.FloorDiv(wx, _size), cz = IntMath.FloorDiv(wz, _size);
        return GetColumn(cx, cz).Biome[(wx - cx * _size) + (wz - cz * _size) * _size];
    }

    private ColumnData Compute(int canonicalChunkX, int chunkZ)
    {
        var heights = new int[_size * _size];
        var biomes = new ushort[_size * _size];
        for (int z = 0; z < _size; z++)
        for (int x = 0; x < _size; x++)
        {
            var s = _climate.Sample(canonicalChunkX * _size + x, chunkZ * _size + z);
            heights[x + z * _size] = MiniCraftClimate.ToBlockHeight(s.Height);
            biomes[x + z * _size] = _climate.Classify(s);
        }
        return new ColumnData(heights, biomes);
    }

    private void Trim()
    {
        // Se descarta hasta el 90 % de la capacidad, de las más antiguas a las más nuevas.
        int target = _capacity - _capacity / 10;
        while (Volatile.Read(ref _count) > target && _order.TryDequeue(out var old))
            if (_columns.TryRemove(old, out _)) Interlocked.Decrement(ref _count);
    }
}
