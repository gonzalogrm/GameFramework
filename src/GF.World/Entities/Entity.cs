using GF.Core;
using GF.World.Events;
using GF.World.Items;

namespace GF.World.Entities;

/// <summary>
/// Dormant: no se simula (solo se guarda). Approximate: no hay chunk cargado, pero tiene destino y avanza en línea recta
/// con un reloj grueso. Active: su columna de chunks está cargada; el juego la simula con todo detalle.
/// </summary>
public enum SimulationTier { Dormant, Approximate, Active }

/// <summary>
/// Definición de un tipo de entidad. Name es la clave del registro y también se usa al guardar.
/// Prototype (opcional) define las propiedades del tipo y sus valores por defecto: las instancias solo guardan lo que cambian.
/// </summary>
public sealed record EntityDef(string Name, double DefaultSpeed, byte R, byte G, byte B,
    float Width = 0.6f, float Height = 1.8f, Prototype? Prototype = null, int InventorySize = 8,
    IReadOnlyList<EntityVariant>? Variants = null, SlotLayout? Slots = null, int SpawnWeight = 0);

/// <summary>Un cambio de propiedad de una entidad, para guardar. El tipo (entero, decimal...) lo da el prototipo al cargar.</summary>
public sealed record PropertyRecord(string Name, double Number, string? Text);

/// <summary>Entidad serializable (el estado de ejecución, Entity.Data, no se guarda).</summary>
public sealed record EntityRecord(long Id, string Type, double X, double Y, double Z,
    double? DestX, double? DestY, double? DestZ, double? Speed, string? Tag, List<PropertyRecord>? Props = null,
    List<StackRecord>? Items = null, List<EquipRecord>? Gear = null, string? Variant = null);

/// <summary>
/// Una INSTANCIA de entidad. Remite a su tipo (EntityDef y su Prototype) y solo guarda lo que ha cambiado:
/// 1000 perros comparten los valores por defecto del prototipo "perro"; el que pierde vida registra únicamente su "vida".
/// La posición solo cambia a través de EntityStore.Move (para mantener los índices espaciales coherentes);
/// las coordenadas son canónicas: X siempre en [0, ancho del mundo).
/// </summary>
public sealed class Entity : IEventReceiver, IInventoryHolder
{
    private PropertyOverrides? _overrides;
    private Inventory? _inventory;
    private Equipment? _equipment;

    internal Entity(long id, ushort typeId)
    {
        Id = id;
        TypeId = typeId;
    }

    public long Id { get; }
    public ushort TypeId { get; }
    public Vec3d Position { get; internal set; }
    public Vec3d? Destination { get; internal set; }
    public double Speed { get; set; }
    public SimulationTier Tier { get; internal set; }

    /// <summary>Dato pequeño serializable definido por el juego (profesión, facción...).</summary>
    public string? Tag { get; set; }
    /// <summary>Estado de ejecución del juego (IA, física...). No se guarda; se crea en Activated y se descarta en Deactivated.</summary>
    public object? Data { get; set; }

    internal double LastSim;
    internal ChunkCoord Column;     // columna de chunks (X canónica, Y = 0, Z)
    internal (int X, int Z) Region;

    // ------------------------------------------------------------------ tipo y propiedades

    /// <summary>Tipo de esta instancia (asignado por EntityStore.Definitions). Compartido por todas las instancias del mismo tipo.</summary>
    public EntityDef? Def { get; internal set; }
    /// <summary>
    /// Variante (índice en EntityDef.Variants; 0 = la primera/por defecto). El prototipo efectivo es el de la variante, hijo del del tipo, así que
    /// una instancia solo guarda QUÉ variante es y sus cambios respecto a ella.
    /// </summary>
    public int Variant { get; internal set; }

    public Prototype? Prototype
    {
        get
        {
            var variants = Def?.Variants;
            return variants != null && (uint)Variant < (uint)variants.Count ? variants[Variant].Prototype : Def?.Prototype;
        }
    }

    /// <summary>Nombre para mensajes: "especie nombre" (p. ej. "Aldeano Marta"), o solo la especie, o el nombre del tipo.</summary>
    public string Label
    {
        get
        {
            string name = HasProperty("name") ? GetText("name") : "";
            string species = HasProperty("species") ? GetText("species") : Def?.Name ?? "entidad";
            return name.Length > 0 ? $"{species} {name}" : species;
        }
    }

    public bool HasProperty(string name) => Prototype is { } p && p.TryGetDef(PropertyIds.Of(name), out _);

    /// <summary>Cuántas propiedades de esta instancia difieren de su prototipo (y por tanto ocupan memoria).</summary>
    public int OverrideCount => _overrides?.Count ?? 0;

    public PropValue Get(string name) => PropertyOps.Get(Require(), _overrides, PropertyIds.Of(name));
    public int GetInt(string name) => Get(name).AsInt;
    public float GetFloat(string name) => Get(name).AsFloat;
    public bool GetBool(string name) => Get(name).AsBool;
    public string GetText(string name) => Get(name).AsText;

    public void Set(string name, int value) => Set(name, PropValue.Of(value));
    public void Set(string name, float value) => Set(name, PropValue.Of(value));
    public void Set(string name, bool value) => Set(name, PropValue.Of(value));
    public void Set(string name, string value) => Set(name, PropValue.Of(value));

