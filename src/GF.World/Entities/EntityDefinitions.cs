using System.Text.Json;
using GF.Core;
using GF.World.Items;

namespace GF.World.Entities;

/// <summary>
/// Lo que lleva una variante de entidad al aparecer: inventario y equipamiento. Es INMUTABLE y se comparte entre todas las instancias de la
/// variante: una instancia que no lo modifica no guarda nada (ni en memoria ni en disco). Se compara por contenido, no por referencia.
/// </summary>
public sealed class LoadoutTemplate
{
    public static readonly LoadoutTemplate Empty = new(Array.Empty<ItemStack>(), Array.Empty<ushort?>());

    private readonly ItemStack[] _items;
    private readonly ushort?[] _gear;
    private readonly Dictionary<ushort, int> _totals = new();

    public LoadoutTemplate(ItemStack[] items, ushort?[] gear)
    {
        _items = items;
        _gear = gear;
        foreach (var s in items) if (!s.IsEmpty) _totals[s.Item] = _totals.GetValueOrDefault(s.Item) + s.Count;
    }

    public IReadOnlyList<ItemStack> Items => _items;
    /// <summary>Objeto de la ranura (por índice de su SlotLayout) o null.</summary>
    public ushort? Gear(int slot) => slot < _gear.Length ? _gear[slot] : null;

    /// <summary>Copia las pilas de la plantilla al inventario (que se supone vacío).</summary>
    public void FillInventory(Inventory inventory)
    {
        for (int i = 0; i < Math.Min(_items.Length, inventory.Size); i++) inventory[i] = _items[i];
    }

    /// <summary>Pone en el equipamiento lo que lleva la plantilla.</summary>
    public void FillEquipment(Equipment equipment)
    {
        for (int s = 0; s < Math.Min(_gear.Length, equipment.SlotCount); s++)
            if (_gear[s] is { } id) equipment.Set(s, id);
    }

    /// <summary>¿Tiene el inventario los mismos objetos y cantidades que la plantilla? (sin importar en qué ranura estén)</summary>
    public bool SameItems(Inventory inventory)
    {
        var totals = new Dictionary<ushort, int>();
        foreach (var s in inventory.Stacks) totals[s.Item] = totals.GetValueOrDefault(s.Item) + s.Count;
        if (totals.Count != _totals.Count) return false;
        foreach (var (item, count) in totals)
            if (!_totals.TryGetValue(item, out int expected) || expected != count) return false;
        return true;
    }

    public bool SameGear(Equipment equipment)
    {
        for (int s = 0; s < Math.Max(_gear.Length, equipment.SlotCount); s++)
            if ((s < equipment.SlotCount ? equipment.Get(s) : null) != Gear(s)) return false;
        return true;
    }
}

/// <summary>
/// Una variante de un tipo de entidad (p. ej. "zombi con armadura"): su propio prototipo (hijo del del tipo, con valores por defecto
/// distintos: más vida, más daño...) y su plantilla de inventario y equipamiento. Una instancia solo guarda QUÉ variante es.
/// </summary>
public sealed record EntityVariant(string Name, int Weight, Prototype Prototype, LoadoutTemplate Loadout);

/// <summary>Lo que define un tipo (también los abstractos, como el del jugador) además de su prototipo: ranuras, inventario y equipo iniciales.</summary>
public sealed record EntityTypeInfo(SlotLayout Slots, LoadoutTemplate Loadout, int InventorySize);

/// <summary>Los tipos de entidad cargados de un JSON: sus prototipos (por nombre, también los abstractos) y el registro de los instanciables.</summary>
public sealed class EntityTypeSet
{
    private readonly Dictionary<string, Prototype> _prototypes;
    private readonly Dictionary<string, EntityTypeInfo> _infos;

    internal EntityTypeSet(Registry<EntityDef> types, Dictionary<string, Prototype> prototypes, Dictionary<string, EntityTypeInfo> infos)
    {
        Types = types;
        _prototypes = prototypes;
        _infos = infos;
    }

    /// <summary>Ranuras, inventario y equipo de un tipo por nombre (p. ej. "player": con qué empieza el jugador).</summary>
    public EntityTypeInfo Info(string name) =>
        _infos.TryGetValue(name, out var i) ? i : throw new KeyNotFoundException($"El archivo de tipos no define '{name}'.");

    /// <summary>Tipos instanciables, en el orden del archivo (el id numérico es solo de ejecución: al guardar se usa el nombre).</summary>
    public Registry<EntityDef> Types { get; }

    public Prototype Get(string name) =>
        _prototypes.TryGetValue(name, out var p) ? p : throw new KeyNotFoundException($"El archivo de tipos no define '{name}'.");
}

