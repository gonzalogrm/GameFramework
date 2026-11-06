using GF.Core;
using GF.World.Cosmos;
using GF.World.Entities;
using GF.World.Items;

namespace MiniCraft;

public enum FaunaDiet { Herbivore, Predator }

/// <summary>Una especie animal de un planeta: tamaño, color, velocidad, vida, dieta, hábitat (biomas) y tamaño de manada.</summary>
public sealed record FaunaSpecies(string Name, string TypeName, FaunaDiet Diet, float Width, float Height, double Speed, float Hp, float Damage, float Aggro,
    byte R, byte G, byte B, int HerdMax, int Weight, ushort[] Habitat);

/// <summary>
/// Fauna de un planeta (determinista: sale solo de la semilla de su dirección). De 4 a 6 especies de herbívoros (de roedores pequeños a colosos) y de 1 a 3 de
/// depredadores, cada una con proporciones, color natural (sin negros), velocidad, vida y hábitat propios; todos los biomas de tierra tienen al menos un
/// herbívoro. Cada especie se registra como un TIPO DE ENTIDAD ("fauna_xxxxxxxx_n"): los herbívoros heredan de "creature" (vagan en manada) y los
/// depredadores de "enemy" (persiguen y atacan al jugador, como los zombis). Los coloca FaunaSpawner.
/// </summary>
public sealed class Fauna
{
    public static readonly Fauna Empty = new(Array.Empty<FaunaSpecies>(), Array.Empty<ushort>());
    public static Fauna Current { get; private set; } = Empty;

    private static readonly (int R, int G, int B)[] Coats =
    {
        (139, 105, 72), (170, 135, 95), (150, 145, 135), (190, 160, 110), (165, 100, 70), (205, 190, 160),
        (110, 125, 80), (180, 120, 80), (215, 215, 205), (140, 150, 165), (125, 95, 70),
    };
    private static readonly (int R, int G, int B)[] Accents = { (210, 150, 60), (200, 90, 60), (95, 140, 200), (120, 170, 90), (200, 170, 70) };
    private static readonly string[] SmallHerb = { "saltarín", "roedor", "correcaminos" };
    private static readonly string[] MediumHerb = { "rumiante", "pastor", "trotador" };
    private static readonly string[] LargeHerb = { "coloso", "gigante lanudo", "acorazado" };
    private static readonly string[] SmallPred = { "carroñero", "rastreador" };
    private static readonly string[] BigPred = { "acechador", "cazador", "devorador" };

    public IReadOnlyList<FaunaSpecies> Species { get; }
    /// <summary>Id de tipo de entidad (EntityTypes.Registry) de cada especie, en el mismo orden.</summary>
    public IReadOnlyList<ushort> TypeIds { get; }

    private Fauna(IReadOnlyList<FaunaSpecies> species, IReadOnlyList<ushort> typeIds) { Species = species; TypeIds = typeIds; }

    /// <summary>Índice de una especie que vive en este bioma, elegida según el hash y los pesos; -1 si ninguna.</summary>
    public int PickIndex(ushort biome, ulong hash)
    {
        int total = 0;
        foreach (var sp in Species) if (Array.IndexOf(sp.Habitat, biome) >= 0) total += sp.Weight;
        if (total <= 0) return -1;
        int roll = (int)(hash % (ulong)total);
        for (int i = 0; i < Species.Count; i++)
        {
            if (Array.IndexOf(Species[i].Habitat, biome) < 0) continue;
            roll -= Species[i].Weight;
            if (roll < 0) return i;
        }
        return -1;
    }

    // ------------------------------------------------------------------ generación y registro

    public static void ApplyPlanet(ulong seed)
    {
        var species = Generate(seed);
        bool hasBone = ItemTypes.Registry.TryGetId("bone", out ushort bone);
        var ids = species.Select(sp => Register(sp, bone, hasBone)).ToArray();
        Current = new Fauna(species, ids);
        Console.WriteLine($"[fauna] {species.Count} especies en este planeta");
    }

