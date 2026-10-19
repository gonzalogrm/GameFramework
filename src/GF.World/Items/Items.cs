using GF.Core;

namespace GF.World.Items;

/// <summary>Un efecto de un objeto consumible al usarlo ("heal", 10). Qué significa cada tipo lo decide el juego.</summary>
public sealed record ItemEffect(string Type, float Amount);

/// <summary>
/// Un tipo de objeto. Es una VISTA de un Prototype: todas sus propiedades (daño, defensa, apilado, ranura, efectos...) salen de él y de sus ancestros, que
/// pueden ser VARIOS (herencia múltiple: en  knife = [item, weapon]  gana el último, weapon). El inventario y el equipamiento solo guardan el ID de
/// este objeto, así que un prototipo lo comparten miles de entidades sin copiarlo.
///
/// Propiedades que el motor entiende (todas opcionales, se heredan y se pueden redefinir):
///   title (texto), category (texto), max_stack (entero; por defecto 64, o 1 si es equipable), slot (texto: tipo de ranura donde se equipa),
///   attack y defense (decimales), use_&lt;efecto&gt; (decimal: consumible; use_heal = 3 cura 3). Cualquier otra propiedad se consulta con Get.
/// </summary>
public sealed class ItemDef
{
    public const string UsePrefix = "use_";

    /// <summary>Atajo para crear un objeto suelto (sin herencia) desde código o tests: se arma un prototipo propio con estos valores.</summary>
    public ItemDef(string Name, string Title, int MaxStack = 64, string? Slot = null, float Attack = 0f, float Defense = 0f,
        string Category = "misc", IReadOnlyList<ItemEffect>? Use = null)
        : this(Name, Build(Name, Title, MaxStack, Slot, Attack, Defense, Category, Use), false) { }

    internal ItemDef(string name, Prototype prototype, bool isRuntime)
    {
        Name = name;
        Prototype = prototype;
        IsRuntime = isRuntime;

        string title = Get("title") is { Kind: PropKind.Text } t ? t.AsText : "";
        Title = title.Length > 0 ? title : name;
        string slot = Get("slot") is { Kind: PropKind.Text } sl ? sl.AsText.Trim().ToLowerInvariant() : "";
        Slot = slot.Length > 0 ? slot : null;
        Attack = Math.Max(0f, Get("attack")?.AsFloat ?? 0f);
        Defense = Math.Max(0f, Get("defense")?.AsFloat ?? 0f);

        List<ItemEffect>? effects = null;
        foreach (var def in prototype.AllDefs())
        {
            string propName = PropertyIds.NameOf(def.Id);
            if (propName.Length > UsePrefix.Length && propName.StartsWith(UsePrefix, StringComparison.Ordinal) && def.Default.Number != 0)
                (effects ??= new List<ItemEffect>()).Add(new ItemEffect(propName[UsePrefix.Length..], (float)def.Default.Number));
        }
        Use = effects;
        Components = prototype.AllComponents();

        // Sin max_stack: 64 por defecto, o 1 si se equipa.
        MaxStack = Get("max_stack") is { } ms ? Math.Max(1, ms.AsInt) : Slot != null ? 1 : 64;
        string category = Get("category") is { Kind: PropKind.Text } c ? c.AsText.Trim().ToLowerInvariant() : "";
        Category = category.Length > 0 ? category : Slot != null ? (Attack > Defense ? "weapon" : "armor") : Use != null ? "consumable" : "misc";
    }

    public string Name { get; }
    public string Title { get; }
    /// <summary>El prototipo (con su herencia) del que salen todas las propiedades.</summary>
    public Prototype Prototype { get; }
    /// <summary>true = creado durante la partida como variante de otro objeto (se guarda con el mundo). false = definido en el JSON de objetos.</summary>
    public bool IsRuntime { get; }
    public int MaxStack { get; }
    /// <summary>Tipo de ranura donde se equipa ("hand", "head"...), o null.</summary>
    public string? Slot { get; }
    public float Attack { get; }
    public float Defense { get; }
    public string Category { get; }
    public IReadOnlyList<ItemEffect>? Use { get; }
    /// <summary>Componentes (propios y heredados): el juego les asocia comportamientos (eventos que recibe, efectos por tick...).</summary>
    public IReadOnlyList<string> Components { get; }
    public bool HasComponent(string name) => Prototype.HasComponent(name);

