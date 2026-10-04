namespace GF.Core;

/// <summary>Registro de contenido (bloques, tiles, ...): nombre a id numérico compacto.</summary>
public class Registry<TDef> where TDef : notnull
{
    private readonly List<TDef> _defs = new();
    private readonly Dictionary<string, ushort> _ids = new();

    public int Count => _defs.Count;

    public ushort Register(string name, TDef def)
    {
        if (_ids.ContainsKey(name)) throw new InvalidOperationException($"'{name}' ya está registrado.");
        if (_defs.Count >= ushort.MaxValue) throw new InvalidOperationException("Registro lleno.");
        ushort id = (ushort)_defs.Count;
        _defs.Add(def);
        _ids[name] = id;
        return id;
    }

    public TDef Get(ushort id) => _defs[id];
    public ushort IdOf(string name) => _ids[name];
    public bool TryGetId(string name, out ushort id) => _ids.TryGetValue(name, out id);
    public IEnumerable<string> NamesInOrder() => _ids.OrderBy(kv => kv.Value).Select(kv => kv.Key);
}
