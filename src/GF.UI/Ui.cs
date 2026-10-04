using Microsoft.Xna.Framework;
using Myra.Graphics2D;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;

namespace GF.UI;

/// <summary>Helpers para construir UI con Myra sin repetir código.</summary>
public static class Ui
{
    public static Button Button(string text, Action onClick, int width = 220)
    {
		Button b = new Button
		{
			Width = width,
			Content = new Label
			{
				HorizontalAlignment = HorizontalAlignment.Center,
				Text = text
			}
		};

		//var b = new Button { Text = text, Width = width, HorizontalAlignment = HorizontalAlignment.Center };
        b.Click += (_, _) => onClick();
        return b;
    }

    public static Label Title(string text) =>
        new() { Text = text, HorizontalAlignment = HorizontalAlignment.Center };

    public static VerticalStackPanel CenteredMenu(string title, params (string Text, Action OnClick)[] items)
    {
        var panel = new VerticalStackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Widgets.Add(Title(title));
        foreach (var (text, onClick) in items) panel.Widgets.Add(Button(text, onClick));
        return panel;
    }

    /// <summary>Envuelve un widget con un fondo oscuro semitransparente a pantalla completa.</summary>
    public static Widget Dim(Widget content)
    {
        var panel = new Panel { Background = new SolidBrush(new Color(0, 0, 0, 150)) };
        panel.Widgets.Add(content);
        return panel;
    }
}
