using GF.Core;
using GF.World;
using GF.World.Map;
using Xunit;

namespace GF.World.Tests;

public class MapTests
{
    private static readonly WorldScale Scale = new(200, 50, 32, 32, 16, 16);

    private sealed class FlatModel : IClimateModel
    {
        public ColumnSample Sample(int wx, int wz) => new(40f, 0.5f, 0.5f, false);
    }

    private sealed class SlopeModel : IClimateModel
    {
        public ColumnSample Sample(int wx, int wz) => new(wx * 0.01f, 0.5f, 0.5f, wx < 100);
    }

    [Fact]
    public void WorldScale_ComputesDimensions()
    {
        Assert.Equal(512, Scale.RegionBlocksX);
        Assert.Equal(102_400, Scale.WidthBlocks);
        Assert.Equal(25_600, Scale.HeightBlocks);
        Assert.Equal(6_400, Scale.ChunkCountX);
        Assert.Equal(1_600, Scale.ChunkCountZ);
    }

    [Fact]
    public void WorldScale_WrapsXAndClampsZ()
    {
        Assert.Equal(102_399, Scale.WrapX(-1));
        Assert.Equal(0, Scale.WrapX(102_400));
        Assert.Equal(6_399, Scale.WrapChunkX(-1));
        Assert.Equal((199, 0), Scale.RegionOf(-1, -50));
        Assert.Equal((1, 49), Scale.RegionOf(512, 999_999));
        var c = Scale.RegionCenter(1, 0);
        Assert.Equal(768, c.X);
        Assert.Equal(256, c.Z);
        Assert.Equal(new ChunkCoord(31, 0, 0), Scale.ChunkOf(511, 0));
    }

    [Fact]
    public void Fbm2Periodic_RepeatsEveryPeriod()
    {
        var noise = new PerlinNoise(5);
        const float period = 10_000f;
        foreach (float x in new[] { 0f, 123.4f, 5_000f, 9_999f })
        {
            float a = noise.Fbm2Periodic(x, 77f, period, 1f / 700f, 3);
            float b = noise.Fbm2Periodic(x + period, 77f, period, 1f / 700f, 3);
            float c = noise.Fbm2Periodic(x - period, 77f, period, 1f / 700f, 3);
            Assert.Equal(a, b, 3);
            Assert.Equal(a, c, 3);
        }
    }

    [Fact]
    public void MapBuilder_AveragesFlatModel()
    {
        var scale = new WorldScale(4, 3, 2, 2, 16, 16);
        var map = WorldMapBuilder.Build(scale, new FlatModel(), _ => 1, biomeCount: 2, samplesPerAxis: 3);
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < 4; x++)
        {
            var c = map.GetCell(new CellCoord(x, y, 0));
            Assert.Equal(40f, c.Elevation, 3);
            Assert.Equal(0f, c.Relief, 3);
            Assert.Equal(1, c.Biome);
            Assert.Equal(0f, c.WaterFraction, 3);
        }
    }

    [Fact]
    public void MapBuilder_ElevationFollowsModelAndReportsRelief()
    {
        var scale = new WorldScale(4, 1, 2, 2, 16, 16);   // regiones de 32 bloques
        var map = WorldMapBuilder.Build(scale, new SlopeModel(), s => (ushort)(s.Water ? 0 : 1), biomeCount: 2);
        var west = map.GetCell(new CellCoord(0, 0, 0));
        var east = map.GetCell(new CellCoord(3, 0, 0));
        Assert.True(east.Elevation > west.Elevation);
        Assert.True(west.Relief > 0f);
        Assert.Equal(0, west.Biome);                 // x < 100: agua
        Assert.Equal(1, east.Biome);
        Assert.Equal(1f, west.WaterFraction, 3);
    }

    [Fact]
    public void FileChunkStore_CanonicalKeys_ShareFileAcrossTheSeam()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gf_seam_" + Guid.NewGuid().ToString("N"));
        try
        {
            var shape = new ChunkShape(16, 16, 16);
            var store = new FileChunkStore<ushort>(dir, c => c with { X = Scale.WrapChunkX(c.X) });
            var west = new Chunk<ushort>(new ChunkCoord(-1, 0, 5), shape);
            for (int i = 0; i < west.Cells.Length; i++) west.Cells[i] = (ushort)(i % 251);
            store.Save(west);

            var east = new Chunk<ushort>(new ChunkCoord(6_399, 0, 5), shape);
            Assert.True(store.TryLoad(east));
            Assert.Equal(west.Cells, east.Cells);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
