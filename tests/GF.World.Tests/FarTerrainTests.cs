using GF.Core;
using GF.World.Voxel;
using Microsoft.Xna.Framework;
using Xunit;

namespace GF.World.Tests;

public class FarTerrainTests
{
    private sealed class FuncSource : IFarTerrainSource
    {
        private readonly Func<int, int, float> _height;
        public FuncSource(Func<int, int, float> height) => _height = height;

        public void Sample(int wx, int wz, int spacing, out float height, out Color color)
        {
            height = _height(wx, wz);
            color = new Color(200, 200, 200);
        }
    }

    private static float AverageRed(FarTileData data, int vertexCount) =>
        data.Vertices.Take(vertexCount).Average(v => (float)v.Color.R);

    // ------------------------------------------------------------------ selección (quadtree)

    [Fact]
    public void Selector_RefinesNearTheCamera_AndTilesNeverOverlap()
    {
        var list = new List<(FarTileKey Key, double Distance)>();
        FarTileSelector.Select(5000, 5000, 1024, 128, 1.6, 100_000, list);

        Assert.NotEmpty(list);
        var nearest = list.OrderBy(t => t.Distance).First();
        Assert.Equal(0, nearest.Key.Level);          // la cámara está sobre un tile del nivel más fino
        Assert.Equal(0.0, nearest.Distance, 6);
        Assert.True(list.Max(t => t.Key.Level) >= 2);   // y hay niveles más gruesos lejos
        Assert.All(list, t => Assert.True(t.Distance <= 1024));

        // Un tile solo se queda grueso si la cámara está lejos de él: nunca se deja un tile basto pegado a la cámara.
        Assert.All(list.Where(t => t.Key.Level > 0),
            t => Assert.True(t.Distance >= (128L << t.Key.Level) * 1.6 - 1e-6));

        for (int i = 0; i < list.Count; i++)
        for (int j = i + 1; j < list.Count; j++)
        {
            var (a, b) = (list[i].Key, list[j].Key);
            long sa = 128L << a.Level, sb = 128L << b.Level;
            bool overlap = a.X * sa < (b.X + 1) * sb && b.X * sb < (a.X + 1) * sa &&
                           a.Z * sa < (b.Z + 1) * sb && b.Z * sb < (a.Z + 1) * sa;
            Assert.False(overlap, $"{a} y {b} se solapan");
        }
    }

    [Fact]
    public void Selector_NeverPlacesTilesBeyondThePoles()
    {
        var list = new List<(FarTileKey Key, double Distance)>();
        FarTileSelector.Select(0, 150, 2048, 128, 1.6, 300, list);   // el mundo solo mide 300 bloques en Z

        Assert.NotEmpty(list);
        Assert.All(list, t =>
        {
            long size = 128L << t.Key.Level;
            Assert.True(t.Key.Z >= 0 && t.Key.Z * size < 300);
        });
    }

    [Fact]
    public void Selector_MoreDetailFactorMeansMoreTiles()
    {
        var low = new List<(FarTileKey Key, double Distance)>();
        var high = new List<(FarTileKey Key, double Distance)>();
        FarTileSelector.Select(5000, 5000, 2048, 128, 1.0, 100_000, low);
        FarTileSelector.Select(5000, 5000, 2048, 128, 3.0, 100_000, high);
        Assert.True(high.Count > low.Count);
    }

    [Fact]
    public void TileKeys_KnowTheirParentsAndChildren()
    {
        var key = new FarTileKey(0, -3, 5);
        Assert.Equal(new FarTileKey(1, -2, 2), key.Parent);
        Assert.Contains(key, key.Parent.Children);
        Assert.Equal(4, key.Parent.Children.Count());
        Assert.All(key.Parent.Children, c => Assert.Equal(key.Parent, c.Parent));
    }

    // ------------------------------------------------------------------ malla de un tile

