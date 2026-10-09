using GF.World.Entities;
using GF.World.Events;
using GF.World.Voxel;

namespace MiniCraft;

/// <summary>
/// Los tipos de evento del juego y la tabla de quién procesa cuáles. Todo se declara por PROTOTIPO: los aldeanos aceptan la
/// conversación, las criaturas aceptan el daño, el tronco y las hojas aceptan el fuego... El resto de receptores (un árbol, una
/// piedra) puede recibir el mismo evento, pero no está en su lista de aceptados y no hace nada.
/// </summary>
public static class GameEvents
{
    // Jerarquía de eventos: quien acepta "dano" procesa también "golpe", "fuego" y "bola_de_fuego".
    public static readonly EventKind Conversation = new("conversacion");
    public static readonly EventKind Damage = new("dano");
    public static readonly EventKind Melee = Damage.Derive("golpe");
    public static readonly EventKind Fire = Damage.Derive("fuego");
    public static readonly EventKind Fireball = Fire.Derive("bola_de_fuego");
    public static readonly EventKind Alarm = new("alarma");

    // Los emite LightWatcher al medir la luz de las entidades: el nivel (0..15) viaja en Amount.
    public static readonly EventKind DarkEnter = new("entra_oscuridad");
    public static readonly EventKind LightEnter = new("entra_luz");

    public static void Register(EventHub hub)
    {
        // ---- Entidades ----
        // Toda criatura (aldeano, perro, caravana...) sufre daño de cualquier tipo y oye las alarmas. Se hereda por prototipo.
        hub.On<Entity>(EntityTypes.Creature, Damage, TakeDamage);
        hub.On<Entity>(EntityTypes.Creature, Alarm, HearAlarm);
        // La luz tiene consecuencias: en la oscuridad las criaturas se asustan; al volver a la luz se calman.
        hub.On<Entity>(EntityTypes.Creature, DarkEnter, GetsScared);
        hub.On<Entity>(EntityTypes.Creature, LightEnter, CalmsDown);
        // La conversación solo la procesan quienes tienen manejador: los aldeanos y los perros (cada uno a su manera).
        // Las caravanas, por ejemplo, la reciben y la ignoran.
        hub.On<Entity>(EntityTypes.VillagerProto, Conversation, VillagerTalks);
        hub.On<Entity>(EntityTypes.DogProto, Conversation, DogBarks);

        // ---- Bloques ----
        // El tronco y las hojas arden. Piedra, tierra, arena... reciben el fuego y lo ignoran; ninguno procesa la conversación.
        hub.On<BlockRef>(Blocks.Registry.Get(Blocks.Log).Props!, Fire, BlockBurns);
        hub.On<BlockRef>(Blocks.Registry.Get(Blocks.Leaves).Props!, Fire, BlockBurns);
    }

    // ------------------------------------------------------------------ manejadores de entidades

    private static void TakeDamage(Entity e, GameEvent ev, EventHub hub)
    {
        float hp = e.GetFloat("hp") - ev.Amount;
        if (hp > 0f)
        {
            e.Set("hp", hp);                                    // solo esta instancia guarda su vida nueva
            hub.Emit(e, new GameEvent(Alarm), 15);              // grita: las criaturas cercanas lo oyen
            return;
        }
        hub.Notify($"{e.Label} muere ({ev.Kind.Name})");
        hub.Entities.Remove(e);
    }

    private static void HearAlarm(Entity e, GameEvent ev, EventHub hub) =>
        e.Set("fear", e.GetFloat("fear") + 1f);

    private static void GetsScared(Entity e, GameEvent ev, EventHub hub)
    {
        e.Set("fear", e.GetFloat("fear") + 1f);
        hub.Notify($"{e.Label} se asusta en la oscuridad (luz {ev.Amount:0.0})");
    }

    private static void CalmsDown(Entity e, GameEvent ev, EventHub hub) =>
        e.Set("fear", MathF.Max(0f, e.GetFloat("fear") - 1f));

    private static void VillagerTalks(Entity e, GameEvent ev, EventHub hub)
    {
        string line =
            e.GetFloat("fear") >= 3f ? "Alejate, tengo miedo!" :
            e.GetFloat("hp") < e.GetFloat("max_hp") ? "Ay... estoy herido" :
            "Buenos dias, forastero!";
        hub.Notify($"{e.Label}: {line}");
    }

    private static void DogBarks(Entity e, GameEvent ev, EventHub hub) =>
        hub.Notify($"{e.Label}: Guau guau!");

    // ------------------------------------------------------------------ manejadores de bloques

    private static void BlockBurns(BlockRef block, GameEvent ev, EventHub hub)
    {
        if (block.TryGetFloat("durability", out float durability) && durability - ev.Amount > 0f)
            block.Set("durability", durability - ev.Amount);    // chamuscado: solo este bloque guarda el cambio
        else
            block.Destroy();
    }
}
