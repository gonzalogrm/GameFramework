using GF.UI;
using GF.World.Items;
using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;

namespace MiniCraft;

/// <summary>
/// Inventario y equipamiento del jugador. E o Esc cierran. Las ranuras de equipamiento salen del tipo "player" de entitytypes.json.
/// Clic en un objeto equipable lo equipa; clic en un consumible lo usa; clic en una ranura equipada guarda su objeto.
/// </summary>
public sealed class InventoryScene : UiScene
{
    private readonly PlayerState _player;
    private VerticalStackPanel _list = null!;
    private string _note = "";

    public InventoryScene(PlayerState player) => _player = player;

    public override bool IsOverlay => true;

    protected override Widget BuildUi()
    {
        _list = new VerticalStackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Fill();
        return Ui.Dim(_list);
    }

    public override void Load()
    {
        base.Load();
        Game.Input.SetMouseCaptured(false);
    }

    public override void Update(GameTime gameTime)
    {
        if (Game.Input.Pressed("Inventory") || Game.Input.Pressed("Pause")) Game.Scenes.Pop();
    }

    /// <summary>Nombre en pantalla de un tipo de ranura (los desconocidos salen tal cual).</summary>
    private static string KindTitle(string kind) => kind switch
    {
        "hand" => "Mano",
        "head" => "Cabeza",
        "chest" => "Torso",
        "feet" => "Pies",
        "ring" => "Anillo",
        _ => kind,
    };

    private static string Stats(ItemDef def)
    {
        var parts = new List<string>();
        if (def.Attack > 0) parts.Add($"ataque +{def.Attack:0.#}");
        if (def.Defense > 0) parts.Add($"defensa +{def.Defense:0.#}");
        if (def.Use is { } use) parts.AddRange(use.Select(e => $"{e.Type} {e.Amount:0.#}"));
        return parts.Count == 0 ? "" : " (" + string.Join(", ", parts) + ")";
    }

    private void Fill()
    {
        var defs = ItemTypes.Registry;
        var inventory = _player.Inventory;
        var equipment = _player.Equipment;
        _list.Widgets.Clear();

        _list.Widgets.Add(Ui.Title("Inventario"));
        _list.Widgets.Add(new Label
        {
            Text = $"HP {Math.Ceiling(_player.Hp):0}/{_player.MaxHp:0}   Ataque {_player.AttackPower:0.#}   Defensa {Combat.Defense(_player):0.#}",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        if (_note.Length > 0) _list.Widgets.Add(new Label { Text = _note, HorizontalAlignment = HorizontalAlignment.Center });

        _list.Widgets.Add(new Label { Text = "Equipado (clic para guardar):" });
        for (int i = 0; i < equipment.SlotCount; i++)
        {
            int slot = i;
            string name = equipment.Get(slot) is { } id ? defs.Get(id).Title : "-";
            _list.Widgets.Add(Ui.Button($"{KindTitle(equipment.Layout.Kind(slot))}: {name}", () =>
            {
                ItemOps.Unequip(inventory, equipment, defs, slot);
                _note = "";
                Fill();
            }, 360));
        }

        _list.Widgets.Add(new Label { Text = "Mochila (clic para equipar o usar):" });
        int shown = 0;
        for (int i = 0; i < inventory.Size; i++)
        {
            var stack = inventory[i];
            if (stack.IsEmpty) continue;
            shown++;
            var def = defs.Get(stack.Item);
            int index = i;
            string text = $"{def.Title} x{stack.Count}{Stats(def)}";
            if (def.IsEquippable)
            {
                _list.Widgets.Add(Ui.Button($"[{KindTitle(def.Slot!)}] {text}", () =>
                {
                    _note = ItemOps.Equip(inventory, equipment, defs, index) ? "" : "No se puede equipar (sin ranura o sin sitio)";
                    Fill();
                }, 360));
            }
            else if (def.IsUsable)
            {
                _list.Widgets.Add(Ui.Button($"[Usar] {text}", () =>
                {
                    _note = _player.UseItem(index) ?? "No tiene efecto ahora";
                    Fill();
                }, 360));
            }
            else _list.Widgets.Add(new Label { Text = text });
        }
        if (shown == 0) _list.Widgets.Add(new Label { Text = "(vacía)" });
    }
}
