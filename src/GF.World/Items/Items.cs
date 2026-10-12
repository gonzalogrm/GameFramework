using GF.Core;

namespace GF.World.Items;

/// <summary>Ranuras de equipamiento.</summary>
public enum EquipSlot : byte { MainHand, Head, Chest, Feet }

/// <summary>
/// Definición de un tipo de objeto (se registra una vez en un Registry&lt;ItemDef&gt;). Slot != null = se puede equipar en esa ranura.
/// Attack suma daño (mano principal) y Defense resta daño recibido (armadura).
/// </summary>
public sealed record ItemDef(string Name, string Title, int MaxStack = 64, EquipSlot? Slot = null, float Attack = 0f, float Defense = 0f);

/// <summary>Una pila de objetos en una ranura del inventario. Count 0 = vacía.</summary>
public readonly record struct ItemStack(ushort Item, int Count)
{
    public bool IsEmpty => Count <= 0;
}

/// <summary>Para guardar: nombre del objeto (los ids numéricos dependen del orden de registro) y cantidad.</summary>
public sealed record StackRecord(string Item, int Count);
/// <summary>Para guardar: ranura equipada y nombre del objeto.</summary>
public sealed record EquipRecord(string Slot, string Item);

/// <summary>Algo con inventario y equipamiento: las entidades y el jugador.</summary>
public interface IInventoryHolder
{
    Inventory Inventory { get; }
    Equipment Equipment { get; }
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

/// <summary>Una ranura por EquipSlot con un objeto (o ninguno). Solo hilo principal.</summary>
public sealed class Equipment
{
    public const int SlotCount = 4;
    private readonly ushort?[] _items = new ushort?[SlotCount];

    public ushort? Get(EquipSlot slot) => _items[(int)slot];

    /// <summary>Pone (o quita, con null) el objeto de una ranura. Devuelve el que había.</summary>
    public ushort? Set(EquipSlot slot, ushort? item)
    {
        var previous = _items[(int)slot];
        _items[(int)slot] = item;
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

    public float Sum(Registry<ItemDef> defs, Func<ItemDef, float> value)
    {
        float total = 0f;
        foreach (var i in _items) if (i.HasValue) total += value(defs.Get(i.Value));
        return total;
    }

    public float Attack(Registry<ItemDef> defs) => Sum(defs, d => d.Attack);
    public float Defense(Registry<ItemDef> defs) => Sum(defs, d => d.Defense);

    public List<EquipRecord>? Export(Registry<ItemDef> defs)
    {
        List<EquipRecord>? list = null;
        for (int s = 0; s < SlotCount; s++)
            if (_items[s] is { } id) (list ??= new List<EquipRecord>()).Add(new EquipRecord(((EquipSlot)s).ToString(), defs.Get(id).Name));
        return list;
    }

    public void Import(IEnumerable<EquipRecord>? records, Registry<ItemDef> defs)
    {
        Array.Clear(_items);
        if (records == null) return;
        foreach (var r in records)
            if (Enum.TryParse<EquipSlot>(r.Slot, out var slot) && defs.TryGetId(r.Item, out ushort id)) _items[(int)slot] = id;
    }
}

/// <summary>Operaciones entre inventario y equipamiento.</summary>
public static class ItemOps
{
    /// <summary>Equipa la pila de la ranura 'index' en la ranura que le corresponda; el objeto que había vuelve al inventario.</summary>
    public static bool Equip(Inventory inventory, Equipment equipment, Registry<ItemDef> defs, int index)
    {
        var stack = inventory[index];
        if (stack.IsEmpty || defs.Get(stack.Item).Slot is not { } slot) return false;

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

    /// <summary>Quita lo equipado en una ranura y lo guarda en el inventario (false si no hay sitio).</summary>
    public static bool Unequip(Inventory inventory, Equipment equipment, Registry<ItemDef> defs, EquipSlot slot)
    {
        if (equipment.Get(slot) is not { } item) return false;
        if (inventory.Add(defs, item, 1) > 0) return false;
        equipment.Set(slot, null);
        return true;
    }
}
