using GF.World.Entities;
using GF.World.Events;

namespace MiniCraft;

/// <summary>
/// Diálogos de los NPC especiales de las ciudades. Como todo lo demás, se declaran por PROTOTIPO: cada tipo del JSON procesa el evento de conversación
/// a su manera (el resto de aldeanos usa VillagerTalks). Llamar a Register después de GameEvents.Register.
/// </summary>
public static class CityNpcs
{
    public static void Register(EventHub hub)
    {
        On(hub, "merchant", MerchantTalks);
        On(hub, "blacksmith", BlacksmithTalks);
        On(hub, "mayor", MayorTalks);
        On(hub, "guard", GuardTalks);
    }

    private static void On(EventHub hub, string type, Action<Entity, GameEvent, EventHub> handler)
    {
        if (!EntityTypes.Registry.TryGetId(type, out ushort id)) return;   // el tipo no está en entitytypes.json: se ignora
        if (EntityTypes.Registry.Get(id).Prototype is { } proto) hub.On<Entity>(proto, GameEvents.Conversation, handler);
    }

    private static void MerchantTalks(Entity e, GameEvent ev, EventHub hub)
    {
        var defs = ItemTypes.Registry;
        string stock = string.Join(", ", e.Inventory.Stacks.Select(s => $"{defs.Get(s.Item).Title} x{s.Count}"));
        hub.Notify($"{e.Label}: ¡Pasa, forastero! Hoy tengo {(stock.Length > 0 ? stock : "poco")}.");
    }

    private static void BlacksmithTalks(Entity e, GameEvent ev, EventHub hub) =>
        hub.Notify($"{e.Label}: Una buena hoja vale más que mil palabras. ¿Necesitas acero?");

    private static void MayorTalks(Entity e, GameEvent ev, EventHub hub) =>
        hub.Notify($"{e.Label}: Bienvenido a nuestra ciudad. Mientras los guardas vigilen, aquí estás a salvo.");

    private static void GuardTalks(Entity e, GameEvent ev, EventHub hub) =>
        hub.Notify($"{e.Label}: " + (e.GetFloat("hp") < e.GetFloat("max_hp") ? "He tenido días mejores, pero sigo en mi puesto." : "Todo tranquilo. Vigilo la ciudad."));
}
