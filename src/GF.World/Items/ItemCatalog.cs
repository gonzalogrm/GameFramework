using GF.Core;

namespace GF.World.Items;

/// <summary>Una propiedad de un objeto de runtime, para guardar: nombre, tipo y valor.</summary>
public sealed record ItemPropRecord(string Name, string Kind, double Number, string? Text);

/// <summary>
/// Un objeto creado durante la partida, tal como se guarda: SOLO su nombre, el objeto del que deriva y lo que cambia respecto a él (título, propiedades,
/// padres extra y componentes). Todo lo demás lo da el objeto base, que está en el JSON.
/// </summary>
public sealed record RuntimeItemRecord(string Name, string Base, List<string>? ExtraParents = null, string? Title = null,
    List<ItemPropRecord>? Overrides = null, List<string>? Components = null);

/// <summary>
/// Todos los prototipos de objeto y el registro de los que son objetos (con ID numérico). Los objetos se definen en JSON (ItemDefinitionLoader) y
/// además se pueden CREAR EN PARTIDA: CreateRuntime("iron_sword", "sword_of_morgrim", ...) hace un prototipo nuevo (hijo de cualquier objeto que no sea
/// abstracto) y lo registra con un ID nuevo. La variante puede cambiar propiedades existentes, añadir propiedades nuevas, heredar de más prototipos y
/// añadir COMPONENTES (comportamientos: eventos que recibe, efectos por tick...). Los objetos de runtime se guardan con el mundo (ExportRuntime, solo el
/// delta) y se recrean al cargarlo (ImportRuntime), antes que las entidades y los inventarios, que los referencian por NOMBRE.
/// Los IDs de los objetos del JSON no cambian entre mundos (Fork conserva su orden); los de runtime se añaden detrás.
/// </summary>
public sealed class ItemCatalog
{
    private readonly Dictionary<string, Prototype> _prototypes;
    private readonly HashSet<string> _abstract;
    private readonly List<(string Name, ItemDef Def)> _baseItems = new();
    private readonly List<RuntimeItemRecord> _runtime = new();

    internal ItemCatalog(Dictionary<string, Prototype> prototypes, HashSet<string> abstractNames)
    {
        _prototypes = prototypes;
        _abstract = abstractNames;
        Registry = new Registry<ItemDef>();
    }

    private ItemCatalog(ItemCatalog source)
    {
        _prototypes = new Dictionary<string, Prototype>(source._prototypes);   // copia: los prototipos de runtime se añaden aquí, no al original
        _abstract = source._abstract;
        _baseItems.AddRange(source._baseItems);
        Registry = new Registry<ItemDef>();
        foreach (var (name, def) in _baseItems) Registry.Register(name, def);
    }

    /// <summary>Los objetos (con ID): primero los del JSON, en su orden, y detrás los creados en partida.</summary>
    public Registry<ItemDef> Registry { get; }

    internal void RegisterBase(string name, ItemDef def)
    {
        Registry.Register(name, def);
        _baseItems.Add((name, def));
    }

    /// <summary>Un prototipo cualquiera (abstracto u objeto) por nombre. Lanza KeyNotFoundException si no existe.</summary>
    public Prototype GetPrototype(string name) =>
        _prototypes.TryGetValue(name, out var p) ? p : throw new KeyNotFoundException($"No hay un prototipo de objeto '{name}'.");

    public bool IsAbstract(string name) => _abstract.Contains(name);

    /// <summary>Una copia con los mismos objetos del JSON y sin objetos de runtime: lo que se usa al abrir un mundo (cada mundo tiene los suyos).</summary>
    public ItemCatalog Fork() => new(this);

    // ------------------------------------------------------------------ runtime

    public IReadOnlyList<RuntimeItemRecord> RuntimeItems => _runtime;

