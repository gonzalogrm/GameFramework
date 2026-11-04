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
        On(hub, "hermit", HermitTalks);
        On(hub, "archaeologist", ArchaeologistTalks);
        On(hub, "engineer", EngineerTalks);
        On(hub, "reactor_tech", ReactorTechTalks);
        On(hub, "foreman", ForemanTalks);
        On(hub, "pilot", PilotTalks);
        On(hub, "astronaut", AstronautTalks);
        On(hub, "astronomer", AstronomerTalks);
        On(hub, "archivist", ArchivistTalks);
        On(hub, "lighthouse_keeper", KeeperTalks);
        On(hub, "wayfarer", WayfarerTalks);
        On(hub, "historian", HistorianTalks);
        On(hub, "gravekeeper", GravekeeperTalks);
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

    private static void HermitTalks(Entity e, GameEvent ev, EventHub hub) =>
        hub.Notify($"{e.Label}: Esta ciudad cayó hace siglos. Los cofres siguen en las casas más grandes, pero los aparecidos y los bandidos no los sueltan sin pelea.");

    private static void ArchaeologistTalks(Entity e, GameEvent ev, EventHub hub) =>
        hub.Notify($"{e.Label}: Este templo guarda un tesoro sobre el altar. Cuidado con los guardianes: de piedra no son, pero casi.");

    private static void EngineerTalks(Entity e, GameEvent ev, EventHub hub) =>
        hub.Notify($"{e.Label}: Las centinelas siguen de ronda desde que se apagó todo. Lo valioso estaba en el hangar.");

    private static void ReactorTechTalks(Entity e, GameEvent ev, EventHub hub) =>
        hub.Notify($"{e.Label}: Dieron la orden de apagarlo y nadie quedó para cumplirla. Aléjate de la cúpula: el aire todavía quema.");

    private static void ForemanTalks(Entity e, GameEvent ev, EventHub hub) =>
        hub.Notify($"{e.Label}: Esto era una refinería. Las bombas siguen en su caseta, y los saqueadores la vigilan.");

    private static void PilotTalks(Entity e, GameEvent ev, EventHub hub) =>
        hub.Notify($"{e.Label}: El último vuelo no llegó a despegar. Lo único de valor es lo del terminal y el hangar.");

    private static void AstronautTalks(Entity e, GameEvent ev, EventHub hub) =>
        hub.Notify($"{e.Label}: Esperé el cohete diez años. Si buscas algo útil, prueba el búnker de control.");

    private static void AstronomerTalks(Entity e, GameEvent ev, EventHub hub) =>
        hub.Notify($"{e.Label}: La Esfera marcaba las estaciones con la rendija de la cúpula. Lo que guardaba el observatorio sigue bajo ella, si pasas los espectros estelares.");

    private static void ArchivistTalks(Entity e, GameEvent ev, EventHub hub) =>
        hub.Notify($"{e.Label}: Los archivos hablaban de un Ojo que lo registraba todo. Ahora solo quedan polvo y bandidos en las salas.");

    private static void KeeperTalks(Entity e, GameEvent ev, EventHub hub) =>
        hub.Notify($"{e.Label}: El faro se apagó, pero sigo contando las noches. Los mojones del campo señalaban el camino a la costa.");

    private static void WayfarerTalks(Entity e, GameEvent ev, EventHub hub) =>
        hub.Notify($"{e.Label}: Subo la escalinata cada año a mirar los monolitos. Las ofrendas del camino son para quien las necesite.");

    private static void HistorianTalks(Entity e, GameEvent ev, EventHub hub) =>
        hub.Notify($"{e.Label}: Aquí reinaron treinta reyes, uno por estatua. Del último solo queda el coloso. Y su guardia aún no ha sido relevada.");

    private static void GravekeeperTalks(Entity e, GameEvent ev, EventHub hub) =>
        hub.Notify($"{e.Label}: Cada urna guarda un nombre. Si molestas a los muertos, los espectros de las urnas despiertan.");

    private static void GuardTalks(Entity e, GameEvent ev, EventHub hub) =>
        hub.Notify($"{e.Label}: " + (e.GetFloat("hp") < e.GetFloat("max_hp") ? "He tenido días mejores, pero sigo en mi puesto." : "Todo tranquilo. Vigilo la ciudad."));
}
