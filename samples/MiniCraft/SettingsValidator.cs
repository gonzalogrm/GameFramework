namespace MiniCraft;

/// <summary>
/// Validación de ajustes sin repetir código: limita cada valor a su rango y anota un aviso con el mismo texto de siempre ("Seccion.Nombre: v fuera de [min, max], se usa c").
///     var v = new SettingsValidator("Fog", warnings);
///     Radius = v.F(nameof(Radius), Radius, 16f, 256f);
///     CellSize = v.I(nameof(CellSize), CellSize, 1, 8);
/// Sección vacía = sin prefijo (como WorldSettings).
/// </summary>
public readonly struct SettingsValidator
{
    private readonly string _prefix;
    private readonly List<string> _warnings;

    public SettingsValidator(string section, List<string> warnings)
    {
        _prefix = section.Length == 0 ? "" : section + ".";
        _warnings = warnings;
    }

    /// <summary>Un aviso con el prefijo de la sección (el texto debe empezar por el nombre del ajuste).</summary>
    public void Warn(string text) => _warnings.Add(_prefix + text);

    public float F(string name, float value, float min, float max)
    {
        float c = float.IsNaN(value) ? min : Math.Clamp(value, min, max);
        if (c != value) Warn($"{name}: {value} fuera de [{min}, {max}], se usa {c}");
        return c;
    }

    public int I(string name, int value, int min, int max)
    {
        int c = Math.Clamp(value, min, max);
        if (c != value) Warn($"{name}: {value} fuera de [{min}, {max}], se usa {c}");
        return c;
    }

    /// <summary>Lista de 'count' pesos (negativos a 0; los que falten, 1; los que sobren, se recortan).</summary>
    public int[] Weights(string name, int[]? values, int count)
    {
        var r = new int[count];
        for (int i = 0; i < count; i++) r[i] = values != null && i < values.Length ? Math.Max(0, values[i]) : 1;
        if (values == null || values.Length != count) Warn($"{name}: debe tener {count} pesos; se completan con 1 o se recortan");
        return r;
    }

    /// <summary>Color RGB de 3 valores (0..1). Si no tiene 3, se usa 'fallback' y se avisa ("se usa {fallbackName} por defecto").</summary>
    public float[] Rgb(float[]? tint, float[] fallback, string fallbackName)
    {
        if (tint is not { Length: 3 })
        {
            Warn($"Tint debe tener 3 valores (R, G, B); se usa {fallbackName} por defecto");
            tint = (float[])fallback.Clone();
        }
        for (int i = 0; i < 3; i++) tint[i] = Math.Clamp(float.IsNaN(tint[i]) ? 0f : tint[i], 0f, 1f);
        return tint;
    }
}
