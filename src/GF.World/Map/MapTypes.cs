namespace GF.World.Map;

/// <summary>Muestra del clima/terreno en una columna del mundo.</summary>
public readonly record struct ColumnSample(float Height, float Temperature, float Humidity, bool Water);

/// <summary>
/// Modelo del mundo a gran escala. Es la FUENTE ÚNICA de verdad: lo usan tanto el generador de chunks
/// (a escala de bloque) como el constructor del mapa (promediando muchas muestras), así que lo que
/// se ve en el mapa es lo que se encuentra al llegar. Debe ser determinista y thread-safe.
/// </summary>
public interface IClimateModel
{
    ColumnSample Sample(int wx, int wz);
}

/// <summary>Tipo de bioma (sin dependencia de MonoGame: el color va como RGB).</summary>
public sealed record BiomeDef(string Name, byte R, byte G, byte B, bool IsWater = false);

/// <summary>Una celda del mapamundi = una región, resumida como promedio de sus columnas.</summary>
public struct MapCell
{
    public float Elevation;       // altura media (bloques)
    public float Relief;          // máx - mín entre las muestras: rugosidad de la región
    public float Temperature;     // 0..1
    public float Humidity;        // 0..1
    public float WaterFraction;   // 0..1
    public ushort Biome;          // bioma dominante (el más frecuente entre las muestras)
}