    public bool IsEquippable => Slot != null;
    public bool IsUsable => Use is { Count: > 0 };

    /// <summary>¿Hereda (directa o indirectamente) de este prototipo? Polimorfismo: un cuchillo "es" un arma y es un objeto.</summary>
    public bool Is(Prototype other) => Prototype.Is(other);

    /// <summary>Una propiedad cualquiera, ya resuelta por la herencia (null si ningún ancestro la define).</summary>
    public PropValue? Get(string name) =>
        PropertyOps.TryGet(Prototype, null, PropertyIds.Of(name), out var v) ? v : null;

    private static Prototype Build(string name, string title, int maxStack, string? slot, float attack, float defense, string category,
        IReadOnlyList<ItemEffect>? use)
    {
        var p = new Prototype(name).Text("title", title).Int("max_stack", maxStack).Text("category", category)
            .Float("attack", attack).Float("defense", defense);
        if (slot != null) p.Text("slot", slot);
        if (use != null) foreach (var e in use) p.Float(UsePrefix + e.Type, e.Amount);
        return p;
    }
}

/// <summary>Una pila de objetos en una ranura del inventario. Count 0 = vacía.</summary>
public readonly record struct ItemStack(ushort Item, int Count)
{
    public bool IsEmpty => Count <= 0;
}

/// <summary>Para guardar: nombre del objeto (los ids numéricos dependen del orden de carga) y cantidad.</summary>
public sealed record StackRecord(string Item, int Count);
/// <summary>Para guardar: nombre de la ranura de equipamiento ("hand", "hand2", "head"...) y nombre del objeto.</summary>
public sealed record EquipRecord(string Slot, string Item);

/// <summary>Algo con inventario y equipamiento: las entidades y el jugador.</summary>
public interface IInventoryHolder
{
    Inventory Inventory { get; }
    Equipment Equipment { get; }
}

/// <summary>
/// Las ranuras de equipamiento de un tipo de criatura: una lista de TIPOS de ranura, p. ej. ["hand", "hand", "hand", "head"] para un enemigo
/// de tres brazos. Cada ranura tiene un nombre único ("hand", "hand2", "hand3", "head") que es lo que se guarda. Inmutable y compartida.
/// </summary>
public sealed class SlotLayout
{
    public static readonly SlotLayout Default = new(new[] { "hand", "head", "chest", "feet" });

    private readonly string[] _kinds;
    private readonly string[] _names;

    public SlotLayout(IEnumerable<string> kinds)
    {
        _kinds = kinds.Select(k => k.Trim().ToLowerInvariant()).ToArray();
        _names = new string[_kinds.Length];
        var seen = new Dictionary<string, int>();
        for (int i = 0; i < _kinds.Length; i++)
        {
            int n = seen.GetValueOrDefault(_kinds[i]) + 1;
            seen[_kinds[i]] = n;
            _names[i] = n == 1 ? _kinds[i] : _kinds[i] + n;
        }
    }

    public int Count => _kinds.Length;
    public string Kind(int slot) => _kinds[slot];
    public string Name(int slot) => _names[slot];

    /// <summary>Índice de una ranura por nombre ("hand2"). Acepta mayúsculas y el nombre antiguo "mainhand". -1 si no existe.</summary>
    public int IndexOf(string name)
    {
        name = name.Trim().ToLowerInvariant();
        if (name == "mainhand") name = "hand";
        return Array.IndexOf(_names, name);
    }
}

