using GF.Core;
using GF.World.Cosmos;

namespace MiniCraft;

/// <summary>Plano (blueprint) de una planta: la forma. Cada especie lo concreta con alturas, radios y bloques propios.</summary>
public enum FloraShape { Round, Conifer, Umbrella, Palm, Mushroom, Twisted, Weeping, Dead, Cactus, Spire, Bush, Thorn, Mound }

/// <summary>Una especie de un planeta: forma + materiales + rangos de tamaño (cada ejemplar elige su altura y radio dentro de ellos).</summary>
public sealed record FloraSpecies(string Name, FloraShape Shape, ushort Trunk, ushort Canopy, int MinH, int MaxH, int MinR, int MaxR, int Weight, bool Shrub);

/// <summary>
/// Flora de un planeta (determinista: sale solo de la semilla de su dirección). Por cada bioma hay de 4 a 6 especies de ÁRBOL y de 3 a 5 de ARBUSTO, elegidas
/// entre las formas que encajan con el bioma (cactus y agujas en el desierto, abetos enanos y troncos muertos en la montaña...), con materiales, tamaños y
/// pesos propios. La densidad de cada bioma también varía por planeta. Los ejemplares los coloca TreeStage con FloraShapes.
/// </summary>
public sealed class Flora
{
    /// <summary>Distancia máxima (en bloques) a la que una planta puede sobresalir de su base en horizontal: TreeStage examina esa franja extra.</summary>
    public const int MaxReach = 7;

    private readonly record struct BiomeFlora(ushort Biome, (FloraShape, int)[] Trees, (FloraShape, int)[] Shrubs, float TreeDensity, float ShrubDensity);

    public static Flora Current { get; private set; } = Create(0);

    public static void ApplyPlanet(ulong seed)
    {
        Current = Create(seed);
        Console.WriteLine($"[flora] {Current.All().Count()} especies en este planeta");
    }

    private readonly FloraSpecies[][] _trees, _shrubs;
    private readonly int[] _treeTotal, _shrubTotal;
    private readonly float[] _treeDensity, _shrubDensity;

    public float MaxTreeDensity { get; }
    public float MaxShrubDensity { get; }

    private Flora(FloraSpecies[][] trees, FloraSpecies[][] shrubs, float[] treeDensity, float[] shrubDensity)
    {
        _trees = trees; _shrubs = shrubs; _treeDensity = treeDensity; _shrubDensity = shrubDensity;
        _treeTotal = trees.Select(l => l.Sum(s => s.Weight)).ToArray();
        _shrubTotal = shrubs.Select(l => l.Sum(s => s.Weight)).ToArray();
        MaxTreeDensity = treeDensity.Length == 0 ? 0f : treeDensity.Max();
        MaxShrubDensity = shrubDensity.Length == 0 ? 0f : shrubDensity.Max();
    }

    public float TreeDensity(ushort biome) => _treeDensity[biome];
    public float ShrubDensity(ushort biome) => _shrubDensity[biome];

    public IEnumerable<FloraSpecies> All() => _trees.Concat(_shrubs).SelectMany(l => l);
    public IEnumerable<FloraSpecies> SpeciesOf(ushort biome) => _trees[biome].Concat(_shrubs[biome]);

    /// <summary>Especie de un ejemplar a partir del hash de su columna (según los pesos de las especies del bioma). null si el bioma no tiene.</summary>
    public FloraSpecies? Pick(ushort biome, bool shrub, ulong hash)
    {
        var list = (shrub ? _shrubs : _trees)[biome];
        if (list.Length == 0) return null;
        int roll = (int)((hash >> 16) % (ulong)(shrub ? _shrubTotal : _treeTotal)[biome]);
        foreach (var sp in list)
        {
            roll -= sp.Weight;
            if (roll < 0) return sp;
        }
        return list[0];
    }

    // ------------------------------------------------------------------ generación

