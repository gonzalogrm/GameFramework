using GF.World.Items;

namespace MiniCraft;

/// <summary>Textos de objetos y ranuras compartidos por el inventario y el inspector.</summary>
public static class ItemText
{
    /// <summary>Nombre en pantalla de un tipo de ranura (los desconocidos salen tal cual).</summary>
    public static string KindTitle(string kind) => kind switch
    {
        "hand" => "Mano",
        "head" => "Cabeza",
        "chest" => "Torso",
        "feet" => "Pies",
        "ring" => "Anillo",
        _ => kind,
    };

    /// <summary>Nombre de la ranura número 'slot': "Mano", "Mano 2", "Mano 3", "Cabeza"...</summary>
    public static string SlotTitle(SlotLayout layout, int slot)
    {
        string name = layout.Name(slot), kind = layout.Kind(slot);
        string number = name.Length > kind.Length ? " " + name[kind.Length..] : "";
        return KindTitle(kind) + number;
    }

    /// <summary>" (ataque +2, defensa +1, heal 3) [thorns]": lo que da un objeto.</summary>
    public static string Stats(ItemDef def)
    {
        var parts = new List<string>();
        if (def.Attack > 0) parts.Add($"ataque +{def.Attack:0.#}");
        if (def.Defense > 0) parts.Add($"defensa +{def.Defense:0.#}");
        if (def.Use is { } use) parts.AddRange(use.Select(e => $"{e.Type} {e.Amount:0.#}"));
        string text = parts.Count == 0 ? "" : " (" + string.Join(", ", parts) + ")";
        if (def.Components.Count > 0) text += " [" + string.Join(", ", def.Components) + "]";
        return text;
    }

    /// <summary>El objeto con su título, marcando los legendarios y los creados en partida.</summary>
    public static string Name(ItemDef def) => (def.Get("legendary")?.AsBool == true ? "* " : "") + def.Title;
}