/// <summary>Inventario de tamaño fijo (ranuras con pilas). Solo hilo principal.</summary>
public sealed class Inventory
{
    private readonly ItemStack[] _slots;

    public Inventory(int size) => _slots = new ItemStack[Math.Max(1, size)];

    public int Size => _slots.Length;

    public ItemStack this[int index]
    {
        get => _slots[index];
        set => _slots[index] = value.Count <= 0 ? default : value;
    }

    public bool IsEmpty
    {
        get
        {
            foreach (var s in _slots) if (!s.IsEmpty) return false;
            return true;
        }
    }

    /// <summary>Las pilas que no están vacías.</summary>
    public IEnumerable<ItemStack> Stacks
    {
        get { foreach (var s in _slots) if (!s.IsEmpty) yield return s; }
    }

    public int CountOf(ushort item)
    {
        int n = 0;
        foreach (var s in _slots) if (!s.IsEmpty && s.Item == item) n += s.Count;
        return n;
    }

    /// <summary>Añade 'count' unidades (completa pilas y usa ranuras vacías). Devuelve las que NO cupieron.</summary>
    public int Add(Registry<ItemDef> defs, ushort item, int count)
    {
        int max = Math.Max(1, defs.Get(item).MaxStack);
        for (int i = 0; i < _slots.Length && count > 0; i++)
        {
            var s = _slots[i];
            if (s.IsEmpty || s.Item != item || s.Count >= max) continue;
            int take = Math.Min(max - s.Count, count);
            _slots[i] = new ItemStack(item, s.Count + take);
            count -= take;
        }
        for (int i = 0; i < _slots.Length && count > 0; i++)
        {
            if (!_slots[i].IsEmpty) continue;
            int take = Math.Min(max, count);
            _slots[i] = new ItemStack(item, take);
            count -= take;
        }
        return count;
    }

    /// <summary>Quita hasta 'count' unidades. Devuelve las que se quitaron.</summary>
    public int Remove(ushort item, int count)
    {
        int removed = 0;
        for (int i = 0; i < _slots.Length && removed < count; i++)
        {
            var s = _slots[i];
            if (s.IsEmpty || s.Item != item) continue;
            int take = Math.Min(s.Count, count - removed);
            this[i] = new ItemStack(item, s.Count - take);
            removed += take;
        }
        return removed;
    }

    public void Clear() => Array.Clear(_slots);

    /// <summary>Pilas por nombre, para guardar. null si está vacío (no ocupa nada en el archivo).</summary>
    public List<StackRecord>? Export(Registry<ItemDef> defs)
    {
        List<StackRecord>? list = null;
        foreach (var s in _slots)
            if (!s.IsEmpty) (list ??= new List<StackRecord>()).Add(new StackRecord(defs.Get(s.Item).Name, s.Count));
        return list;
    }

    /// <summary>Sustituye el contenido. Los objetos que ya no existen en el registro se ignoran.</summary>
    public void Import(IEnumerable<StackRecord>? records, Registry<ItemDef> defs)
    {
        Clear();
        if (records == null) return;
        foreach (var r in records)
            if (defs.TryGetId(r.Item, out ushort id)) Add(defs, id, r.Count);
    }
}

/// <summary>Un objeto (o ninguno) por ranura de un SlotLayout. Solo hilo principal.</summary>
public sealed class Equipment
{
    private readonly SlotLayout _layout;
    private readonly ushort?[] _items;

    public Equipment() : this(SlotLayout.Default) { }

    public Equipment(SlotLayout layout)
    {
        _layout = layout;
        _items = new ushort?[layout.Count];
    }

    public SlotLayout Layout => _layout;
    public int SlotCount => _items.Length;

    public ushort? Get(int slot) => _items[slot];

    /// <summary>Pone (o quita, con null) el objeto de una ranura. Devuelve el que había.</summary>
    public ushort? Set(int slot, ushort? item)
    {
        var previous = _items[slot];
        _items[slot] = item;
        return previous;
    }

