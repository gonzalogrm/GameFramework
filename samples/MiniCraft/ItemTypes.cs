using GF.Core;
using GF.World.Items;

namespace MiniCraft;

/// <summary>
/// Los objetos del juego (loot, armas y armaduras). Como los bloques y las entidades: el nombre es la clave guardada en disco y el orden
/// da el id numérico; añade objetos nuevos AL FINAL.
/// </summary>
public static class ItemTypes
{
    public static readonly Registry<ItemDef> Registry = new();
    public static readonly ushort Bone, RottenFlesh, WoodSword, IronSword, LeatherCap, LeatherVest, LeatherBoots, IronHelmet;

    static ItemTypes()
    {
        Bone = Registry.Register("bone", new ItemDef("bone", "Hueso"));
        RottenFlesh = Registry.Register("rotten_flesh", new ItemDef("rotten_flesh", "Carne podrida"));
        WoodSword = Registry.Register("wood_sword", new ItemDef("wood_sword", "Espada de madera", 1, EquipSlot.MainHand, Attack: 2f));
        IronSword = Registry.Register("iron_sword", new ItemDef("iron_sword", "Espada de hierro", 1, EquipSlot.MainHand, Attack: 4f));
        LeatherCap = Registry.Register("leather_cap", new ItemDef("leather_cap", "Gorro de cuero", 1, EquipSlot.Head, Defense: 1f));
        LeatherVest = Registry.Register("leather_vest", new ItemDef("leather_vest", "Peto de cuero", 1, EquipSlot.Chest, Defense: 2f));
        LeatherBoots = Registry.Register("leather_boots", new ItemDef("leather_boots", "Botas de cuero", 1, EquipSlot.Feet, Defense: 1f));
        IronHelmet = Registry.Register("iron_helmet", new ItemDef("iron_helmet", "Casco de hierro", 1, EquipSlot.Head, Defense: 2f));
    }
}
