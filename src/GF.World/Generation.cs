using GF.Core;

namespace GF.World;

public sealed class GenerationContext
{
    public int Seed { get; }
    public INoise Noise { get; }
    /// <summary>RNG derivado de (seed, coord del chunk): determinista e independiente del orden.</summary>
    public IRandom Random { get; }

    public GenerationContext(int seed, INoise noise, IRandom random)
    {
        Seed = seed; Noise = noise; Random = random;
    }
}

/// <summary>Etapa del pipeline. Debe ser stateless: se ejecuta en varios hilos a la vez.</summary>
public interface IGenerationStage<TCell> where TCell : unmanaged
{
    void Apply(Chunk<TCell> chunk, GenerationContext ctx);
}

public sealed class WorldGenerator<TCell> where TCell : unmanaged
{
    private readonly List<IGenerationStage<TCell>> _stages = new();

    public int Seed { get; }
    public INoise Noise { get; }

    public WorldGenerator(int seed, INoise? noise = null)
    {
        Seed = seed;
        Noise = noise ?? new PerlinNoise(seed);
    }

    public WorldGenerator<TCell> AddStage(IGenerationStage<TCell> stage)
    {
        _stages.Add(stage);
        return this;
    }

    public void Generate(Chunk<TCell> chunk)
    {
        var c = chunk.Coord;
        var rng = new SplitMixRandom(Hashing.Combine(Seed, c.X, c.Y, c.Z));
        var ctx = new GenerationContext(Seed, Noise, rng);
        foreach (var stage in _stages) stage.Apply(chunk, ctx);
    }
}
