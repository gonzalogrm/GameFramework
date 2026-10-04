using Microsoft.Xna.Framework;
using Myra.Graphics2D;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;

namespace GF.UI;

/// <summary>Barra de slots seleccionables (hotbar de Minecraft / barra de acciones).</summary>
public sealed class Hotbar
{
    private static readonly IBrush Normal = new SolidBrush(new Color(0, 0, 0, 140));
    private static readonly IBrush Highlight = new SolidBrush(new Color(255, 255, 255, 110));

    private readonly Label[] _slots;
    private int _selected;

    public Widget Root { get; }

    public Hotbar(IReadOnlyList<string> names)
    {
        var stack = new HorizontalStackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 12),
        };
        _slots = new Label[names.Count];
        for (int i = 0; i < names.Count; i++)
        {
            _slots[i] = new Label { Text = $"{i + 1}: {names[i]}", Width = 96, Padding = new Thickness(6), Background = Normal };
            stack.Widgets.Add(_slots[i]);
        }
        Root = stack;
        Selected = 0;
    }

    public int Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            for (int i = 0; i < _slots.Length; i++) _slots[i].Background = i == _selected ? Highlight : Normal;
        }
    }
}
