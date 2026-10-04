using GF.Core;

namespace GF.World.Map;

public static class WorldMapBuilder
{
    /// <summary>
    /// Construye el mapamundi como un IWorld de una sola capa: un chunk de MapWidth x MapHeight x 1.
    /// Cada región se muestrea en una rejilla samplesPerAxis x samplesPerAxis; se promedian elevación,
    /// temperatura y humedad, y el bioma de la celda es el más frecuente. Se calcula en paralelo.
    /// </summary>
    public static World<MapCell> Build(WorldScale scale, IClimateModel model, Func<ColumnSample, ushort> classify,
        int biomeCount, int samplesPerAxis = 6)
    {
        var shape = new ChunkShape(scale.MapWidth, scale.MapHeight, 1);
        var chunk = new Chunk<MapCell>(new ChunkCoord(0, 0, 0), shape);
        int n = Math.Max(1, samplesPerAxis);

        Parallel.For(0, scale.MapHeight, rz =>
        {
            var counts = new int[biomeCount];
            for (int rx = 0; rx < scale.MapWidth; rx++)
            {
                Array.Clear(counts);
                double height = 0, temp = 0, hum = 0;
                int water = 0;
                float min = float.MaxValue, max = float.MinValue;

                for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int wx = rx * scale.RegionBlocksX + (int)((i + 0.5) * scale.RegionBlocksX / n);
                    int wz = rz * scale.RegionBlocksZ + (int)((j + 0.5) * scale.RegionBlocksZ / n);
                    var s = model.Sample(wx, wz);
                    height += s.Height; temp += s.Temperature; hum += s.Humidity;
                    if (s.Water) water++;
                    min = MathF.Min(min, s.Height);
                    max = MathF.Max(max, s.Height);
                    counts[classify(s)]++;
                }

                int best = 0;
                for (int b = 1; b < biomeCount; b++) if (counts[b] > counts[best]) best = b;

                double total = n * n;
                chunk.Cells[shape.Index(rx, rz, 0)] = new MapCell
                {
                    Elevation = (float)(height / total),
                    Relief = max - min,
                    Temperature = (float)(temp / total),
                    Humidity = (float)(hum / total),
                    WaterFraction = (float)(water / total),
                    Biome = (ushort)best,
                };
            }
        });

        var world = new World<MapCell>(shape);
        world.AddChunk(chunk);
        return world;
    }
}