    /// <summary>
    /// Crea un objeto nuevo en tiempo de ejecución como variante de CUALQUIER objeto no abstracto: hereda de él (y de 'extraParents', que se añaden DESPUÉS y
    /// por tanto mandan sobre él, y pueden ser abstractos: mezclas), con título propio, propiedades cambiadas o nuevas ('overrides') y componentes añadidos.
    /// Si ya existe un objeto de runtime con ese nombre, lo devuelve (así crearlo dos veces es seguro).
    /// </summary>
    /// <param name="baseItem">Nombre de un objeto (del JSON o de runtime) que no sea abstracto.</param>
    /// <param name="name">Clave única del objeto nuevo (es lo que se guarda en inventarios y entidades).</param>
    /// <param name="components">Componentes que se le añaden (los hereda cualquier variante posterior).</param>
    public ItemDef CreateRuntime(string baseItem, string name, string? title = null, IReadOnlyDictionary<string, PropValue>? overrides = null,
        IEnumerable<string>? extraParents = null, IEnumerable<string>? components = null)
    {
        if (Registry.TryGetId(name, out var existing))
        {
            var def = Registry.Get(existing);
            if (def.IsRuntime) return def;
            throw new InvalidOperationException($"'{name}' ya es un objeto definido en el JSON: elige otro nombre.");
        }
        if (!_prototypes.TryGetValue(baseItem, out var baseProto)) throw new InvalidOperationException($"No hay un objeto '{baseItem}' del que derivar.");
        if (_abstract.Contains(baseItem)) throw new InvalidOperationException($"'{baseItem}' es abstracto: solo sirve de base, no se puede crear una variante.");

        var parents = new List<Prototype> { baseProto };
        var extra = extraParents?.ToList();
        if (extra != null) foreach (var e in extra) parents.Add(GetPrototype(e));

        var proto = new Prototype(name, parents);
        if (title != null) proto.Text("title", title);
        List<ItemPropRecord>? saved = null;
        if (overrides != null)
            foreach (var (prop, value) in overrides)
            {
                proto.Define(prop, value);
                (saved ??= new List<ItemPropRecord>()).Add(new ItemPropRecord(prop, value.Kind.ToString(), value.Number, value.Text));
            }
        var comps = components?.Select(c => c.Trim().ToLowerInvariant()).Distinct().ToList();
        if (comps != null) foreach (var c in comps) proto.AddComponent(c);

        var item = new ItemDef(name, proto, isRuntime: true);
        Registry.Register(name, item);
        _prototypes[name] = proto;
        _runtime.Add(new RuntimeItemRecord(name, baseItem, extra is { Count: > 0 } ? extra : null, title, saved, comps is { Count: > 0 } ? comps : null));
        return item;
    }

    /// <summary>Los objetos de runtime, para guardar con el mundo. Lista vacía si no hay.</summary>
    public List<RuntimeItemRecord> ExportRuntime() => _runtime.ToList();

    /// <summary>
    /// Solo los objetos de runtime con estos nombres, más los de runtime de los que dependen (su objeto base o sus padres extra), en orden de creación (las bases
    /// primero). Es lo que hay que llevarse para que un objeto de este mundo siga existiendo en otro: el jugador los guarda con su inventario.
    /// </summary>
    public List<RuntimeItemRecord> ExportRuntimeFor(IEnumerable<string> names)
    {
        var runtimeNames = new HashSet<string>(_runtime.Select(r => r.Name), StringComparer.Ordinal);
        var wanted = new HashSet<string>(names.Where(runtimeNames.Contains), StringComparer.Ordinal);
        bool grew;
        do
        {
            grew = false;
            foreach (var r in _runtime)
            {
                if (!wanted.Contains(r.Name)) continue;
                if (runtimeNames.Contains(r.Base) && wanted.Add(r.Base)) grew = true;
                if (r.ExtraParents != null) foreach (var p in r.ExtraParents) if (runtimeNames.Contains(p) && wanted.Add(p)) grew = true;
            }
        } while (grew);
        return _runtime.Where(r => wanted.Contains(r.Name)).ToList();
    }

    /// <summary>Recrea los objetos de runtime de un mundo guardado (en el mismo orden). Los que fallan se ignoran y se devuelven como avisos.</summary>
    public List<string> ImportRuntime(IEnumerable<RuntimeItemRecord>? records)
    {
        var problems = new List<string>();
        if (records == null) return problems;
        foreach (var r in records)
        {
            try
            {
                Dictionary<string, PropValue>? overrides = null;
                if (r.Overrides != null)
                {
                    overrides = new Dictionary<string, PropValue>();
                    foreach (var o in r.Overrides)
                        if (Enum.TryParse<PropKind>(o.Kind, out var kind)) overrides[o.Name] = new PropValue(kind, o.Number, o.Text);
                }
                CreateRuntime(r.Base, r.Name, r.Title, overrides, r.ExtraParents, r.Components);
            }
            catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or ArgumentNullException)
            {
                problems.Add($"Objeto de runtime '{r.Name}': {e.Message}");
            }
        }
        return problems;
    }
}
