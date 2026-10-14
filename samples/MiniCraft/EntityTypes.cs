using GF.Core;
using GF.World.Entities;
using GF.World.Items;

namespace MiniCraft;

/// <summary>
/// Los tipos de entidad del juego, cargados de entitytypes.json (prototipos, inventarios, equipamiento y variantes). Llama a Load una vez al
/// arrancar, antes de crear escenas. Aquí solo quedan atajos con nombre a lo que el código necesita referenciar; el contenido (y los tipos
/// nuevos) se añaden en el JSON.
/// </summary>
public static class EntityTypes
{
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "entitytypes.json");

    public static Registry<EntityDef> Registry { get; private set; } = new();

    // Prototipos con los que el código declara qué eventos procesa cada uno (GameEvents) o pregunta "¿es un enemigo?".
    public static Prototype Creature { get; private set; } = null!;
    public static Prototype PlayerProto { get; private set; } = null!;
    public static Prototype EnemyProto { get; private set; } = null!;
    public static Prototype VillagerProto { get; private set; } = null!;
    public static Prototype DogProto { get; private set; } = null!;

    /// <summary>Ranuras, inventario y equipo iniciales del jugador (el tipo abstracto "player" del JSON).</summary>
    public static EntityTypeInfo PlayerInfo { get; private set; } = null!;

    // Ids de ejecución de los tipos instanciables (por nombre al guardar).
    public static ushort Villager { get; private set; }
    public static ushort Caravan { get; private set; }
    public static ushort Dog { get; private set; }
    public static ushort Zombie { get; private set; }

    /// <returns>Avisos de la carga (objetos desconocidos...). Un archivo ausente o inválido lanza una excepción con el motivo.</returns>
    public static List<string> Load(string path, Registry<ItemDef> items)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Falta el archivo de tipos de entidad: {path}", path);
        var messages = new List<string>();
        var set = EntityDefinitionLoader.Load(File.ReadAllText(path), items, messages);

        Registry = set.Types;
        Creature = set.Get("creature");
        PlayerProto = set.Get("player");
        PlayerInfo = set.Info("player");
        EnemyProto = set.Get("enemy");
        VillagerProto = set.Get("villager");
        DogProto = set.Get("dog");
        Villager = set.Types.IdOf("villager");
        Caravan = set.Types.IdOf("caravan");
        Dog = set.Types.IdOf("dog");
        Zombie = set.Types.IdOf("zombie");
        return messages;
    }
}