    private void Set(string name, PropValue value) => PropertyOps.Set(Require(), ref _overrides, PropertyIds.Of(name), value);

    /// <summary>Devuelve la propiedad al valor de su prototipo (y libera lo que ocupaba).</summary>
    public void ResetProperty(string name) => PropertyOps.Reset(Require(), ref _overrides, PropertyIds.Of(name));

    /// <summary>Todas las propiedades con su valor actual, el del prototipo y si esta instancia lo ha cambiado.</summary>
    public IEnumerable<PropertyView> Properties =>
        Prototype is { } p ? PropertyOps.Enumerate(p, _overrides) : Enumerable.Empty<PropertyView>();

    // ------------------------------------------------------------------ inventario y equipamiento

    /// <summary>Inventario de esta entidad (todas tienen uno; se crea al usarlo, así que las que no llevan nada no gastan memoria).</summary>
    public Inventory Inventory => _inventory ??= NewInventory();
    /// <summary>Lo que lleva puesto o en la mano.</summary>
    public Equipment Equipment => _equipment ??= NewEquipment();

    private LoadoutTemplate? Loadout =>
        Def?.Variants is { } v && (uint)Variant < (uint)v.Count ? v[Variant].Loadout : null;

    // Al usarlos por primera vez se rellenan con lo que define su variante (la plantilla es compartida; esto es una copia propia).
    private Inventory NewInventory()
    {
        var inventory = new Inventory(Def?.InventorySize ?? 8);
        Loadout?.FillInventory(inventory);
        return inventory;
    }

    private Equipment NewEquipment()
    {
        var equipment = new Equipment(Def?.Slots ?? SlotLayout.Default);   // las ranuras las define el tipo (un enemigo de 3 brazos tiene 3 de mano)
        Loadout?.FillEquipment(equipment);
        return equipment;
    }

    /// <summary>
    /// Solo si el inventario DIFIERE de la plantilla de su variante (si no, ya está en el prototipo y no se guarda nada). Si difiere se guarda
    /// entero, y una lista vacía significa "se vació". null = sin cambios.
    /// </summary>
    // ------------------------------------------------------------------ consultas sin crear el inventario

    /// <summary>Cuántas ranuras de equipamiento tiene, sin crear el equipamiento si aún es el de la plantilla.</summary>
    public int GearSlotCount => _equipment?.SlotCount ?? Loadout?.GearLength ?? 0;

    /// <summary>Objeto equipado en una ranura (desde la plantilla si la instancia no ha tocado su equipo). null si está vacía.</summary>
    public ushort? GearAt(int slot) => _equipment != null ? _equipment.Get(slot) : Loadout?.Gear(slot);

    /// <summary>¿Lleva este objeto, en el inventario o equipado? Mira la plantilla si la instancia no ha tocado su inventario o su equipo (no los crea).</summary>
    public bool Holds(ushort item)
    {
        for (int s = 0; s < GearSlotCount; s++) if (GearAt(s) == item) return true;
        return _inventory != null ? _inventory.CountOf(item) > 0 : (Loadout?.CountOf(item) ?? 0) > 0;
    }

    internal List<StackRecord>? ExportItems(Registry<ItemDef>? items)
    {
        if (items == null || _inventory == null) return null;
        var template = Loadout;
        if (template != null ? template.SameItems(_inventory) : _inventory.IsEmpty) return null;
        return _inventory.Export(items) ?? new List<StackRecord>();
    }

    internal List<EquipRecord>? ExportGear(Registry<ItemDef>? items)
    {
        if (items == null || _equipment == null) return null;
        var template = Loadout;
        if (template != null ? template.SameGear(_equipment) : _equipment.IsEmpty) return null;
        return _equipment.Export(items) ?? new List<EquipRecord>();
    }

    internal void ImportItems(List<StackRecord>? stacks, List<EquipRecord>? gear, Registry<ItemDef>? items)
    {
        if (items == null) return;
        if (stacks != null) Inventory.Import(stacks, items);
        if (gear != null) Equipment.Import(gear, items);
    }

    internal IEnumerable<(PropertyId Id, PropValue Value)> RawOverrides =>
        _overrides?.Entries ?? Enumerable.Empty<(PropertyId, PropValue)>();

    /// <summary>Aplica un cambio guardado. Si el tipo ya no define esa propiedad, se ignora.</summary>
    internal void ImportProperty(PropertyRecord record)
    {
        var prototype = Prototype;
        if (prototype == null) return;
        var id = PropertyIds.Of(record.Name);
        if (!prototype.TryGetDef(id, out var def)) return;

        var value = def.Kind switch
        {
            PropKind.Int => PropValue.Of((int)Math.Round(record.Number)),
            PropKind.Float => PropValue.Of((float)record.Number),
            PropKind.Bool => PropValue.Of(record.Number != 0),
            _ => PropValue.Of(record.Text ?? ""),
        };
        PropertyOps.Set(prototype, ref _overrides, id, value);
    }

    private Prototype Require() => Prototype ?? throw new InvalidOperationException(
        $"La entidad #{Id} no tiene prototipo: asigna EntityStore.Definitions con definiciones que lo incluyan.");
}
