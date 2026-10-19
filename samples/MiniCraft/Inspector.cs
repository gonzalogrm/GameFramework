using System.Globalization;
using System.Text;
using GF.Core;
using GF.World;
using GF.World.Entities;
using GF.World.Events;
using GF.World.Items;
using GF.World.Map;
using GF.World.Voxel;

namespace MiniCraft;

/// <summary>Texto del panel de inspección: el tipo (prototipo), la instancia y qué propiedades ha cambiado respecto a él.</summary>
public static class Inspector
{
    public static string Describe(Entity e, WorldScale scale, EventHub? hub = null)
    {
        var sb = new StringBuilder();
        sb.Append("ENTIDAD #").Append(e.Id).Append("   ").AppendLine(e.Prototype?.ChainName ?? e.Def?.Name ?? "(sin tipo)");
        sb.Append("Simulacion: ").AppendLine(e.Tier.ToString());
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Posicion: {e.Position.X:0.0}, {e.Position.Y:0.0}, {e.Position.Z:0.0}"));
        var (rx, rz) = scale.RegionOf((int)Math.Floor(e.Position.X), (int)Math.Floor(e.Position.Z));
        sb.AppendLine($"Region: {rx}, {rz}");
        sb.AppendLine(e.Destination is { } d
            ? string.Create(CultureInfo.InvariantCulture, $"Destino: {d.X:0}, {d.Z:0}")
            : "Destino: ninguno");
        sb.AppendLine();
        AppendEvents(sb, hub, e.Prototype);
        AppendComponents(sb, e.Prototype);
        AppendGear(sb, e);
        AppendProperties(sb, e.Properties);
        return sb.ToString();
    }

    /// <summary>Los componentes (comportamientos) del tipo, propios y heredados.</summary>
    private static void AppendComponents(StringBuilder sb, Prototype? prototype)
    {
        var components = prototype?.AllComponents();
        if (components is { Count: > 0 }) sb.AppendLine("Componentes: " + string.Join(", ", components));
    }

    /// <summary>
    /// Lo que lleva la entidad: EQUIPO (armas y armaduras en cada ranura, con lo que dan) e INVENTARIO. Es lo de su variante salvo que haya cambiado; el dato
    /// sale de los mismos prototipos de objeto que se comparten entre todas las entidades.
    /// </summary>
    private static void AppendGear(StringBuilder sb, Entity e)
    {
        var defs = ItemTypes.Registry;
        var equipment = e.Equipment;
        sb.AppendLine($"Equipamiento ({equipment.SlotCount} ranuras)   ataque +{Combat.Attack(e):0.#}  defensa +{Combat.Defense(e):0.#}:");
        for (int s = 0; s < equipment.SlotCount; s++)
        {
            sb.Append("  ").Append(ItemText.SlotTitle(equipment.Layout, s).PadRight(9)).Append(": ");
            sb.AppendLine(equipment.Get(s) is { } id ? ItemText.Name(defs.Get(id)) + ItemText.Stats(defs.Get(id)) : "-");
        }
        var inventory = e.Inventory;
        int used = inventory.Stacks.Count();
        sb.AppendLine($"Inventario ({used}/{inventory.Size}):");
        if (used == 0) sb.AppendLine("  (vacio)");
        foreach (var stack in inventory.Stacks)
            sb.AppendLine($"  {ItemText.Name(defs.Get(stack.Item))} x{stack.Count}{ItemText.Stats(defs.Get(stack.Item))}");
        sb.AppendLine();
    }

    public static string Describe(World<ushort> world, BlockRegistry blocks, CellCoord cell, EventHub? hub = null)
    {
        var block = new BlockRef(world, blocks, cell);
        var def = block.Def;
        var chunk = world.Shape.ToChunk(cell);

        var sb = new StringBuilder();
        sb.Append("BLOQUE  ").AppendLine(def.Props?.ChainName ?? def.Name);
        sb.AppendLine($"Celda: {cell.X}, {cell.Y}, {cell.Z}   Chunk: {chunk.X}, {chunk.Y}, {chunk.Z}");
        sb.AppendLine($"Solido: {(def.Solid ? "si" : "no")}   Opaco: {(def.Opaque ? "si" : "no")}   Dibujo: {def.Render}");
        sb.AppendLine();
        AppendEvents(sb, hub, def.Props);
        if (def.Props == null) sb.AppendLine("Este tipo no tiene propiedades.");
        else AppendProperties(sb, block.Properties);
        return sb.ToString();
    }

    /// <summary>La lista de eventos que procesa este tipo (la comparten todas sus instancias).</summary>
    private static void AppendEvents(StringBuilder sb, EventHub? hub, Prototype? prototype)
    {
        if (hub == null) return;
        var kinds = hub.AcceptedKinds(prototype);
        sb.AppendLine("Eventos que procesa: " + (kinds.Count == 0 ? "ninguno" : string.Join(", ", kinds.Select(k => k.Name))));
    }

    private static void AppendProperties(StringBuilder sb, IEnumerable<PropertyView> properties)
    {
        var list = properties.ToList();
        int changed = list.Count(p => p.Overridden);
        sb.AppendLine($"Propiedades: {list.Count}   (cambiadas y guardadas en esta instancia: {changed})");
        foreach (var p in list)
        {
            sb.Append("  ").Append(p.Name.PadRight(10)).Append(" = ").Append(p.Value);
            if (p.Overridden) sb.Append("     * cambiada, por defecto ").Append(p.Default);
            sb.AppendLine();
        }
    }
}
