using System.Collections.Concurrent;
using System.Globalization;

namespace GF.Core;

// =============================================================================================================
// Modelo prototipo / instancia
//
//   Prototype            define las propiedades de un TIPO y sus valores por defecto (una sola vez, en memoria compartida).
//                        Puede heredar de otro: "perro" -> "criatura".
//   PropertyOverrides    es lo único que guarda una INSTANCIA: solo las propiedades cuyo valor se ha cambiado.
//                        Una instancia sin cambios no reserva nada (el campo es null).
//   PropertyOps          lee (instancia, o prototipo si no hay cambio) y escribe (si el valor vuelve al por defecto, se borra).
//
// Lo usan tanto las entidades como los bloques del mundo.
// =============================================================================================================

public enum PropKind : byte { Int, Float, Bool, Text }

/// <summary>Valor de una propiedad. Los números (enteros, decimales, booleanos) viajan en Number; el texto, en Text.</summary>
public readonly record struct PropValue(PropKind Kind, double Number, string? Text)
{
    public static PropValue Of(int v) => new(PropKind.Int, v, null);
    public static PropValue Of(float v) => new(PropKind.Float, v, null);
    public static PropValue Of(double v) => new(PropKind.Float, v, null);
    public static PropValue Of(bool v) => new(PropKind.Bool, v ? 1 : 0, null);
    public static PropValue Of(string v) => new(PropKind.Text, 0, v);

    public int AsInt => (int)Math.Round(Number);
    public float AsFloat => (float)Number;
    public bool AsBool => Number != 0;
    public string AsText => Text ?? "";

    public override string ToString() => Kind switch
    {
        PropKind.Int => ((long)Math.Round(Number)).ToString(CultureInfo.InvariantCulture),
        PropKind.Float => Number.ToString("0.##", CultureInfo.InvariantCulture),
        PropKind.Bool => AsBool ? "si" : "no",
        _ => "\"" + AsText + "\"",
    };
}

/// <summary>Identificador numérico (2 bytes) de un nombre de propiedad. No es estable entre ejecuciones: al guardar se usa el nombre.</summary>
public readonly record struct PropertyId(ushort Value);

/// <summary>Tabla global de nombres de propiedad. Segura desde varios hilos.</summary>
public static class PropertyIds
{
    private static readonly object Gate = new();
    private static readonly ConcurrentDictionary<string, ushort> ByName = new(StringComparer.Ordinal);
    private static string[] _names = Array.Empty<string>();

    public static PropertyId Of(string name)
    {
        if (ByName.TryGetValue(name, out var existing)) return new PropertyId(existing);
        lock (Gate)
        {
            if (ByName.TryGetValue(name, out existing)) return new PropertyId(existing);
            var names = _names;
            if (names.Length >= ushort.MaxValue) throw new InvalidOperationException("Demasiados nombres de propiedad.");
            var id = (ushort)names.Length;
            var grown = new string[names.Length + 1];
            Array.Copy(names, grown, names.Length);
            grown[^1] = name;
            Volatile.Write(ref _names, grown);   // se publica el nombre antes que el id
            ByName[name] = id;
            return new PropertyId(id);
        }
    }

    public static string NameOf(PropertyId id) => Volatile.Read(ref _names)[id.Value];
}

public sealed record PropDef(PropertyId Id, PropKind Kind, PropValue Default);

/// <summary>Una propiedad tal como la ve el inspector.</summary>
public readonly record struct PropertyView(string Name, PropKind Kind, PropValue Value, PropValue Default, bool Overridden);

/// <summary>
/// Definición de un tipo: propiedades y valores por defecto. Se construye una vez al arrancar y después es de solo lectura.
/// HERENCIA MÚLTIPLE: un prototipo puede heredar de varios; el ORDEN importa y el ÚLTIMO gana. En  knife = [item, weapon]  una propiedad que tengan
/// los dos sale del arma; las propias del hijo mandan sobre todas. La resolución es la linealización C3 (la de Python) con los padres en orden
/// inverso: en una herencia en diamante el ancestro común queda siempre por detrás de los dos caminos.
///     var criatura = new Prototype("criatura").Text("nombre", "").Float("vida", 10f).Float("vida_max", 10f);
///     var perro = criatura.Derive("perro").Text("especie", "Perro").Float("vida", 12f);   // hereda y cambia un valor
/// </summary>
public sealed class Prototype
{
    private readonly Dictionary<ushort, PropDef> _defs = new();

