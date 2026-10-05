using GF.Core;

namespace GF.World.Voxel;

/// <summary>
/// Una INSTANCIA de bloque: una celda del mundo vista junto a su tipo. Es solo un manejador (mundo + celda), no copia nada.
/// Las propiedades salen del prototipo del tipo (BlockDef.Props) y solo se guarda un cambio, en el chunk, para las celdas que
/// lo necesitan: millones de bloques de piedra comparten un único valor de "durabilidad"; el que se ha golpeado guarda el suyo.
/// Al reemplazar el bloque (World.SetCell con otro tipo) la celda pierde sus cambios.
/// </summary>
public readonly struct BlockRef
{
    private readonly World<ushort> _world;
    private readonly BlockRegistry _blocks;

    public BlockRef(World<ushort> world, BlockRegistry blocks, CellCoord cell)
    {
        _world = world;
        _blocks = blocks;
        Cell = cell;
    }

    public CellCoord Cell { get; }
    public ushort Id => _world.GetCell(Cell);
    public BlockDef Def => _blocks.Get(Id);
    public Prototype? Prototype => Def.Props;

    /// <summary>Cuántas propiedades de este bloque difieren de su prototipo.</summary>
    public int OverrideCount => _world.GetCellOverrides(Cell)?.Count ?? 0;

    public bool Has(string name) => Prototype is { } p && p.TryGetDef(PropertyIds.Of(name), out _);

    public bool TryGet(string name, out PropValue value)
    {
        value = default;
        return Prototype is { } p && PropertyOps.TryGet(p, _world.GetCellOverrides(Cell), PropertyIds.Of(name), out value);
    }

    public bool TryGetFloat(string name, out float value)
    {
        bool ok = TryGet(name, out var v);
        value = v.AsFloat;
        return ok;
    }

    public PropValue Get(string name) => PropertyOps.Get(Require(), _world.GetCellOverrides(Cell), PropertyIds.Of(name));
    public float GetFloat(string name) => Get(name).AsFloat;
    public int GetInt(string name) => Get(name).AsInt;
    public bool GetBool(string name) => Get(name).AsBool;
    public string GetText(string name) => Get(name).AsText;

    public void Set(string name, float value) => Set(name, PropValue.Of(value));
    public void Set(string name, int value) => Set(name, PropValue.Of(value));
    public void Set(string name, bool value) => Set(name, PropValue.Of(value));
    public void Set(string name, string value) => Set(name, PropValue.Of(value));

    private void Set(string name, PropValue value)
    {
        var prototype = Require();
        var overrides = _world.GetCellOverrides(Cell);
        PropertyOps.Set(prototype, ref overrides, PropertyIds.Of(name), value);
        _world.SetCellOverrides(Cell, overrides);   // null si todo volvió a su valor por defecto
    }

    public void Reset(string name)
    {
        var prototype = Require();
        var overrides = _world.GetCellOverrides(Cell);
        PropertyOps.Reset(prototype, ref overrides, PropertyIds.Of(name));
        _world.SetCellOverrides(Cell, overrides);
    }

    public IEnumerable<PropertyView> Properties =>
        Prototype is { } p ? PropertyOps.Enumerate(p, _world.GetCellOverrides(Cell)) : Enumerable.Empty<PropertyView>();

    private Prototype Require() => Prototype ?? throw new InvalidOperationException(
        $"El bloque '{Def.Name}' no tiene prototipo de propiedades (BlockDef.Props).");
}
