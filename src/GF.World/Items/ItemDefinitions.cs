using System.Text.Json;
using GF.Core;

namespace GF.World.Items;

/// <summary>
/// Carga los objetos desde JSON. Cada objeto: name (clave, es lo que se guarda), title, category (weapon, armor, accessory, potion, food,
/// ingredient, misc...), maxStack, y según el caso:
///   slot + attack / defense   equipables ("hand", "head", "chest", "feet"... el tipo de ranura de la criatura donde encajan)
///   use: [ { "effect": "heal", "amount": 10 } ]   consumibles (pociones, comida)
///   { "items": [ { "name": "iron_sword", "title": "Espada de hierro", "category": "weapon", "slot": "hand", "attack": 4 },
///                { "name": "apple", "title": "Manzana", "category": "food", "maxStack": 16, "use": [ { "effect": "heal", "amount": 3 } ] } ] }
/// Un equipable apila de 1 en 1 salvo que se indique. Los errores recuperables van a 'messages'; un JSON inválido lanza InvalidDataException.
/// El orden del archivo da el id numérico (solo de ejecución: al guardar se usa el nombre).
/// </summary>
public static class ItemDefinitionLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    public static Registry<ItemDef> Load(string json, List<string> messages)
    {
        ItemsFile file;
        try { file = JsonSerializer.Deserialize<ItemsFile>(json, Options) ?? new ItemsFile(); }
        catch (JsonException e) { throw new InvalidDataException("El archivo de objetos no es válido: " + e.Message, e); }

        var registry = new Registry<ItemDef>();
        foreach (var spec in file.Items)
        {
            if (string.IsNullOrWhiteSpace(spec.Name)) { messages.Add("Hay un objeto sin 'name': se ignora."); continue; }
            if (registry.TryGetId(spec.Name, out _)) { messages.Add($"El objeto '{spec.Name}' está repetido: se ignora la segunda definición."); continue; }

            string? slot = string.IsNullOrWhiteSpace(spec.Slot) ? null : spec.Slot.Trim().ToLowerInvariant();
            var use = spec.Use?
                .Where(u => !string.IsNullOrWhiteSpace(u.Effect))
                .Select(u => new ItemEffect(u.Effect!.Trim().ToLowerInvariant(), u.Amount))
                .ToList();
            if (use is { Count: 0 }) use = null;
            if (spec.Attack < 0 || spec.Defense < 0) messages.Add($"'{spec.Name}': attack y defense no pueden ser negativos.");

            string category = !string.IsNullOrWhiteSpace(spec.Category) ? spec.Category.Trim().ToLowerInvariant()
                : slot != null ? (spec.Attack > spec.Defense ? "weapon" : "armor")
                : use != null ? "consumable" : "misc";
            int maxStack = Math.Max(1, spec.MaxStack ?? (slot != null ? 1 : 64));

            registry.Register(spec.Name, new ItemDef(spec.Name, spec.Title ?? spec.Name, maxStack, slot,
                Math.Max(0f, spec.Attack), Math.Max(0f, spec.Defense), category, use));
        }
        return registry;
    }

    // ------------------------------------------------------------------ formato del JSON

    internal sealed class ItemsFile
    {
        public List<ItemSpec> Items { get; set; } = new();
    }

    internal sealed class ItemSpec
    {
        public string Name { get; set; } = "";
        public string? Title { get; set; }
        public string? Category { get; set; }
        public int? MaxStack { get; set; }
        public string? Slot { get; set; }
        public float Attack { get; set; }
        public float Defense { get; set; }
        public List<EffectSpec>? Use { get; set; }
    }

    internal sealed class EffectSpec
    {
        public string? Effect { get; set; }
        public float Amount { get; set; }
    }
}