/// <summary>
/// Carga los tipos de entidad desde JSON. Cada tipo es un prototipo que puede heredar de otro; los que no son "abstract" se pueden crear.
///   { "types": [
///       { "name": "creature", "abstract": true, "properties": { "hp": 10.0, "level": 1 } },
///       { "name": "ogre", "parent": "creature", "speed": 2.4, "size": [1.0, 2.4], "color": [150,110,60], "inventorySize": 6,
///         "slots": [ "hand", "hand", "hand", "head" ],        // tres brazos: ranuras hand, hand2, hand3, y cabeza
///         "properties": { "hp": 30.0 },  "spawnWeight": 1,
///         "inventory": [ { "item": "bone", "count": 2 } ],  "equipment": { "hand": "club", "hand2": "club", "hand3": "club" },
///         "variants": [ { "name": "brute", "weight": 5 },
///                       { "name": "warlord", "weight": 1, "equipment": { "hand": "club", "hand2": "club", "hand3": "iron_sword", "head": "iron_helmet" } } ] } ] }
/// El tipo de una propiedad (entero, decimal, booleano, texto) lo da el ancestro que la define o, si es nueva, el propio valor (10.0 =
/// decimal, 10 = entero). 'inventory' y 'equipment' de una variante sustituyen a los del tipo; si no los indica, hereda los del tipo.
/// Los errores recuperables (objeto desconocido...) se añaden a 'messages'; un JSON inválido o una herencia circular lanzan InvalidDataException.
/// </summary>
public static class EntityDefinitionLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    private sealed record Resolved(Prototype Proto, double Speed, float Width, float Height, byte R, byte G, byte B, int InventorySize,
        SlotLayout Slots, LoadoutTemplate Loadout, List<EntityVariant> Variants);

    public static EntityTypeSet Load(string json, Registry<ItemDef> items, List<string> messages)
    {
        TypesFile file;
        try { file = JsonSerializer.Deserialize<TypesFile>(json, Options) ?? new TypesFile(); }
        catch (JsonException e) { throw new InvalidDataException("El archivo de tipos de entidad no es válido: " + e.Message, e); }

        var specs = new Dictionary<string, TypeSpec>(StringComparer.Ordinal);
        foreach (var t in file.Types)
        {
            if (string.IsNullOrWhiteSpace(t.Name)) { messages.Add("Hay un tipo sin 'name': se ignora."); continue; }
            if (!specs.TryAdd(t.Name, t)) messages.Add($"El tipo '{t.Name}' está repetido: se ignora la segunda definición.");
        }

        var resolved = new Dictionary<string, Resolved>(StringComparer.Ordinal);
        var prototypes = new Dictionary<string, Prototype>(StringComparer.Ordinal);
        var infos = new Dictionary<string, EntityTypeInfo>(StringComparer.Ordinal);

        Resolved Resolve(string name, HashSet<string> visiting)
        {
            if (resolved.TryGetValue(name, out var done)) return done;
            if (!specs.TryGetValue(name, out var spec)) throw new InvalidDataException($"El tipo '{name}' no está definido.");
            if (!visiting.Add(name)) throw new InvalidDataException($"Herencia circular en el tipo '{name}'.");

            var parent = spec.Parent != null ? Resolve(spec.Parent, visiting) : null;
            string title = spec.Title ?? spec.Name;
            var proto = new Prototype(title, parent?.Proto);
            ApplyProperties(proto, spec.Properties, name, messages);

            float width = spec.Size is { Length: >= 2 } size ? size[0] : parent?.Width ?? 0.6f;
            float height = spec.Size is { Length: >= 2 } size2 ? size2[1] : parent?.Height ?? 1.8f;
            var color = spec.Color is { Length: >= 3 } c ? c : null;
            int inventorySize = spec.InventorySize ?? parent?.InventorySize ?? 8;
            var slots = spec.Slots is { Length: > 0 } ? new SlotLayout(spec.Slots) : parent?.Slots ?? SlotLayout.Default;
            var loadout = BuildLoadout(spec.Inventory, spec.Equipment, parent?.Loadout, items, inventorySize, slots, messages, name);

            var variants = new List<EntityVariant>();
            if (!spec.Abstract && spec.Variants is { Count: > 0 })
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var v in spec.Variants)
                {
                    string vname = string.IsNullOrWhiteSpace(v.Name) ? $"v{variants.Count}" : v.Name;
                    if (!names.Add(vname)) { messages.Add($"'{name}': la variante '{vname}' está repetida: se ignora."); continue; }
                    var vproto = new Prototype($"{title}/{vname}", proto);
                    ApplyProperties(vproto, v.Properties, $"{name}/{vname}", messages);
                    var vload = BuildLoadout(v.Inventory, v.Equipment, loadout, items, inventorySize, slots, messages, $"{name}/{vname}");
                    variants.Add(new EntityVariant(vname, Math.Max(0, v.Weight), vproto, vload));
                }
            }
            if (variants.Count == 0) variants.Add(new EntityVariant("default", 1, proto, loadout));

            var result = new Resolved(proto, spec.Speed ?? parent?.Speed ?? 3.0, width, height,
                color != null ? (byte)color[0] : parent?.R ?? 200, color != null ? (byte)color[1] : parent?.G ?? 200,
                color != null ? (byte)color[2] : parent?.B ?? 200, inventorySize, slots, loadout, variants);
            visiting.Remove(name);
            resolved[name] = result;
            prototypes[name] = proto;
            infos[name] = new EntityTypeInfo(slots, loadout, inventorySize);
            return result;
        }

        foreach (var t in file.Types) if (specs.ContainsKey(t.Name)) Resolve(t.Name, new HashSet<string>(StringComparer.Ordinal));

        // Registro de los instanciables, en el orden del archivo.
        var types = new Registry<EntityDef>();
        foreach (var t in file.Types)
        {
            if (!specs.TryGetValue(t.Name, out var spec) || !ReferenceEquals(spec, t) || spec.Abstract) continue;
            var r = resolved[t.Name];
            types.Register(t.Name, new EntityDef(t.Name, r.Speed, r.R, r.G, r.B, r.Width, r.Height, r.Proto, r.InventorySize, r.Variants,
                r.Slots, Math.Max(0, spec.SpawnWeight)));
        }
        return new EntityTypeSet(types, prototypes, infos);
    }

    private static void ApplyProperties(Prototype proto, Dictionary<string, JsonElement>? properties, string owner, List<string> messages)
    {
        if (properties == null) return;
        foreach (var (key, element) in properties)
        {
            try
            {
                var value = ToValue(element, proto, key);
                if (value is { } v) proto.Define(key, v);
                else messages.Add($"'{owner}': la propiedad '{key}' tiene un valor que no se admite (solo números, booleanos y texto).");
            }
            catch (InvalidOperationException e) { messages.Add($"'{owner}': {e.Message}"); }
        }
    }

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
                string raw = e.GetRawText();
                bool integer = raw.IndexOfAny(new[] { '.', 'e', 'E' }) < 0;
                return integer ? PropValue.Of((int)Math.Round(number)) : PropValue.Of((float)number);
            default: return null;
        }
    }

    private static LoadoutTemplate BuildLoadout(List<ItemSpec>? inventory, Dictionary<string, string>? equipment, LoadoutTemplate? inherited,
        Registry<ItemDef> items, int inventorySize, SlotLayout layout, List<string> messages, string owner)
    {
        var stacks = new List<ItemStack>();
        if (inventory != null)
        {
            foreach (var spec in inventory)
            {
                if (!items.TryGetId(spec.Item ?? "", out ushort id)) { messages.Add($"'{owner}': el objeto '{spec.Item}' no existe."); continue; }
                int max = Math.Max(1, items.Get(id).MaxStack), left = Math.Max(1, spec.Count);
                while (left > 0)
                {
                    int take = Math.Min(max, left);
                    stacks.Add(new ItemStack(id, take));
                    left -= take;
                }
            }
            if (stacks.Count > inventorySize)
            {
                messages.Add($"'{owner}': el inventario tiene {stacks.Count} ranuras y el tipo solo {inventorySize}: se recorta.");
                stacks.RemoveRange(inventorySize, stacks.Count - inventorySize);
            }
        }
        else if (inherited != null) stacks.AddRange(inherited.Items);

        var gear = new ushort?[layout.Count];
        if (equipment != null)
        {
            foreach (var (slotName, itemName) in equipment)
            {
                int slot = layout.IndexOf(slotName);
                if (slot < 0) { messages.Add($"'{owner}': la ranura '{slotName}' no existe en este tipo."); continue; }
                if (!items.TryGetId(itemName, out ushort id)) { messages.Add($"'{owner}': el objeto '{itemName}' no existe."); continue; }
                if (items.Get(id).Slot != layout.Kind(slot)) { messages.Add($"'{owner}': '{itemName}' no se puede equipar en '{slotName}'."); continue; }
                gear[slot] = id;
            }
        }
        else if (inherited != null)
            for (int s = 0; s < gear.Length; s++) gear[s] = inherited.Gear(s);

        return stacks.Count == 0 && gear.All(g => !g.HasValue) ? LoadoutTemplate.Empty : new LoadoutTemplate(stacks.ToArray(), gear);
    }

    // ------------------------------------------------------------------ formato del JSON

    internal sealed class TypesFile
    {
        public List<TypeSpec> Types { get; set; } = new();
    }

    internal sealed class TypeSpec
    {
        public string Name { get; set; } = "";
        public string? Title { get; set; }
        public string? Parent { get; set; }
        public bool Abstract { get; set; }
        public double? Speed { get; set; }
        public float[]? Size { get; set; }
        public int[]? Color { get; set; }
        public int? InventorySize { get; set; }
        public string[]? Slots { get; set; }
        public int SpawnWeight { get; set; }
        public Dictionary<string, JsonElement>? Properties { get; set; }
        public List<ItemSpec>? Inventory { get; set; }
        public Dictionary<string, string>? Equipment { get; set; }
        public List<VariantSpec>? Variants { get; set; }
    }

    internal sealed class VariantSpec
    {
        public string Name { get; set; } = "";
        public int Weight { get; set; } = 1;
        public Dictionary<string, JsonElement>? Properties { get; set; }
        public List<ItemSpec>? Inventory { get; set; }
        public Dictionary<string, string>? Equipment { get; set; }
    }

    internal sealed class ItemSpec
    {
        public string? Item { get; set; }
        public int Count { get; set; } = 1;
    }
}