    private readonly Prototype[] _parents;
    private readonly Prototype[] _lineage;
    private HashSet<string>? _components;

    public string Name { get; }

    /// <summary>Los padres, en el orden en que se declararon (el último tiene más prioridad).</summary>
    public IReadOnlyList<Prototype> Parents => _parents;

    /// <summary>El padre de más prioridad (el último declarado): con herencia simple, el único padre. null si no hereda.</summary>
    public Prototype? Parent => _parents.Length > 0 ? _parents[^1] : null;

    /// <summary>Orden de resolución: este prototipo y luego sus ancestros por prioridad decreciente. La primera definición de una propiedad en esta lista es la que vale.</summary>
    public IReadOnlyList<Prototype> Lineage => _lineage;

    public Prototype(string name, Prototype? parent = null) : this(name, parent == null ? Array.Empty<Prototype>() : new[] { parent }) { }

    /// <param name="parents">Padres en orden: si dos tienen la misma propiedad, vale la del ÚLTIMO.</param>
    public Prototype(string name, IEnumerable<Prototype> parents)
    {
        Name = name;
        _parents = parents.Distinct().ToArray();   // un padre repetido cuenta una vez (por la última posición)
        _lineage = Linearize(this, _parents);
        // Una misma propiedad no puede tener tipos distintos en prototipos de la herencia.
        var kinds = new Dictionary<ushort, PropKind>();
        foreach (var p in _lineage)
            foreach (var def in p._defs.Values)
                if (kinds.TryGetValue(def.Id.Value, out var kind) && kind != def.Kind)
                    throw new InvalidOperationException($"'{name}': la propiedad '{PropertyIds.NameOf(def.Id)}' es {kind} en un ancestro y {def.Kind} en otro.");
                else kinds[def.Id.Value] = def.Kind;
    }

    public Prototype Derive(string name) => new(name, this);

    // ------------------------------------------------------------------ componentes

    /// <summary>
    /// Añade un COMPONENTE: una etiqueta con nombre que el juego asocia a un comportamiento (eventos que recibe, IA, efectos por tick...). Se hereda: un
    /// hijo tiene los de todos sus ancestros. Se puede añadir también a prototipos creados en tiempo de ejecución.
    /// </summary>
    public Prototype AddComponent(string name)
    {
        (_components ??= new HashSet<string>(StringComparer.Ordinal)).Add(name.Trim().ToLowerInvariant());
        return this;
    }

    public bool HasComponent(string name)
    {
        name = name.Trim().ToLowerInvariant();
        foreach (var p in _lineage) if (p._components != null && p._components.Contains(name)) return true;
        return false;
    }

    /// <summary>Todos los componentes (propios y heredados), sin repetir.</summary>
    public IReadOnlyList<string> AllComponents()
    {
        var list = new List<string>();
        foreach (var p in _lineage)
            if (p._components != null)
                foreach (var c in p._components) if (!list.Contains(c)) list.Add(c);
        return list;
    }

    /// <summary>Linealización C3 con los padres en orden inverso (el último primero): el prototipo y sus ancestros por prioridad.</summary>
    private static Prototype[] Linearize(Prototype self, Prototype[] parents)
    {
		var ordered = Enumerable.Reverse(parents).ToList();  // el de más prioridad primero
		var sequences = ordered.Select(p => p._lineage.ToList()).ToList();
        sequences.Add(ordered);
        var result = new List<Prototype> { self };
        while (true)
        {
            sequences.RemoveAll(s => s.Count == 0);
            if (sequences.Count == 0) return result.ToArray();

            Prototype? next = null;
            foreach (var seq in sequences)
            {
                var head = seq[0];
                if (sequences.Any(other => other.IndexOf(head) > 0)) continue;   // sigue siendo ancestro de otro que aún no se ha colocado
                next = head;
                break;
            }
            if (next == null)
                throw new InvalidOperationException($"'{self.Name}': el orden de herencia es inconsistente (los padres ponen a sus ancestros en órdenes contradictorios).");
            result.Add(next);
            foreach (var seq in sequences) if (seq.Count > 0 && ReferenceEquals(seq[0], next)) seq.RemoveAt(0);
        }
    }

    /// <summary>"perro > criatura": el tipo y sus ancestros.</summary>
    public string ChainName
    {
        get
        {
            return string.Join(" > ", _lineage.Select(p => p.Name));
        }
    }

