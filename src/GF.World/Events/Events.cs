using GF.Core;
using GF.World.Entities;

namespace GF.World.Events;

/// <summary>
/// Tipo de evento. Forman una jerarquía: "bola de fuego" es un tipo de "fuego", que es un tipo de "daño". Quien acepta "daño" procesa
/// cualquiera de sus derivados; quien solo acepta "fuego" no procesa un daño genérico. Se compara por identidad (créalos una vez).
/// </summary>
public sealed class EventKind
{
    public EventKind(string name, EventKind? parent = null)
    {
        Name = name;
        Parent = parent;
    }

    public string Name { get; }
    public EventKind? Parent { get; }

    public EventKind Derive(string name) => new(name, this);

    /// <summary>¿Es este tipo, o desciende de él?</summary>
    public bool Is(EventKind other)
    {
        for (var k = this; k != null; k = k.Parent)
            if (ReferenceEquals(k, other)) return true;
        return false;
    }

    public override string ToString() => Name;
}

/// <summary>
/// Un evento: de qué tipo es, quién lo emite (si alguien), y una carga numérica y de texto opcionales. Es inmutable;
/// 'ev with { Amount = 2 }' crea una variante. Se puede heredar para añadir más datos.
/// </summary>
public record GameEvent(EventKind Kind, object? Source = null, float Amount = 0f, string? Text = null);

/// <summary>
/// Algo que puede recibir eventos: una entidad, un bloque del mundo... Qué eventos procesa lo decide su PROTOTIPO, no la instancia:
/// mil perros comparten una sola lista de eventos aceptados.
/// </summary>
public interface IEventReceiver
{
    Prototype? Prototype { get; }
    /// <summary>Nombre para mensajes y registros.</summary>
    string Label { get; }
}

public enum DeliveryResult
{
    /// <summary>Lo recibió, estaba en su lista de eventos aceptados y lo procesó.</summary>
    Processed,
    /// <summary>Lo recibió pero no es uno de los que acepta: no hace nada.</summary>
    Ignored,
}

public readonly record struct Delivery(IEventReceiver Target, GameEvent Event, DeliveryResult Result);

/// <summary>
/// Reparte eventos. Tiene la tabla "prototipo + tipo de evento -> manejador":
///     hub.On&lt;Entity&gt;(perro, Conversacion, (perro, ev, hub) => ...);   // los perros procesan la conversación
/// y entrega los eventos a un receptor concreto (Send) o a todas las entidades de una zona (Broadcast, con el índice espacial).
/// Un receptor sin manejador para ese tipo lo recibe pero no lo procesa. Los manejadores se heredan por prototipo (el hijo puede
/// sustituirlos) y por tipo de evento (gana el más específico). Solo hilo principal.
/// </summary>
public sealed class EventHub
{
    /// <summary>Profundidad máxima de eventos que provocan otros eventos; evita cascadas infinitas.</summary>
    public const int MaxDepth = 8;

    private readonly Dictionary<Prototype, Dictionary<EventKind, Action<object, GameEvent, EventHub>>> _handlers = new();
    private int _depth;

    public EventHub(EntityStore entities) => Entities = entities;

    public EntityStore Entities { get; }

    /// <summary>Se lanza tras cada entrega, procesada o ignorada (útil para registros y depuración).</summary>
    public event Action<Delivery>? Delivered;

    /// <summary>Mensajes para el jugador que emiten los manejadores (Notify). La interfaz decide cómo mostrarlos.</summary>
    public event Action<string>? Message;

    public void Notify(string text) => Message?.Invoke(text);

    // ------------------------------------------------------------------ registro

    /// <summary>El prototipo (y los que heredan de él) acepta este tipo de evento, y los derivados, y lo procesa con 'handler'.</summary>
    public void On<TReceiver>(Prototype prototype, EventKind kind, Action<TReceiver, GameEvent, EventHub> handler)
        where TReceiver : IEventReceiver
    {
        if (!_handlers.TryGetValue(prototype, out var table))
            _handlers[prototype] = table = new Dictionary<EventKind, Action<object, GameEvent, EventHub>>();
        table[kind] = (receiver, ev, hub) =>
        {
            if (receiver is TReceiver typed) handler(typed, ev, hub);
        };
    }

    /// <summary>
    /// El manejador que corresponde: se recorre el prototipo y sus ancestros (el más específico primero) y, en cada uno, el tipo de
    /// evento y sus ancestros (el más específico primero). null = no lo acepta.
    /// </summary>
    private Action<object, GameEvent, EventHub>? FindHandler(Prototype? prototype, EventKind kind)
    {
        for (var p = prototype; p != null; p = p.Parent)
        {
            if (!_handlers.TryGetValue(p, out var table)) continue;
            for (var k = kind; k != null; k = k.Parent)
                if (table.TryGetValue(k, out var handler)) return handler;
        }
        return null;
    }

    public bool Accepts(IEventReceiver receiver, EventKind kind) => FindHandler(receiver.Prototype, kind) != null;

    /// <summary>Los tipos de evento que un prototipo acepta (incluidos los heredados), del más cercano al más lejano.</summary>
    public List<EventKind> AcceptedKinds(Prototype? prototype)
    {
        var result = new List<EventKind>();
        for (var p = prototype; p != null; p = p.Parent)
            if (_handlers.TryGetValue(p, out var table))
                foreach (var kind in table.Keys)
                    if (!result.Contains(kind)) result.Add(kind);
        return result;
    }

    // ------------------------------------------------------------------ entrega

    /// <summary>Entrega el evento a un receptor. Si no está en su lista de aceptados, lo recibe pero no hace nada.</summary>
    public DeliveryResult Send(IEventReceiver target, GameEvent ev)
    {
        var handler = FindHandler(target.Prototype, ev.Kind);
        if (handler == null || _depth >= MaxDepth)
        {
            Delivered?.Invoke(new Delivery(target, ev, DeliveryResult.Ignored));
            return DeliveryResult.Ignored;
        }

        _depth++;
        try { handler(target, ev, this); }
        finally { _depth--; }

        Delivered?.Invoke(new Delivery(target, ev, DeliveryResult.Processed));
        return DeliveryResult.Processed;
    }

    /// <summary>
    /// Entrega el evento a TODAS las entidades a menos de 'radius' del centro (consulta por el índice espacial; funciona con el mundo
    /// cilíndrico). Devuelve cuántas lo procesaron. Los manejadores pueden eliminar entidades sin romper el reparto.
    /// </summary>
    public int Broadcast(Vec3d center, double radius, GameEvent ev, IEventReceiver? exclude = null)
    {
        var found = new List<Entity>();
        Entities.QueryRadius(center, radius, found);

        int processed = 0;
        foreach (var entity in found)
        {
            if (ReferenceEquals(entity, exclude)) continue;
            if (!Entities.Contains(entity)) continue;   // un manejador anterior la eliminó
            if (Send(entity, ev) == DeliveryResult.Processed) processed++;
        }
        return processed;
    }

    /// <summary>Una entidad EMITE un evento que oyen las demás en un radio (ella no). El evento lleva a la entidad como Source.</summary>
    public int Emit(Entity source, GameEvent ev, double radius) =>
        Broadcast(source.Position, radius, ev with { Source = source }, exclude: source);
}
