using GF.Core;
using GF.World.Items;

namespace MiniCraft;

/// <summary>
/// Los objetos del juego, cargados de items.json como PROTOTIPOS con herencia múltiple (item, weapon, knife = [item, weapon]...). Llama a Load una vez
/// al arrancar, antes que EntityTypes.Load (los tipos de entidad referencian los objetos por nombre).
/// Cada MUNDO tiene su propio catálogo (BeginWorld): los objetos del JSON son iguales en todos y conservan sus IDs; los creados en partida a partir de
/// otro objeto (p. ej. el cuchillo de un líder o un arma legendaria) pertenecen al mundo donde nacieron y se guardan con él.
/// </summary>
public static class ItemTypes
{
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "items.json");

    private static ItemCatalog _base = null!;

    /// <summary>El catálogo del mundo abierto: objetos del JSON más los de runtime de ese mundo.</summary>
    public static ItemCatalog Catalog { get; private set; } = null!;

    /// <summary>Los objetos con ID del mundo abierto (todo lo que guardan inventarios y equipamientos, por nombre).</summary>
    public static Registry<ItemDef> Registry => Catalog.Registry;

    /// <returns>Avisos de la carga. Un archivo ausente o inválido lanza una excepción con el motivo.</returns>
    public static List<string> Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Falta el archivo de objetos: {path}", path);
        var messages = new List<string>();
        _base = ItemDefinitionLoader.Load(File.ReadAllText(path), messages);
        Catalog = _base.Fork();
        return messages;
    }

    /// <summary>Empieza con un catálogo limpio (sin los objetos de runtime de otro mundo) y le añade los guardados de este.</summary>
    public static List<string> BeginWorld(IEnumerable<RuntimeItemRecord>? saved)
    {
        Catalog = _base.Fork();
        return Catalog.ImportRuntime(saved);
    }
}