    public bool Is(Prototype other)
    {
        foreach (var p in _lineage) if (ReferenceEquals(p, other)) return true;
        return false;
    }

    public Prototype Define(string name, PropValue value)
    {
        var id = PropertyIds.Of(name);
        if (TryGetDef(id, out var inherited) && inherited.Kind != value.Kind)
            throw new InvalidOperationException($"'{Name}.{name}' ya existe como {inherited.Kind} en un ancestro.");
        _defs[id.Value] = new PropDef(id, value.Kind, value);
        return this;
    }

    public Prototype Int(string name, int value) => Define(name, PropValue.Of(value));
    public Prototype Float(string name, float value) => Define(name, PropValue.Of(value));
    public Prototype Bool(string name, bool value) => Define(name, PropValue.Of(value));
    public Prototype Text(string name, string value) => Define(name, PropValue.Of(value));

    /// <summary>Busca la definición en este tipo y, si no está, en sus ancestros.</summary>
    public bool TryGetDef(PropertyId id, out PropDef def)
    {
        foreach (var p in _lineage)   // por prioridad: la propia, luego la del último padre...
            if (p._defs.TryGetValue(id.Value, out def!)) return true;
        def = null!;
        return false;
    }

    /// <summary>Todas las propiedades efectivas: primero las del ancestro más lejano; un hijo que redefine un valor lo sustituye en su sitio.</summary>
    public List<PropDef> AllDefs()
    {
        var chain = new List<Prototype>(_lineage);
        chain.Reverse();   // de menor a mayor prioridad: la que vale al final sustituye a las anteriores

        var result = new List<PropDef>();
        var index = new Dictionary<ushort, int>();
        foreach (var p in chain)
            foreach (var def in p._defs.Values)
            {
                if (index.TryGetValue(def.Id.Value, out int i)) result[i] = def;
                else { index[def.Id.Value] = result.Count; result.Add(def); }
            }
        return result;
    }
}

/// <summary>
/// Lo único que guarda una instancia: las propiedades que se han salido del prototipo. Son arrays minúsculos de tamaño exacto
/// (una instancia suele tener 0, 1 o 2 cambios), sin tablas hash.
/// </summary>
public sealed class PropertyOverrides
{
    private ushort[] _ids = Array.Empty<ushort>();
    private PropValue[] _values = Array.Empty<PropValue>();

    public int Count => _ids.Length;

    public bool TryGet(PropertyId id, out PropValue value)
    {
        int i = Array.IndexOf(_ids, id.Value);
        if (i < 0) { value = default; return false; }
        value = _values[i];
        return true;
    }

    public void Set(PropertyId id, PropValue value)
    {
        int i = Array.IndexOf(_ids, id.Value);
        if (i >= 0) { _values[i] = value; return; }
        Array.Resize(ref _ids, _ids.Length + 1);
        Array.Resize(ref _values, _values.Length + 1);
        _ids[^1] = id.Value;
        _values[^1] = value;
    }

    public bool Remove(PropertyId id)
    {
        int i = Array.IndexOf(_ids, id.Value);
        if (i < 0) return false;
        var ids = new ushort[_ids.Length - 1];
        var values = new PropValue[_values.Length - 1];
        Array.Copy(_ids, 0, ids, 0, i);
        Array.Copy(_ids, i + 1, ids, i, ids.Length - i);
        Array.Copy(_values, 0, values, 0, i);
        Array.Copy(_values, i + 1, values, i, values.Length - i);
        _ids = ids;
        _values = values;
        return true;
    }

    public IEnumerable<(PropertyId Id, PropValue Value)> Entries
    {
        get
        {
            for (int i = 0; i < _ids.Length; i++) yield return (new PropertyId(_ids[i]), _values[i]);
        }
    }
}

/// <summary>Lectura y escritura de propiedades de una instancia respecto a su prototipo.</summary>
public static class PropertyOps
{
    public static bool TryGet(Prototype prototype, PropertyOverrides? overrides, PropertyId id, out PropValue value)
    {
        if (overrides != null && overrides.TryGet(id, out value)) return true;
        if (prototype.TryGetDef(id, out var def)) { value = def.Default; return true; }
        value = default;
        return false;
    }

    public static PropValue Get(Prototype prototype, PropertyOverrides? overrides, PropertyId id)
    {
        if (TryGet(prototype, overrides, id, out var value)) return value;
        throw new KeyNotFoundException($"'{prototype.Name}' no define la propiedad '{PropertyIds.NameOf(id)}'.");
    }

