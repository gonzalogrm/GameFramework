using GF.Core;
using Xunit;

namespace GF.World.Tests;

public class WrapNoiseTests
{
    private const float Period = 4096f;

    [Fact]
    public void TheNoise_WrapsExactlyInX_ForAnyOrientation()
    {
        var n = new WrapNoise(7, Period);
        foreach (float aspect in new[] { 1f, 3f, 0.33f })
            for (float z = -300f; z < 900f; z += 97f)
                for (float x = 0f; x < 2000f; x += 131f)
                {
                    Assert.Equal(n.Perlin(x, z, 500f, 1, aspect), n.Perlin(x + Period, z, 500f, 1, aspect), 3);
                    Assert.Equal(n.Fbm(x, z, 800f, 4, 2, aspect), n.Fbm(x + Period, z, 800f, 4, 2, aspect), 3);
                }
    }

    [Fact]
    public void TheNoise_IsDeterministic_BoundedAndDependsOnSeedAndLayer()
    {
        var a = new WrapNoise(1, Period);
        var b = new WrapNoise(1, Period);
        var c = new WrapNoise(2, Period);
        int differentSeed = 0, differentLayer = 0;
        for (int i = 0; i < 400; i++)
        {
            float x = i * 37.3f, z = i * 11.7f - 1000f;
            float v = a.Perlin(x, z, 300f, 0);
            Assert.Equal(v, b.Perlin(x, z, 300f, 0));
            Assert.InRange(v, -1.01f, 1.01f);
            Assert.InRange(a.Ridged(x, z, 300f, 4, 3), 0f, 1.01f);
            if (Math.Abs(v - c.Perlin(x, z, 300f, 0)) > 1e-4f) differentSeed++;
            if (Math.Abs(v - a.Perlin(x, z, 300f, 1)) > 1e-4f) differentLayer++;
        }
        Assert.True(differentSeed > 300);
        Assert.True(differentLayer > 300);
    }

    [Fact]
    public void ElongatedNoise_ChangesSlowerAlongItsLongAxis()
    {
        var n = new WrapNoise(3, 65536f);
        double alongZ = 0, alongX = 0;
        for (int i = 0; i < 500; i++)
        {
            float x = i * 53f, z = i * 29f;
            alongZ += Math.Abs(n.Perlin(x, z + 20f, 400f, 0, 4f) - n.Perlin(x, z, 400f, 0, 4f));   // aspect 4: alargado en Z
            alongX += Math.Abs(n.Perlin(x + 20f, z, 400f, 0, 4f) - n.Perlin(x, z, 400f, 0, 4f));
        }
        Assert.True(alongZ < alongX * 0.6, $"Z {alongZ:0.00} X {alongX:0.00}");
    }

    [Fact]
    public void BandLimiting_DropsFineOctavesButKeepsTheCoarseShape()
    {
        var n = new WrapNoise(5, Period);
        double diffFull = 0, diffCoarse = 0;
        for (int i = 0; i < 300; i++)
        {
            float x = i * 41f, z = i * 17f;
            float full = n.Fbm(x, z, 1000f, 5, 4);
            float coarse = n.Fbm(x, z, 1000f, 5, 4, 1f, 2);
            diffFull += Math.Abs(full);
            diffCoarse += Math.Abs(full - coarse);
        }
        Assert.True(diffCoarse < diffFull * 0.5);   // las octavas finas aportan poco: el filtrado conserva la forma gruesa
    }
}
