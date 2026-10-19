using GF.Core;
using GF.World.Events;
using GF.World.Items;

namespace MiniCraft;

/// <summary>
/// El jugador como combatiente: puntos de vida, inventario y equipamiento (los mismos tipos que usan las entidades). Es un receptor de
/// eventos: los enemigos le envían daño por el EventHub y GameEvents declara cómo lo procesa (la armadura resta daño).
/// </summary>
public sealed class PlayerState : IEventReceiver, IInventoryHolder
{
    public const float DefaultMaxHp = 20f;

    public float Hp { get; set; } = DefaultMaxHp;
    public float MaxHp { get; set; } = DefaultMaxHp;
    /// <summary>Segundos desde el último golpe recibido (la vida se regenera tras unos segundos sin daño).</summary>
    public float SecondsSinceHurt { get; set; } = 99f;
    /// <summary>Segundos de invulnerabilidad que quedan (tras reaparecer).</summary>
    public float GraceSeconds { get; set; }

    // Tamaño del inventario, ranuras y equipo inicial: los define el tipo "player" de entitytypes.json.
    public Inventory Inventory { get; } = new(EntityTypes.PlayerInfo.InventorySize);
    public Equipment Equipment { get; } = new(EntityTypes.PlayerInfo.Slots);

    // IEventReceiver: qué eventos procesa lo decide su prototipo.
    public Prototype? Prototype => EntityTypes.PlayerProto;
    public string Label => "Jugador";

    /// <summary>Daño de un golpe: 1 con los puños, más el de lo que lleve en la mano.</summary>
    public float AttackPower => 1f + Combat.Attack(this);

    /// <summary>Con lo que empieza una partida nueva: el inventario y el equipamiento del tipo "player" del JSON.</summary>
    public void GiveStarterKit()
    {
        Inventory.Clear();
        EntityTypes.PlayerInfo.Loadout.FillInventory(Inventory);
        EntityTypes.PlayerInfo.Loadout.FillEquipment(Equipment);
    }

    /// <summary>Usa un consumible del inventario. Devuelve el mensaje, o null si no se pudo (no es consumible o no tendría efecto).</summary>
    public string? UseItem(int index)
    {
        var defs = ItemTypes.Registry;
        var stack = Inventory[index];
        if (stack.IsEmpty) return null;
        string title = defs.Get(stack.Item).Title;
        float before = Hp;
        return ItemOps.Use(Inventory, index, defs, ApplyEffect) ? $"{title}: +{Hp - before:0.#} HP" : null;
    }

    private bool ApplyEffect(ItemEffect effect)
    {
        switch (effect.Type)
        {
            case "heal":
                if (Hp >= MaxHp) return false;   // curar con la vida llena no gasta el objeto
                Hp = Math.Min(MaxHp, Hp + effect.Amount);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// El personaje es UNIVERSAL: vida, inventario y equipo viajan de un mundo a otro. Los objetos creados en partida (legendarios, armas únicas) solo existen en el
    /// catálogo de su mundo, así que se guardan AQUÍ las definiciones de los que lleva (solo el delta) y cada mundo las recrea al abrirse.
    /// </summary>
    public PlayerSave Export(ItemCatalog catalog)
    {
        var defs = catalog.Registry;
        var carried = new List<string>();
        foreach (var stack in Inventory.Stacks) if (defs.Get(stack.Item).IsRuntime) carried.Add(defs.Get(stack.Item).Name);
        for (int s = 0; s < Equipment.SlotCount; s++) if (Equipment.Get(s) is { } id && defs.Get(id).IsRuntime) carried.Add(defs.Get(id).Name);
        var items = catalog.ExportRuntimeFor(carried);
        return new PlayerSave
        {
            Hp = Hp,
            Inventory = Inventory.Export(defs),
            Gear = Equipment.Export(defs),
            Items = items.Count == 0 ? null : items,
        };
    }

    public void Restore(PlayerSave save)
    {
        Hp = save.Hp > 0f ? Math.Min(save.Hp, MaxHp) : MaxHp;
        Inventory.Import(save.Inventory, ItemTypes.Registry);
        Equipment.Import(save.Gear, ItemTypes.Registry);
    }
}

/// <summary>Estadísticas de combate calculadas con lo que lleva equipado cualquier entidad o el jugador.</summary>
public static class Combat
{
    public static float Attack(IInventoryHolder who) => who.Equipment.Attack(ItemTypes.Registry);
    public static float Defense(IInventoryHolder who) => who.Equipment.Defense(ItemTypes.Registry);

    /// <summary>Pasa todo lo que lleva 'from' (inventario y equipo) al inventario del jugador. Devuelve el texto " +2 Hueso, ..." (o vacío).</summary>
    public static string Loot(IInventoryHolder from, PlayerState to)
    {
        var defs = ItemTypes.Registry;
        var parts = new List<string>();
        foreach (var stack in from.Inventory.Stacks)
        {
            int taken = stack.Count - to.Inventory.Add(defs, stack.Item, stack.Count);
            if (taken > 0) parts.Add($"{taken} {defs.Get(stack.Item).Title}");
        }
        for (int slot = 0; slot < from.Equipment.SlotCount; slot++)
            if (from.Equipment.Get(slot) is { } item && to.Inventory.Add(defs, item, 1) == 0) parts.Add(defs.Get(item).Title);
        return parts.Count == 0 ? "" : " +" + string.Join(", ", parts);
    }
}
