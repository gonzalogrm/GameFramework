using GF.Core;
using GF.World.Map;
using Xunit;

namespace GF.World.Tests;

public class FloatingOriginTests
{
    private static readonly WorldScale Scale = new(200, 50, 32, 32, 16, 16);   // 102.400 bloques de ancho

    [Fact]
    public void Vec3d_DoesNotLosePrecisionFarFromOrigin()
    {
        // A 100.000 bloques un float solo distingue 1/128; un double distingue una milésima sin problema.
        var p = new Vec3d(100_000.001, 0, 0);
        var q = p + new Vec3d(0.002, 0, 0);
        Assert.Equal(0.002, q.X - p.X, 6);
    }

    [Fact]
    public void WrapX_Double_StaysInRange()
    {
        Assert.Equal(0.0, Scale.WrapX(102_400.0));
        Assert.Equal(102_399.5, Scale.WrapX(-0.5), 6);
        Assert.Equal(5.25, Scale.WrapX(102_405.25), 6);
        Assert.True(Scale.WrapX(-1e-20) < Scale.WidthBlocks);   // nunca devuelve el ancho exacto
    }

    [Fact]
    public void FloorToInt_Double_FloorsNegatives()
    {
        Assert.Equal(-1, IntMath.FloorToInt(-0.0001));
        Assert.Equal(100_000, IntMath.FloorToInt(100_000.9));
    }
}