    private static BiomeFlora[] Table() => new[]
    {
        new BiomeFlora(Biomes.Plains,
            new[] { (FloraShape.Round, 4), (FloraShape.Umbrella, 3), (FloraShape.Twisted, 3), (FloraShape.Conifer, 1), (FloraShape.Weeping, 1) },
            new[] { (FloraShape.Bush, 5), (FloraShape.Mound, 3), (FloraShape.Thorn, 1) }, 0.010f, 0.020f),
        new BiomeFlora(Biomes.Savanna,
            new[] { (FloraShape.Umbrella, 6), (FloraShape.Twisted, 3), (FloraShape.Dead, 2), (FloraShape.Palm, 1) },
            new[] { (FloraShape.Bush, 3), (FloraShape.Thorn, 4), (FloraShape.Mound, 2) }, 0.008f, 0.030f),
        new BiomeFlora(Biomes.Forest,
            new[] { (FloraShape.Round, 4), (FloraShape.Conifer, 3), (FloraShape.Weeping, 3), (FloraShape.Mushroom, 2), (FloraShape.Twisted, 3), (FloraShape.Umbrella, 1) },
            new[] { (FloraShape.Bush, 5), (FloraShape.Mound, 2), (FloraShape.Thorn, 1) }, 0.035f, 0.050f),
        new BiomeFlora(Biomes.Jungle,
            new[] { (FloraShape.Palm, 4), (FloraShape.Umbrella, 4), (FloraShape.Weeping, 3), (FloraShape.Mushroom, 3), (FloraShape.Round, 2), (FloraShape.Twisted, 2) },
            new[] { (FloraShape.Bush, 4), (FloraShape.Thorn, 3), (FloraShape.Mound, 2) }, 0.060f, 0.080f),
        new BiomeFlora(Biomes.Taiga,
            new[] { (FloraShape.Conifer, 6), (FloraShape.Spire, 2), (FloraShape.Dead, 2), (FloraShape.Mushroom, 2), (FloraShape.Twisted, 1) },
            new[] { (FloraShape.Mound, 4), (FloraShape.Bush, 3), (FloraShape.Thorn, 2) }, 0.030f, 0.030f),
        new BiomeFlora(Biomes.Tundra,
            new[] { (FloraShape.Dead, 4), (FloraShape.Conifer, 2), (FloraShape.Spire, 3), (FloraShape.Twisted, 1) },
            new[] { (FloraShape.Mound, 5), (FloraShape.Thorn, 2) }, 0.006f, 0.020f),
        new BiomeFlora(Biomes.Desert,
            new[] { (FloraShape.Cactus, 6), (FloraShape.Dead, 3), (FloraShape.Spire, 3), (FloraShape.Palm, 1), (FloraShape.Mushroom, 1) },
            new[] { (FloraShape.Thorn, 4), (FloraShape.Mound, 3), (FloraShape.Cactus, 3), (FloraShape.Bush, 1) }, 0.006f, 0.025f),
        new BiomeFlora(Biomes.Mountains,
            new[] { (FloraShape.Conifer, 3), (FloraShape.Dead, 3), (FloraShape.Twisted, 2), (FloraShape.Spire, 4), (FloraShape.Round, 1) },
            new[] { (FloraShape.Mound, 4), (FloraShape.Thorn, 3), (FloraShape.Bush, 1) }, 0.008f, 0.020f),
        new BiomeFlora(Biomes.SnowPeaks,
            new[] { (FloraShape.Dead, 3), (FloraShape.Spire, 4), (FloraShape.Conifer, 1) },
            new[] { (FloraShape.Mound, 4), (FloraShape.Thorn, 2) }, 0.003f, 0.008f),
    };

    private static Flora Create(ulong seed)
    {
        var rng = new SplitMixRandom(Hashing.Mix(seed ^ 0xF10CAUL));
        int n = Biomes.Registry.Count;
        var trees = new FloraSpecies[n][];
        var shrubs = new FloraSpecies[n][];
        var td = new float[n];
        var sd = new float[n];
        for (int i = 0; i < n; i++) { trees[i] = Array.Empty<FloraSpecies>(); shrubs[i] = Array.Empty<FloraSpecies>(); }

        foreach (var b in Table())
        {
            int tCount = 4 + rng.Next(3), sCount = 3 + rng.Next(3);
            trees[b.Biome] = Enumerable.Range(0, tCount).Select(_ => MakeSpecies(rng, Weighted(rng, b.Trees), false)).ToArray();
            shrubs[b.Biome] = Enumerable.Range(0, sCount).Select(_ => MakeSpecies(rng, Weighted(rng, b.Shrubs), true)).ToArray();
            td[b.Biome] = b.TreeDensity * (0.6f + rng.NextFloat() * 0.8f);    // cada planeta, su espesura
            sd[b.Biome] = b.ShrubDensity * (0.6f + rng.NextFloat() * 0.8f);
        }
        return new Flora(trees, shrubs, td, sd);
    }

    private static FloraShape Weighted(SplitMixRandom rng, (FloraShape Shape, int Weight)[] list)
    {
        int roll = rng.Next(list.Sum(l => l.Weight));
        foreach (var (shape, weight) in list)
        {
            roll -= weight;
            if (roll < 0) return shape;
        }
        return list[0].Shape;
    }

