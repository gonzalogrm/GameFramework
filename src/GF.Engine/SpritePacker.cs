namespace GF.Engine;

public readonly record struct PackItem(string Name, int Width, int Height);
public readonly record struct PackPlacement(string Name, int X, int Y, int Width, int Height);

/// <summary>
/// Empaqueta rectángulos de cualquier tamaño en una textura (algoritmo de estantes). Es determinista: no depende del orden de
/// entrada. Sin dependencias de MonoGame, así que se puede probar sin ventana.
/// </summary>
public static class SpritePacker
{
    /// <param name="padding">Píxeles transparentes entre sprites y con el borde de la textura (evita sangrado al muestrear).</param>
    /// <returns>Posiciones y tamaño de la textura (potencias de dos, el menor que cabe y no excede maxSize).</returns>
    public static (List<PackPlacement> Placements, int Width, int Height) Pack(
        IReadOnlyList<PackItem> items, int padding = 1, int maxSize = 4096)
    {
        if (items.Count == 0) return (new List<PackPlacement>(), 1, 1);

        int widest = items.Max(i => i.Width), tallest = items.Max(i => i.Height);
        if (widest + 2 * padding > maxSize || tallest + 2 * padding > maxSize)
            throw new InvalidOperationException(
                $"Un sprite de {widest}x{tallest} px no cabe en una textura de {maxSize} px.");

        // Más altos primero (y por nombre para desempatar): el resultado no depende del orden en que se añadieron.
        var sorted = items.OrderByDescending(i => i.Height).ThenBy(i => i.Name, StringComparer.Ordinal).ToList();

        // Se prueba con anchuras crecientes hasta que la textura sea (casi) cuadrada.
        for (int width = NextPow2(widest + 2 * padding); width <= maxSize; width *= 2)
        {
            var placements = TryPack(sorted, width, padding, out int usedHeight);
            if (placements == null) continue;
            if (usedHeight <= width || width == maxSize)
            {
                if (usedHeight > maxSize)
                    throw new InvalidOperationException($"Los sprites no caben en una textura de {maxSize}x{maxSize} px.");
                return (placements, width, NextPow2(usedHeight));
            }
        }
        throw new InvalidOperationException($"Los sprites no caben en una textura de {maxSize}x{maxSize} px.");
    }

    private static List<PackPlacement>? TryPack(List<PackItem> sorted, int atlasWidth, int padding, out int usedHeight)
    {
        var result = new List<PackPlacement>(sorted.Count);
        int x = padding, y = padding, rowHeight = 0;
        foreach (var item in sorted)
        {
            if (x + item.Width + padding > atlasWidth)   // nueva fila
            {
                y += rowHeight + padding;
                x = padding;
                rowHeight = 0;
                if (x + item.Width + padding > atlasWidth) { usedHeight = 0; return null; }
            }
            result.Add(new PackPlacement(item.Name, x, y, item.Width, item.Height));
            x += item.Width + padding;
            rowHeight = Math.Max(rowHeight, item.Height);
        }
        usedHeight = y + rowHeight + padding;
        return result;
    }

    private static int NextPow2(int v)
    {
        int p = 1;
        while (p < v) p <<= 1;
        return p;
    }
}