    /// <summary>
    /// Escribe una propiedad. Si el valor coincide con el del prototipo se BORRA el cambio (y si no queda ninguno, 'overrides' pasa a
    /// null): una instancia que vuelve a su estado normal deja de ocupar memoria. Enteros y decimales se convierten entre sí.
    /// </summary>
    public static void Set(Prototype prototype, ref PropertyOverrides? overrides, PropertyId id, PropValue value)
    {
        if (!prototype.TryGetDef(id, out var def))
            throw new KeyNotFoundException($"'{prototype.Name}' no define la propiedad '{PropertyIds.NameOf(id)}'.");
        value = Coerce(def, value);

        if (value == def.Default)
        {
            if (overrides != null && overrides.Remove(id) && overrides.Count == 0) overrides = null;
            return;
        }
        overrides ??= new PropertyOverrides();
        overrides.Set(id, value);
    }

    public static void Reset(Prototype prototype, ref PropertyOverrides? overrides, PropertyId id)
    {
        if (overrides != null && overrides.Remove(id) && overrides.Count == 0) overrides = null;
    }

    public static IEnumerable<PropertyView> Enumerate(Prototype prototype, PropertyOverrides? overrides)
    {
        foreach (var def in prototype.AllDefs())
        {
            string name = PropertyIds.NameOf(def.Id);
            if (overrides != null && overrides.TryGet(def.Id, out var v))
                yield return new PropertyView(name, def.Kind, v, def.Default, true);
            else
                yield return new PropertyView(name, def.Kind, def.Default, def.Default, false);
        }
    }

    private static PropValue Coerce(PropDef def, PropValue value)
    {
        if (value.Kind == def.Kind) return value;
        bool numericTarget = def.Kind is PropKind.Int or PropKind.Float;
        bool numericSource = value.Kind is PropKind.Int or PropKind.Float;
        if (numericTarget && numericSource)
            return def.Kind == PropKind.Int ? PropValue.Of((int)Math.Round(value.Number)) : PropValue.Of((float)value.Number);
        throw new InvalidOperationException(
            $"'{PropertyIds.NameOf(def.Id)}' es {def.Kind} y se le asignó un valor {value.Kind}.");
    }
}

/// <summary>Formato binario de los cambios de instancia (por NOMBRE de propiedad, porque los ids cambian entre ejecuciones).</summary>
public static class PropertySerializer
{
    public static void Write(BinaryWriter w, PropertyOverrides overrides)
    {
        var entries = overrides.Entries.ToList();
        w.Write((ushort)entries.Count);
        foreach (var (id, value) in entries)
        {
            w.Write(PropertyIds.NameOf(id));
            w.Write((byte)value.Kind);
            if (value.Kind == PropKind.Text) w.Write(value.Text ?? "");
            else w.Write(value.Number);
        }
    }

    public static PropertyOverrides Read(BinaryReader r)
    {
        int count = r.ReadUInt16();
        var overrides = new PropertyOverrides();
        for (int i = 0; i < count; i++)
        {
            string name = r.ReadString();
            var kind = (PropKind)r.ReadByte();
            if (kind > PropKind.Text) throw new InvalidDataException("Tipo de propiedad desconocido.");
            var value = kind == PropKind.Text ? PropValue.Of(r.ReadString()) : new PropValue(kind, r.ReadDouble(), null);
            overrides.Set(PropertyIds.Of(name), value);
        }
        return overrides;
    }

    /// <summary>Cambios de las celdas de un chunk: índice local de la celda -> cambios.</summary>
    public static void WriteCellMap(BinaryWriter w, Dictionary<int, PropertyOverrides> map)
    {
        w.Write(map.Count);
        foreach (var (index, overrides) in map)
        {
            w.Write(index);
            Write(w, overrides);
        }
    }

    public static Dictionary<int, PropertyOverrides> ReadCellMap(BinaryReader r)
    {
        int count = r.ReadInt32();
        if (count < 0 || count > 1 << 20) throw new InvalidDataException("Número de celdas con propiedades no válido.");
        var map = new Dictionary<int, PropertyOverrides>(count);
        for (int i = 0; i < count; i++)
        {
            int index = r.ReadInt32();
            map[index] = Read(r);
        }
        return map;
    }
}
