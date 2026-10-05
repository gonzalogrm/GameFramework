using GF.Core;

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
    float Width = 0.6f, float Height = 1.8f, Prototype? Prototype = null);

/// <summary>Un cambio de propiedad de una entidad, para guardar. El tipo (entero, decimal...) lo da el prototipo al cargar.</summary>
public sealed record PropertyRecord(string Name, double Number, string? Text);

/// <summary>Entidad serializable (el estado de ejecución, Entity.Data, no se guarda).</summary>
public sealed record EntityRecord(long Id, string Type, double X, double Y, double Z,
    double? DestX, double? DestY, double? DestZ, double Speed, string? Tag, List<PropertyRecord>? Props = null);

/// <summary>
/// Una INSTANCIA de entidad. Remite a su tipo (EntityDef y su Prototype) y solo guarda lo que ha cambiado:
/// 1000 perros comparten los valores por defecto del prototipo "perro"; el que pierde vida registra únicamente su "vida".
/// La posición solo cambia a través de EntityStore.Move (para mantener los índices espaciales coherentes);
/// las coordenadas son canónicas: X siempre en [0, ancho del mundo).
/// </summary>
public sealed class Entity
{
    private PropertyOverrides? _overrides;

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
    public Prototype? Prototype => Def?.Prototype;

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