    private static ushort Register(FaunaSpecies sp, ushort bone, bool hasBone)
    {
        if (EntityTypes.Registry.TryGetId(sp.TypeName, out ushort existing)) return existing;   // ya registrada (se volvió a este planeta)

        var parent = sp.Diet == FaunaDiet.Predator ? EntityTypes.EnemyProto : EntityTypes.Creature;
        var proto = new Prototype(sp.Name, parent).Text("species", sp.Name).Float("hp", sp.Hp).Float("max_hp", sp.Hp);
        if (sp.Diet == FaunaDiet.Predator) proto.Float("damage", sp.Damage).Float("aggro", sp.Aggro).Float("attack_cooldown", 1.0f);

        List<EntityVariant>? variants = null;
        if (hasBone)   // al morir a manos del jugador suelta huesos (proporcionales a su tamaño)
        {
            var loadout = new LoadoutTemplate(new[] { new ItemStack(bone, 1 + (int)sp.Height) }, new ushort?[SlotLayout.Default.Count]);
            variants = new List<EntityVariant> { new("default", 1, proto, loadout) };
        }
        var def = new EntityDef(sp.TypeName, sp.Speed, sp.R, sp.G, sp.B, sp.Width, sp.Height, proto, 4, variants, null, 0);
        return EntityTypes.Registry.Register(sp.TypeName, def);
    }

    private static List<FaunaSpecies> Generate(ulong seed)
    {
        var rng = new SplitMixRandom(Hashing.Mix(seed ^ 0xFA07AUL));
        uint tag = unchecked((uint)(seed ^ (seed >> 32)));
        ushort[] land = { Biomes.Plains, Biomes.Savanna, Biomes.Forest, Biomes.Jungle, Biomes.Taiga, Biomes.Tundra, Biomes.Desert, Biomes.Mountains, Biomes.SnowPeaks };

        int herbivores = 4 + rng.Next(3), predators = 1 + rng.Next(3);
        var list = new List<FaunaSpecies>();
        for (int i = 0; i < herbivores + predators; i++)
            list.Add(Make(rng, $"fauna_{tag:x8}_{i}", i >= herbivores ? FaunaDiet.Predator : FaunaDiet.Herbivore, land));

        // Cobertura: todo bioma de tierra tiene al menos un herbívoro, y casi todos un depredador.
        foreach (ushort biome in land)
        {
            if (!list.Any(s => s.Diet == FaunaDiet.Herbivore && Array.IndexOf(s.Habitat, biome) >= 0)) AddHabitat(list, rng, FaunaDiet.Herbivore, biome);
            if (rng.Chance(0.6f) && !list.Any(s => s.Diet == FaunaDiet.Predator && Array.IndexOf(s.Habitat, biome) >= 0)) AddHabitat(list, rng, FaunaDiet.Predator, biome);
        }
        return list;
    }

    private static void AddHabitat(List<FaunaSpecies> list, SplitMixRandom rng, FaunaDiet diet, ushort biome)
    {
        var candidates = Enumerable.Range(0, list.Count).Where(i => list[i].Diet == diet).ToList();
        if (candidates.Count == 0) return;
        int k = candidates[rng.Next(candidates.Count)];
        list[k] = list[k] with { Habitat = list[k].Habitat.Append(biome).ToArray() };
    }

    private static FaunaSpecies Make(SplitMixRandom rng, string typeName, FaunaDiet diet, ushort[] land)
    {
        bool pred = diet == FaunaDiet.Predator;
        float r = rng.NextFloat();
        float height = pred ? 0.7f + r * 1.1f : 0.5f + r * r * 1.7f;
        float width = Math.Clamp(height * (pred ? 0.6f + rng.NextFloat() * 0.5f : 0.55f + rng.NextFloat() * 0.7f), 0.35f, 1.6f);
        double speed = pred ? 3.2 + rng.NextFloat() * 2.0 : Math.Clamp(5.5 - height * 1.6 + rng.NextFloat() * 0.8, 1.8, 6.0);
        float hp = pred ? 10f + height * 14f : 4f + height * 7f;
        float damage = pred ? 1f + height * 1.6f : 0f;
        float aggro = pred ? 12f + rng.NextFloat() * 8f : 0f;
        int herdMax = pred ? 2 : height > 1.5f ? 2 : 5;

        string[] words = pred ? (height < 1.1f ? SmallPred : BigPred) : height < 0.8f ? SmallHerb : height < 1.4f ? MediumHerb : LargeHerb;
        string name = $"{UniverseGenerator.MakeName(rng)} {words[rng.Next(words.Length)]}";

        var (cr, cg, cb) = rng.Chance(0.25f) ? Accents[rng.Next(Accents.Length)] : Coats[rng.Next(Coats.Length)];
        byte J(int v) => (byte)Math.Clamp(v + rng.Next(-15, 16), 60, 240);   // sin negros: nunca por debajo de 60

        // Hábitat: de 2 a 4 biomas de tierra al azar.
        var shuffled = land.OrderBy(_ => rng.Next(1000)).Take(2 + rng.Next(3)).ToArray();
        return new FaunaSpecies(name, typeName, diet, width, height, speed, hp, damage, aggro, J(cr), J(cg), J(cb), herdMax, 1 + rng.Next(4), shuffled);
    }
}