    public bool IsEmpty
    {
        get
        {
            foreach (var i in _items) if (i.HasValue) return false;
            return true;
        }
    }

    /// <summary>Una ranura de ese tipo: la primera vacía y, si todas están ocupadas, la primera. -1 si no hay ninguna.</summary>
    public int FindSlot(string kind)
    {
        int first = -1;
        for (int i = 0; i < _items.Length; i++)
        {
            if (_layout.Kind(i) != kind) continue;
            if (!_items[i].HasValue) return i;
            if (first < 0) first = i;
        }
        return first;
    }

    public float Sum(Registry<ItemDef> defs, Func<ItemDef, float> value)
    {
        float total = 0f;
        foreach (var i in _items) if (i.HasValue) total += value(defs.Get(i.Value));
        return total;
    }

    /// <summary>Daño que suman TODOS los objetos equipados: tres brazos con tres armas hacen el daño de las tres.</summary>
    public float Attack(Registry<ItemDef> defs) => Sum(defs, d => d.Attack);
    public float Defense(Registry<ItemDef> defs) => Sum(defs, d => d.Defense);

    public List<EquipRecord>? Export(Registry<ItemDef> defs)
    {
        List<EquipRecord>? list = null;
        for (int s = 0; s < _items.Length; s++)
            if (_items[s] is { } id) (list ??= new List<EquipRecord>()).Add(new EquipRecord(_layout.Name(s), defs.Get(id).Name));
        return list;
    }

    public void Import(IEnumerable<EquipRecord>? records, Registry<ItemDef> defs)
    {
        Array.Clear(_items);
        if (records == null) return;
        foreach (var r in records)
        {
            int slot = _layout.IndexOf(r.Slot);
            if (slot >= 0 && defs.TryGetId(r.Item, out ushort id)) _items[slot] = id;
        }
    }
}

/// <summary>Operaciones entre inventario, equipamiento y uso de objetos.</summary>
public static class ItemOps
{
    /// <summary>Equipa la pila de la ranura 'index' en una ranura de su tipo (una vacía si hay); el objeto que había vuelve al inventario.</summary>
    public static bool Equip(Inventory inventory, Equipment equipment, Registry<ItemDef> defs, int index)
    {
        var stack = inventory[index];
        if (stack.IsEmpty || defs.Get(stack.Item).Slot is not { } kind) return false;
        int slot = equipment.FindSlot(kind);
        if (slot < 0) return false;

        var previous = equipment.Set(slot, stack.Item);
        inventory[index] = new ItemStack(stack.Item, stack.Count - 1);
        if (previous is { } old && inventory.Add(defs, old, 1) > 0)
        {
            equipment.Set(slot, old);   // no cabe: se deshace
            inventory[index] = stack;
            return false;
        }
        return true;
    }

    /// <summary>Quita lo equipado en una ranura y lo guarda en el inventario (false si no hay nada o no hay sitio).</summary>
    public static bool Unequip(Inventory inventory, Equipment equipment, Registry<ItemDef> defs, int slot)
    {
        if (equipment.Get(slot) is not { } item) return false;
        if (inventory.Add(defs, item, 1) > 0) return false;
        equipment.Set(slot, null);
        return true;
    }

    /// <summary>
    /// Usa una unidad de un consumible: aplica cada efecto con 'apply' (devuelve si tuvo efecto: p. ej. curar con la vida llena no lo tiene)
    /// y, si alguno lo tuvo, gasta la unidad.
    /// </summary>
    public static bool Use(Inventory inventory, int index, Registry<ItemDef> defs, Func<ItemEffect, bool> apply)
    {
        var stack = inventory[index];
        if (stack.IsEmpty || defs.Get(stack.Item).Use is not { Count: > 0 } effects) return false;
        bool any = false;
        foreach (var effect in effects) any |= apply(effect);
        if (any) inventory[index] = new ItemStack(stack.Item, stack.Count - 1);
        return any;
    }
}
