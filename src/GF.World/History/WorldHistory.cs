using GF.Core;
using GF.World.Cosmos;

namespace GF.World.History;

/// <summary>Un héroe de una época.</summary>
public sealed record Hero(string Name, string Epithet, int Epoch, int Index)
{
    public override string ToString() => $"{Name} {Epithet}";
}

/// <summary>Una época: una civilización simulada, sus años, cómo terminó y sus héroes.</summary>
public sealed record Epoch(int Index, string Civilization, int StartYear, int EndYear, string Ending, IReadOnlyList<Hero> Heroes)
{
    public int Years => EndYear - StartYear;
}

/// <summary>
/// La historia simulada de un mundo: hasta 10 épocas de civilizaciones con sus héroes. Es DETERMINISTA (sale solo de la semilla) y muy pequeña (unos
/// pocos registros), así que no se guarda: se regenera al abrir el mundo. Lo que sí queda en partida son las consecuencias (objetos legendarios en
/// poder de enemigos, en alijos...), que son entidades y objetos como los demás.
/// </summary>
public sealed class WorldHistory
{
    public const int MaxEpochs = 10;

    private static readonly string[] Prefixes = { "Imperio", "Reino", "Confederación", "Dominio", "Concilio", "Hermandad", "Califato", "República" };
    private static readonly string[] Endings =
    {
        "una plaga", "una guerra civil", "un cataclismo", "la invasión de un enemigo", "una gran sequía", "la traición de su último rey",
        "una migración masiva", "una rebelión", "la pérdida de su magia",
    };
    private static readonly string[] Epithets =
    {
        "el Forjador", "la Ardiente", "el Sin Nombre", "la Tejedora de Tormentas", "el Último", "la Bendita", "el Cazador de Reyes", "la Inquebrantable",
        "el Profeta", "la Sombra", "el Constructor", "la Sabia",
    };

    public WorldHistory(IReadOnlyList<Epoch> epochs) => Epochs = epochs;

    public IReadOnlyList<Epoch> Epochs { get; }
    public IEnumerable<Hero> Heroes => Epochs.SelectMany(e => e.Heroes);

    /// <summary>Genera la historia de un mundo: de 2 a 10 épocas, de 1 a 3 héroes cada una. Misma semilla, misma historia.</summary>
    public static WorldHistory Generate(ulong seed, int maxEpochs = MaxEpochs)
    {
        var rng = new SplitMixRandom(UniverseGenerator.Mix(seed, 0x4157041));
        int count = rng.Next(Math.Min(2, maxEpochs), Math.Max(2, Math.Min(MaxEpochs, maxEpochs)) + 1);
        var epochs = new List<Epoch>(count);
        int year = 0;
        for (int i = 0; i < count; i++)
        {
            string civ = $"{Prefixes[rng.Next(Prefixes.Length)]} de {UniverseGenerator.MakeName(rng)}";
            int length = rng.Next(80, 420);
            var heroes = new List<Hero>();
            int heroCount = rng.Next(1, 4);
            for (int h = 0; h < heroCount; h++)
                heroes.Add(new Hero(UniverseGenerator.MakeName(rng), Epithets[rng.Next(Epithets.Length)], i, h));
            epochs.Add(new Epoch(i, civ, year, year + length, Endings[rng.Next(Endings.Length)], heroes));
            year += length + rng.Next(10, 120);   // un periodo oscuro entre épocas
        }
        return new WorldHistory(epochs);
    }
}