    [Fact]
    public void Builder_ProducesTheExpectedGeometry_WithSkirts()
    {
        var data = FarTileBuilder.Build(new FuncSource((_, _) => 50f), new FarTileKey(0, 0, 0), 128, 32);

        Assert.Equal(33 * 33 + 4 * 33, data.Vertices.Length);
        Assert.Equal(32 * 32 * 6 + 4 * 32 * 6, data.Indices.Length);
        Assert.All(data.Indices, i => Assert.InRange((int)i, 0, data.Vertices.Length - 1));
        Assert.Equal(50f, data.MaxY);
        Assert.Equal(50f - (4 + 3), data.MinY);   // la falda baja separación + 3 bloques
        Assert.Equal(data.Indices.Length / 3, data.TriangleCount);

        // Las coordenadas X/Z son locales al tile y Y es absoluta.
        Assert.Equal(0f, data.Vertices[0].Position.X);
        Assert.Equal(128f, data.Vertices[32].Position.X);
        Assert.Equal(128f, data.Vertices[32 * 33].Position.Z);
        Assert.Equal(50f, data.Vertices[100].Position.Y);
    }

    [Fact]
    public void Builder_SamplesWithTheSpacingOfItsLevel()
    {
        var spacings = new HashSet<int>();
        var source = new SpacingProbe(spacings);
        FarTileBuilder.Build(source, new FarTileKey(2, 1, 1), 128, 32);   // nivel 2: tile de 512 bloques, muestras cada 16
        Assert.Equal(new[] { 16 }, spacings.ToArray());
    }

    private sealed class SpacingProbe : IFarTerrainSource
    {
        private readonly HashSet<int> _spacings;
        public SpacingProbe(HashSet<int> spacings) => _spacings = spacings;

        public void Sample(int wx, int wz, int spacing, out float height, out Color color)
        {
            lock (_spacings) _spacings.Add(spacing);
            height = 10f;
            color = Color.White;
        }
    }

    [Fact]
    public void Builder_LightsSlopesSoReliefIsVisible()
    {
        const int Main = 33 * 33;
        var flat = FarTileBuilder.Build(new FuncSource((_, _) => 50f), new FarTileKey(0, 0, 0), 128, 32);
        var towardLight = FarTileBuilder.Build(new FuncSource((x, _) => 50f + 0.5f * x), new FarTileKey(0, 0, 0), 128, 32);   // ladera hacia el sol (noroeste)
        var awayFromLight = FarTileBuilder.Build(new FuncSource((x, _) => 50f - 0.5f * x), new FarTileKey(0, 0, 0), 128, 32);

        float f = AverageRed(flat, Main), lit = AverageRed(towardLight, Main), dark = AverageRed(awayFromLight, Main);
        Assert.True(lit > f, $"iluminada {lit} > llana {f}");
        Assert.True(f > dark, $"llana {f} > en sombra {dark}");
    }

    // ------------------------------------------------------------------ ruido filtrado

    [Fact]
    public void BandLimitedNoise_EqualsTheFullNoiseWhenAllOctavesAreUsed_AndIsALowPassOtherwise()
    {
        var noise = new PerlinNoise(3);
        const float period = 10_000f, freq = 1f / 200f;
        double err1 = 0, err2 = 0;

        for (int i = 0; i < 300; i++)
        {
            float x = i * 37.3f, z = 1000f + i * 11.7f;
            float full = noise.Fbm2Periodic(x, z, period, freq, 4);
            Assert.Equal(full, noise.Fbm2PeriodicBandLimited(x, z, period, freq, 4, 4), 5);
            Assert.Equal(0f, noise.Fbm2PeriodicBandLimited(x, z, period, freq, 4, 0));

            err1 += Math.Abs(full - noise.Fbm2PeriodicBandLimited(x, z, period, freq, 4, 1));
            err2 += Math.Abs(full - noise.Fbm2PeriodicBandLimited(x, z, period, freq, 4, 2));
        }
        Assert.True(err2 < err1, "cada octava añadida acerca más al ruido completo");
    }
}