    /// <summary>Rangos de la forma: altura mínima y máxima posibles, radio mínimo y máximo posibles.</summary>
    private static (int HLo, int HHi, int RLo, int RHi) Ranges(FloraShape shape, bool shrub) => shape switch
    {
        FloraShape.Round => (4, 9, 2, 4),
        FloraShape.Conifer => (7, 16, 2, 3),
        FloraShape.Umbrella => (6, 11, 3, 5),
        FloraShape.Palm => (6, 12, 3, 5),
        FloraShape.Mushroom => (3, 9, 3, 5),
        FloraShape.Twisted => (5, 10, 2, 3),
        FloraShape.Weeping => (5, 9, 3, 4),
        FloraShape.Dead => (4, 9, 0, 0),
        FloraShape.Cactus => shrub ? (2, 3, 0, 0) : (3, 7, 0, 0),
        FloraShape.Spire => (5, 14, 0, 0),
        FloraShape.Bush => (1, 2, 1, 2),
        FloraShape.Thorn => (1, 3, 1, 3),
        _ => (0, 0, 2, 4),   // Mound
    };

    private static string ShapeWord(FloraShape shape) => shape switch
    {
        FloraShape.Round => "arbol",
        FloraShape.Conifer => "abeto",
        FloraShape.Umbrella => "parasol",
        FloraShape.Palm => "palma",
        FloraShape.Mushroom => "seta gigante",
        FloraShape.Twisted => "retorcido",
        FloraShape.Weeping => "llorón",
        FloraShape.Dead => "tronco muerto",
        FloraShape.Cactus => "cacto",
        FloraShape.Spire => "aguja",
        FloraShape.Bush => "arbusto",
        FloraShape.Thorn => "espino",
        _ => "tapiz",
    };

    private static ushort One(SplitMixRandom rng, params ushort[] options) => options[rng.Next(options.Length)];

    private static FloraSpecies MakeSpecies(SplitMixRandom rng, FloraShape shape, bool shrub)
    {
        var (hLo, hHi, rLo, rHi) = Ranges(shape, shrub);
        int minH = hLo + rng.Next((hHi - hLo) / 2 + 1);
        int maxH = Math.Max(minH, Math.Min(hHi, minH + 1 + rng.Next(4)));
        int minR = rLo + rng.Next((rHi - rLo) / 2 + 1);
        int maxR = Math.Max(minR, Math.Min(rHi, minR + rng.Next(2)));

        // Materiales: a menudo los del planeta (Biomes.Trunk / Canopy), si no uno al azar. Las formas especiales usan los suyos.
        ushort trunk = rng.Chance(0.55f) ? Biomes.Trunk : One(rng, Blocks.Log, Blocks.StemWood, Blocks.Petrified);
        ushort canopy = rng.Chance(0.55f) ? Biomes.Canopy : One(rng, Blocks.Leaves, Blocks.Fronds);   // sombreros y hongos solo en las setas
        switch (shape)
        {
            case FloraShape.Cactus:
                trunk = One(rng, Blocks.Fronds, Blocks.Leaves);
                canopy = trunk;
                break;
            case FloraShape.Spire:
                trunk = canopy = One(rng, Blocks.Crystal, Blocks.Petrified, Blocks.Chalk, Blocks.Basalt, Blocks.Salt, Blocks.Strata);
                break;
            case FloraShape.Mushroom:
                trunk = One(rng, Blocks.StemWood, Blocks.Chalk, Blocks.Fungus);
                canopy = One(rng, Blocks.SporeCap, Blocks.Fungus);
                break;
            case FloraShape.Mound:
                trunk = canopy = One(rng, Blocks.Moss, Blocks.Lichen, Blocks.Fronds, Blocks.Leaves);
                break;
            case FloraShape.Dead:
                trunk = canopy = One(rng, Blocks.Petrified, Blocks.Log, Blocks.StemWood);
                break;
            case FloraShape.Thorn:
                trunk = One(rng, Blocks.Petrified, Blocks.Log, Blocks.Basalt);
                canopy = One(rng, Blocks.Fronds, Blocks.Leaves);
                break;
        }

        string name = $"{UniverseGenerator.MakeName(rng)} ({ShapeWord(shape)})";
        return new FloraSpecies(name, shape, trunk, canopy, minH, maxH, minR, maxR, 1 + rng.Next(4), shrub);
    }
}
