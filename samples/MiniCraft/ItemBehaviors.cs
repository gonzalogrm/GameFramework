using GF.Core;
using GF.World.Entities;
using GF.World.Events;
using GF.World.Items;

namespace MiniCraft;

/// <summary>
/// Comportamiento que un COMPONENTE de objeto da a quien lo lleva equipado. Un objeto (del JSON o creado en partida) lista los nombres de sus componentes; aquí
/// se asocia cada nombre a código. Dos ganchos: recibir los eventos que le llegan a quien lo lleva, y un tick por segundo (IA/efectos).
/// Las propiedades del objeto parametrizan el comportamiento (p. ej. "thorns" = daño reflejado), y como una variante de runtime puede cambiarlas o
/// añadirlas, cada variante se puede comportar distinto.
/// </summary>
public abstract class ItemBehavior
{
    /// <summary>Quien lleva el objeto recibió un evento (daño, conversación...). Se llama antes de que el evento haga efecto.</summary>
    public virtual void OnHolderEvent(IInventoryHolder holder, ItemDef item, GameEvent ev, EventHub hub) { }

    /// <summary>Tick: se llama cada ~1 s (dt = segundos desde el anterior) mientras el objeto esté equipado.</summary>
    public virtual void Think(IInventoryHolder holder, ItemDef item, float dt, EventHub hub) { }
}

public static class ItemBehaviors
{
    private static readonly Dictionary<string, ItemBehavior> Table = new()
    {
        ["thorns"] = new Thorns(),
        ["regeneration"] = new Regeneration(),
    };

    /// <summary>Asocia (o sustituye) el comportamiento de un componente. Los objetos que lo listen lo ejecutan al llevarlos equipados.</summary>
    public static void Register(string component, ItemBehavior behavior) => Table[component.Trim().ToLowerInvariant()] = behavior;

    public static IEnumerable<string> Known => Table.Keys;

    private static int SlotCount(IInventoryHolder h) => h is Entity e ? e.GearSlotCount : h.Equipment.SlotCount;
    private static ushort? Gear(IInventoryHolder h, int slot) => h is Entity e ? e.GearAt(slot) : h.Equipment.Get(slot);

    /// <summary>Reparte un evento recibido por 'holder' a los componentes de los objetos que lleva equipados.</summary>
    public static void OnHolderEvent(IInventoryHolder holder, GameEvent ev, EventHub hub)
    {
        var defs = ItemTypes.Registry;
        for (int s = 0; s < SlotCount(holder); s++)
        {
            if (Gear(holder, s) is not { } id) continue;
            var def = defs.Get(id);
            foreach (var component in def.Components)
                if (Table.TryGetValue(component, out var behavior)) behavior.OnHolderEvent(holder, def, ev, hub);
        }
    }

    /// <summary>Tick de los componentes de lo equipado. Barato para quien no lleva nada con componentes (solo mira sus ranuras).</summary>
    public static void Think(IInventoryHolder holder, float dt, EventHub hub)
    {
        int slots = SlotCount(holder);
        if (slots == 0) return;
        var defs = ItemTypes.Registry;
        for (int s = 0; s < slots; s++)
        {
            if (Gear(holder, s) is not { } id) continue;
            var def = defs.Get(id);
            if (def.Components.Count == 0) continue;
            foreach (var component in def.Components)
                if (Table.TryGetValue(component, out var behavior)) behavior.Think(holder, def, dt, hub);
        }
    }

    private static bool Inherits(EventKind kind, EventKind ancestor)
    {
        for (var k = kind; k != null; k = k.Parent) if (ReferenceEquals(k, ancestor)) return true;
        return false;
    }

    /// <summary>"thorns": al recibir daño, devuelve al atacante el daño de la propiedad "thorns" (1 por defecto). Las espinas no activan espinas.</summary>
    private sealed class Thorns : ItemBehavior
    {
        public override void OnHolderEvent(IInventoryHolder holder, ItemDef item, GameEvent ev, EventHub hub)
        {
            if (!Inherits(ev.Kind, GameEvents.Damage) || Inherits(ev.Kind, GameEvents.Thorns)) return;
            if (ev.Source is not IEventReceiver attacker) return;
            float amount = item.Get("thorns")?.AsFloat ?? 1f;
            hub.Send(attacker, new GameEvent(GameEvents.Thorns, holder, amount));
        }
    }

    /// <summary>"regeneration": cura al que lo lleva la cantidad de la propiedad "regen" por segundo (0,5 por defecto).</summary>
    private sealed class Regeneration : ItemBehavior
    {
        public override void Think(IInventoryHolder holder, ItemDef item, float dt, EventHub hub)
        {
            float heal = (item.Get("regen")?.AsFloat ?? 0.5f) * dt;
            switch (holder)
            {
                case PlayerState p when p.Hp > 0f:
                    p.Hp = Math.Min(p.MaxHp, p.Hp + heal);
                    break;
                case Entity e:
                    float hp = e.GetFloat("hp");
                    if (hp > 0f) e.Set("hp", Math.Min(e.GetFloat("max_hp"), hp + heal));
                    break;
            }
        }
    }
}
