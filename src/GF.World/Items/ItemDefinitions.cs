using System.Text.Json;
using GF.Core;

namespace GF.World.Items;

/// <summary>
/// Carga los objetos desde JSON como PROTOTIPOS con herencia MÚLTIPLE. Cada entrada define un prototipo:
///   { "name": "item",   "abstract": true, "properties": { "category": "misc", "max_stack": 64 } },
///   { "name": "weapon", "abstract": true, "parents": ["item"], "properties": { "slot": "hand", "category": "weapon", "max_stack": 1, "attack": 1.0 } },
///   { "name": "knife",  "parents": ["item", "weapon"], "title": "Cuchillo", "components": ["thorns"], "properties": { "attack": 2.5 } }
///
///   parents     los prototipos de los que hereda, EN ORDEN: si dos tienen la misma propiedad vale la del ÚLTIMO (aquí, weapon sobre item). Las propias
///               del hijo mandan sobre todas. "parent" (uno solo) se admite y cuenta como el primero de la lista.
///   abstract    solo sirve de base: no es un objeto (no tiene ID) ni se pueden crear variantes de él en partida. Cualquier otro objeto sí.
///   components  componentes (comportamientos) que tiene el objeto y heredan sus hijos: ["thorns", "regeneration"]. El juego asocia cada nombre a su código.
///   properties  valores por defecto (10.0 decimal, 10 entero, "texto", true). Cualquier nombre vale; el motor entiende title, category, max_stack
///               (por defecto 64; 1 si es equipable), slot, attack, defense y use_&lt;efecto&gt; (consumibles: use_heal = 3).
///   atajos      title, category, maxStack, slot, attack, defense y use: [{ "effect": "heal", "amount": 3 }] equivalen a esas propiedades.
/// Los errores recuperables van a 'messages'; un JSON inválido, una herencia circular o inconsistente lanzan InvalidDataException.
/// El orden de los objetos concretos en el archivo da su ID numérico (solo de ejecución: al guardar se usa el nombre).
/// </summary>
public static class ItemDefinitionLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    public static ItemCatalog Load(string json, List<string> messages)
    {
        ItemsFile file;
        try { file = JsonSerializer.Deserialize<ItemsFile>(json, Options) ?? new ItemsFile(); }
        catch (JsonException e) { throw new InvalidDataException("El archivo de objetos no es válido: " + e.Message, e); }

        var specs = new Dictionary<string, ItemSpec>(StringComparer.Ordinal);
        foreach (var spec in file.Items)
        {
            if (string.IsNullOrWhiteSpace(spec.Name)) { messages.Add("Hay un objeto sin 'name': se ignora."); continue; }
            if (!specs.TryAdd(spec.Name, spec)) messages.Add($"El objeto '{spec.Name}' está repetido: se ignora la segunda definición.");
        }

        var prototypes = new Dictionary<string, Prototype>(StringComparer.Ordinal);

        Prototype Build(string name, HashSet<string> visiting)
        {
            if (prototypes.TryGetValue(name, out var done)) return done;
            var spec = specs[name];
            if (!visiting.Add(name)) throw new InvalidDataException($"Herencia circular en el objeto '{name}'.");

            var parents = new List<Prototype>();
            var parentNames = new List<string>();
            if (!string.IsNullOrWhiteSpace(spec.Parent)) parentNames.Add(spec.Parent);
            if (spec.Parents != null) parentNames.AddRange(spec.Parents);
            foreach (var parentName in parentNames)
            {
                if (!specs.ContainsKey(parentName)) { messages.Add($"'{name}': el padre '{parentName}' no existe: se ignora."); continue; }
                parents.Add(Build(parentName, visiting));
            }

            Prototype proto;
            try { proto = new Prototype(name, parents); }
            catch (InvalidOperationException e) { throw new InvalidDataException($"Objeto '{name}': {e.Message}", e); }

            // Atajos y después "properties" (lo explícito manda).
            if (spec.Title != null) Define(proto, "title", PropValue.Of(spec.Title), name, messages);
            if (spec.Category != null) Define(proto, "category", PropValue.Of(spec.Category), name, messages);
            if (spec.MaxStack != null) Define(proto, "max_stack", PropValue.Of(spec.MaxStack.Value), name, messages);
            if (spec.Slot != null) Define(proto, "slot", PropValue.Of(spec.Slot), name, messages);
            if (spec.Attack != null) Define(proto, "attack", PropValue.Of(spec.Attack.Value), name, messages);
            if (spec.Defense != null) Define(proto, "defense", PropValue.Of(spec.Defense.Value), name, messages);
            if (spec.Use != null)
                foreach (var u in spec.Use.Where(u => !string.IsNullOrWhiteSpace(u.Effect)))
                    Define(proto, ItemDef.UsePrefix + u.Effect!.Trim().ToLowerInvariant(), PropValue.Of(u.Amount), name, messages);
            if (spec.Properties != null)
                foreach (var (key, element) in spec.Properties)
                {
                    var value = ToValue(element, proto, key);
                    if (value is { } v) Define(proto, key, v, name, messages);
                    else messages.Add($"'{name}': la propiedad '{key}' tiene un valor que no se admite (solo números, booleanos y texto).");
                }

            if (spec.Components != null) foreach (var c in spec.Components.Where(c => !string.IsNullOrWhiteSpace(c))) proto.AddComponent(c);

            visiting.Remove(name);
            prototypes[name] = proto;
            return proto;
        }

        foreach (var spec in file.Items) if (specs.TryGetValue(spec.Name, out var s) && ReferenceEquals(s, spec)) Build(spec.Name, new HashSet<string>(StringComparer.Ordinal));

        var abstractNames = new HashSet<string>(specs.Values.Where(s => s.Abstract).Select(s => s.Name), StringComparer.Ordinal);
        var catalog = new ItemCatalog(prototypes, abstractNames);
        foreach (var spec in file.Items)
        {
            if (!specs.TryGetValue(spec.Name, out var s) || !ReferenceEquals(s, spec) || spec.Abstract) continue;
            var item = new ItemDef(spec.Name, prototypes[spec.Name], isRuntime: false);
            if (spec.Attack < 0 || spec.Defense < 0) messages.Add($"'{spec.Name}': attack y defense no pueden ser negativos.");
            catalog.RegisterBase(spec.Name, item);
        }
        return catalog;
    }

    private static void Define(Prototype proto, string name, PropValue value, string owner, List<string> messages)
    {
        try { proto.Define(name, value); }
        catch (InvalidOperationException e) { messages.Add($"'{owner}': {e.Message}"); }
    }

    /// <summary>El tipo lo da el ancestro que ya define la propiedad o, si es nueva, el propio valor (10.0 = decimal, 10 = entero).</summary>
    private static PropValue? ToValue(JsonElement e, Prototype proto, string name)
    {
        PropKind? known = proto.TryGetDef(PropertyIds.Of(name), out var def) ? def.Kind : null;
        switch (e.ValueKind)
        {
            case JsonValueKind.String: return PropValue.Of(e.GetString() ?? "");
            case JsonValueKind.True: return PropValue.Of(true);
            case JsonValueKind.False: return PropValue.Of(false);
            case JsonValueKind.Number:
                double number = e.GetDouble();
                if (known == PropKind.Int) return PropValue.Of((int)Math.Round(number));
                if (known == PropKind.Float) return PropValue.Of((float)number);
                bool integer = e.GetRawText().IndexOfAny(new[] { '.', 'e', 'E' }) < 0;
                return integer ? PropValue.Of((int)Math.Round(number)) : PropValue.Of((float)number);
            default: return null;
        }
    }

    // ------------------------------------------------------------------ formato del JSON

    internal sealed class ItemsFile
    {
        public List<ItemSpec> Items { get; set; } = new();
    }

    internal sealed class ItemSpec
    {
        public string Name { get; set; } = "";
        public string? Parent { get; set; }
        public List<string>? Parents { get; set; }
        public bool Abstract { get; set; }
        public List<string>? Components { get; set; }
        public string? Title { get; set; }
        public string? Category { get; set; }
        public int? MaxStack { get; set; }
        public string? Slot { get; set; }
        public float? Attack { get; set; }
        public float? Defense { get; set; }
        public List<EffectSpec>? Use { get; set; }
        public Dictionary<string, JsonElement>? Properties { get; set; }
    }

    internal sealed class EffectSpec
    {
        public string? Effect { get; set; }
        public float Amount { get; set; }
    }
}
