using GF.Core;
using GF.World.Events;
using GF.World.Items;

namespace GF.World.Entities;

/// <summary>
/// Fachada sencilla para entidades: tipos, variantes y objetos por NOMBRE, sin ids ni registros.
///     var api = new EntityApi(entities, events);
///     var orco = api.Spawn("ogre", x, y, z, variant: "warlord");
///     api.Give(orco, "health_potion", 2);
///     api.Equip(orco, "iron_sword");
///     foreach (var e in api.Nearby(centro, 20, "zombie")) api.Send(e, GameEvents.Fire, 3f);
/// Solo hilo principal. Posiciones en coordenadas del mundo (la X se envuelve sola). Los errores de nombre lanzan una excepción con el nombre pedido.
/// </summary>
public sealed class EntityApi
{
    private readonly List<Entity> _scratch = new();

    public EntityApi(EntityStore store, EventHub hub)
    {
        Store = store;
        Hub = hub;
    }

    public EntityStore Store { get; }
    public EventHub Hub { get; }

    private Registry<EntityDef> Types =>
        Store.Definitions ?? throw new InvalidOperationException("EntityStore.Definitions no está asignado.");

    private Registry<ItemDef> Items =>
        Store.Items ?? throw new InvalidOperationException("EntityStore.Items no está asignado.");

    // ------------------------------------------------------------------ crear y borrar

    /// <summary>Crea una entidad de un tipo (y variante opcional, por nombre). 'name' pone su propiedad "name" si el tipo la tiene.</summary>
    public Entity Spawn(string type, double x, double y, double z, string? name = null, string? variant = null, string? tag = null)
    {
        if (!Types.TryGetId(type, out ushort id)) throw new KeyNotFoundException($"No existe el tipo de entidad '{type}'.");
        var def = Types.Get(id);
        var e = Store.Spawn(id, new Vec3d(x, y, z), def.DefaultSpeed, tag, VariantIndex(def, variant));
        if (name != null && e.HasProperty("name")) e.Set("name", name);
        return e;
    }

    public Entity Spawn(string type, Vec3d pos, string? name = null, string? variant = null, string? tag = null) =>
        Spawn(type, pos.X, pos.Y, pos.Z, name, variant, tag);

    public void Remove(Entity e) => Store.Remove(e);

    private static int VariantIndex(EntityDef def, string? variant)
    {
        if (variant == null) return 0;
        var list = def.Variants;
        if (list != null)
            for (int i = 0; i < list.Count; i++)
                if (list[i].Name == variant) return i;
        throw new KeyNotFoundException($"El tipo '{def.Name}' no tiene la variante '{variant}'.");
    }

    // ------------------------------------------------------------------ consultas

    public bool Is(Entity e, string type) => e.Def?.Name == type;

    /// <summary>Entidades a menos de 'radius' bloques (con envoltura en X), opcionalmente solo de un tipo.</summary>
    public List<Entity> Nearby(Vec3d center, double radius, string? type = null)
    {
        var result = new List<Entity>();
        Store.QueryRadius(center, radius, result);
        if (type != null) result.RemoveAll(e => e.Def?.Name != type);
        return result;
    }

    /// <summary>La más cercana a 'center' dentro de 'radius' (null si no hay).</summary>
    public Entity? Nearest(Vec3d center, double radius, string? type = null)
    {
        _scratch.Clear();
        Store.QueryRadius(center, radius, _scratch);
        Entity? best = null;
        double bestD = double.MaxValue;
        foreach (var e in _scratch)
        {
            if (type != null && e.Def?.Name != type) continue;
            double dx = Store_DeltaX(center.X, e.Position.X), dy = e.Position.Y - center.Y, dz = e.Position.Z - center.Z;
            double d = dx * dx + dy * dy + dz * dz;
            if (d < bestD) { bestD = d; best = e; }
        }
        _scratch.Clear();
        return best;
    }

    // QueryRadius ya envuelve en X; para ordenar basta la diferencia directa salvo junto a la costura, donde la distancia relativa sigue siendo válida
    // porque 'center' y las posiciones canónicas difieren como mucho en medio mundo: se usa el camino corto a mano.
    private double Store_DeltaX(double from, double to)
    {
        double d = to - from;
        double half = 0.5 * WorldWidth;
        if (WorldWidth > 0) { if (d > half) d -= WorldWidth; else if (d < -half) d += WorldWidth; }
        return d;
    }

    /// <summary>Ancho del mundo en bloques para elegir el camino corto en X (0 = no envolver). Asígnalo con scale.WidthBlocks.</summary>
    public double WorldWidth { get; set; }

    public int Count(string? type = null) =>
        type == null ? Store.Count : Store.All.Count(e => e.Def?.Name == type);

    /// <summary>Todas las entidades de un tipo.</summary>
    public IEnumerable<Entity> All(string type) => Store.All.Where(e => e.Def?.Name == type);

    // ------------------------------------------------------------------ movimiento

    /// <summary>Teletransporta (cualquier nivel de simulación).</summary>
    public void Teleport(Entity e, Vec3d pos) => Store.Move(e, pos);

    /// <summary>Le da un destino: lo recorre en línea recta (aproximadas) o con la IA (activas).</summary>
    public void MoveTo(Entity e, Vec3d destination) => Store.SetDestination(e, destination);

    public void Stop(Entity e) => Store.SetDestination(e, null);

    // ------------------------------------------------------------------ eventos

    /// <summary>Envía un evento a una entidad (daño, fuego, conversación...). Lo procesa solo quien lo acepte.</summary>
    public DeliveryResult Send(Entity target, EventKind kind, float amount = 0f, object? source = null, string? text = null) =>
        Hub.Send(target, new GameEvent(kind, source, amount, text));

    /// <summary>Envía un evento a todas las entidades a menos de 'radius'. Devuelve cuántas lo procesaron.</summary>
    public int Broadcast(Vec3d center, double radius, EventKind kind, float amount = 0f, object? source = null) =>
        Hub.Broadcast(center, radius, new GameEvent(kind, source, amount), source as IEventReceiver);

    // ------------------------------------------------------------------ objetos

    /// <summary>Añade objetos al inventario. Devuelve cuántos NO cupieron (0 = todo dentro).</summary>
    public int Give(Entity e, string item, int count = 1) => e.Inventory.Add(Items, ItemId(item), count);

    /// <summary>Quita hasta 'count' objetos del inventario. Devuelve cuántos quitó.</summary>
    public int Take(Entity e, string item, int count = 1) => e.Inventory.Remove(ItemId(item), count);

    /// <summary>¿Lleva el objeto (en el inventario o equipado)?</summary>
    public bool Has(Entity e, string item) => e.Holds(ItemId(item));

    /// <summary>
    /// Equipa un objeto en una ranura libre de su tipo (o en la primera de ese tipo si todas están ocupadas), SIN gastarlo del inventario.
    /// false si el objeto no es equipable o la entidad no tiene ranura de ese tipo.
    /// </summary>
    public bool Equip(Entity e, string item)
    {
        ushort id = ItemId(item);
        if (Items.Get(id).Slot is not { } kind) return false;
        int slot = e.Equipment.FindSlot(kind);
        if (slot < 0) return false;
        e.Equipment.Set(slot, id);
        return true;
    }

    private ushort ItemId(string item) =>
        Items.TryGetId(item, out ushort id) ? id : throw new KeyNotFoundException($"No existe el objeto '{item}'.");
}
