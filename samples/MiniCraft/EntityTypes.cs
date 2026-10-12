using GF.Core;
using GF.World.Entities;

namespace MiniCraft;

public static class EntityTypes
{
    // ---- Prototipos: propiedades y valores por defecto, compartidos por TODAS las instancias de cada tipo. ----
    // Una instancia solo guarda las propiedades que cambia (p. ej. un perro herido registra únicamente su "hp").
    public static readonly Prototype Creature = new Prototype("criatura")
        .Text("name", "")            // nombre del individuo
        .Text("species", "criatura") // nombre de la especie
        .Float("hp", 10f)
        .Float("max_hp", 10f)
        .Float("hunger", 0f)
        .Float("fear", 0f)           // sube cuando oyen una alarma cerca
        .Int("age", 0)
        .Int("level", 1);

    public static readonly Prototype VillagerProto = Creature.Derive("aldeano")
        .Text("species", "Aldeano").Float("hp", 8f).Float("max_hp", 8f);

    public static readonly Prototype CaravanProto = Creature.Derive("caravana")
        .Text("species", "Caravana").Float("hp", 40f).Float("max_hp", 40f).Int("cargo", 0);

    public static readonly Prototype DogProto = Creature.Derive("perro")
        .Text("species", "Perro").Float("hp", 12f).Float("max_hp", 12f).Float("loyalty", 0.5f);

    // El jugador no es una Entity, pero también recibe eventos (daño de los enemigos): su prototipo dice cuáles procesa.
    public static readonly Prototype PlayerProto = new Prototype("jugador");

    // Enemigos: persiguen al jugador y le atacan. Sus propiedades por defecto se comparten; cada tipo las redefine.
    public static readonly Prototype EnemyProto = Creature.Derive("enemigo")
        .Text("species", "Enemigo")
        .Float("damage", 2f)             // daño por golpe
        .Float("attack_cooldown", 1.0f) // segundos entre golpes
        .Float("aggro", 20f);            // distancia (bloques) a la que detecta y persigue al jugador

    public static readonly Prototype ZombieProto = EnemyProto.Derive("zombi")
        .Text("species", "Zombi").Float("hp", 10f).Float("max_hp", 10f);

    // ---- Tipos ----
    public static readonly Registry<EntityDef> Registry = new();
    public static readonly ushort Villager, Caravan, Dog, Zombie;

    // Como los bloques: el nombre es la clave guardada en disco y el orden da el id. Añade tipos al final.
    static EntityTypes()
    {
        Villager = Registry.Register("villager", new EntityDef("villager", 3.0, 255, 200, 60, 0.6f, 1.8f, VillagerProto));
        Caravan = Registry.Register("caravan", new EntityDef("caravan", 60.0, 230, 60, 200, 1.6f, 1.4f, CaravanProto));
        Dog = Registry.Register("dog", new EntityDef("dog", 4.5, 150, 100, 60, 0.6f, 0.8f, DogProto));
        Zombie = Registry.Register("zombie", new EntityDef("zombie", 3.3, 70, 150, 70, 0.6f, 1.8f, ZombieProto, InventorySize: 6));
    }
}
