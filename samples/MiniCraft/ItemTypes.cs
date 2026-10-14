using GF.Core;
using GF.World.Items;

namespace MiniCraft;

/// <summary>
/// Los objetos del juego, cargados de items.json (armas, armaduras, accesorios, pociones, comida, ingredientes...). Llama a Load una vez al
/// arrancar, antes que EntityTypes.Load (los tipos de entidad referencian los objetos por nombre). Los objetos nuevos se añaden en el JSON.
/// </summary>
public static class ItemTypes
{
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "items.json");

    public static Registry<ItemDef> Registry { get; private set; } = new();

    /// <returns>Avisos de la carga. Un archivo ausente o inválido lanza una excepción con el motivo.</returns>
    public static List<string> Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Falta el archivo de objetos: {path}", path);
        var messages = new List<string>();
        Registry = ItemDefinitionLoader.Load(File.ReadAllText(path), messages);
        return messages;
    }
}
