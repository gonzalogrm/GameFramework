using GF.UI;
using GF.World.Items;
using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;

namespace MiniCraft;

/// <summary>Inventario y equipamiento del jugador. E o Esc cierran. Clic en un objeto equipable lo equipa; clic en una ranura equipada lo guarda.</summary>
public sealed class InventoryScene : UiScene
{
    private readonly PlayerState _player;
    private VerticalStackPanel _list = null!;

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

    private static string SlotName(EquipSlot slot) => slot switch
    {
        EquipSlot.MainHand => "Mano",
        EquipSlot.Head => "Cabeza",
        EquipSlot.Chest => "Torso",
        _ => "Pies",
    };

    private void Fill()
    {
        var defs = ItemTypes.Registry;
        var inventory = _player.Inventory;
        _list.Widgets.Clear();

        _list.Widgets.Add(Ui.Title("Inventario"));
        _list.Widgets.Add(new Label
        {
            Text = $"HP {Math.Ceiling(_player.Hp):0}/{_player.MaxHp:0}   Ataque {_player.AttackPower:0.#}   Defensa {Combat.Defense(_player):0.#}",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        _list.Widgets.Add(new Label { Text = "Equipado (clic para guardar):" });
        foreach (var slot in Enum.GetValues<EquipSlot>())
        {
            var s = slot;
            string name = _player.Equipment.Get(s) is { } id ? defs.Get(id).Title : "-";
            _list.Widgets.Add(Ui.Button($"{SlotName(s)}: {name}", () =>
            {
                ItemOps.Unequip(inventory, _player.Equipment, defs, s);
                Fill();
            }, 320));
        }

        _list.Widgets.Add(new Label { Text = "Mochila (clic para equipar):" });
        int shown = 0;
        for (int i = 0; i < inventory.Size; i++)
        {
            var stack = inventory[i];
            if (stack.IsEmpty) continue;
            shown++;
            var def = defs.Get(stack.Item);
            int index = i;
            if (def.Slot is { } slot)
            {
                string text = $"{def.Title} ({SlotName(slot)}, ataque +{def.Attack:0.#}, defensa +{def.Defense:0.#})";
                _list.Widgets.Add(Ui.Button(text, () =>
                {
                    ItemOps.Equip(inventory, _player.Equipment, defs, index);
                    Fill();
                }, 320));
            }
            else _list.Widgets.Add(new Label { Text = $"{def.Title} x{stack.Count}" });
        }
        if (shown == 0) _list.Widgets.Add(new Label { Text = "(vacía)" });
    }
}
