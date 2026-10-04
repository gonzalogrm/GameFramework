using GF.Core;
using GF.World.Entities;

namespace MiniCraft;

public static class EntityTypes
{
    public static readonly Registry<EntityDef> Registry = new();
    public static readonly ushort Villager, Caravan;

    // Como los bloques: el nombre es la clave guardada en disco, no lo cambies.
    static EntityTypes()
    {
        Villager = Registry.Register("villager", new EntityDef("villager", 3.0, 255, 200, 60, 0.6f, 1.8f));
        Caravan = Registry.Register("caravan", new EntityDef("caravan", 60.0, 230, 60, 200, 1.6f, 1.4f));
    }
}
