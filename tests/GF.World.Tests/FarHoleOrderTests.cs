using GF.World.Voxel;
using Xunit;

namespace GF.World.Tests;

public class FarHoleOrderTests
{
    private static HashSet<(int X, int Z)> Square(int cx, int cz, int radius)
    {
        var set = new HashSet<(int, int)>();
        for (int dz = -radius; dz <= radius; dz++)
        for (int dx = -radius; dx <= radius; dx++)
            set.Add((cx + dx, cz + dz));
        return set;
    }

    [Fact]
    public void CoveredRadius_StopsAtTheFirstColumnWithoutAMesh()
    {
        var built = Square(10, 20, 4);
        built.Remove((13, 18));   // falta una columna del anillo 3
        Assert.Equal(2, FarHoleBuilder.CoveredRadius(10, 20, 6, (x, z) => built.Contains((x, z))));
    }

    [Fact]
    public void CoveredRadius_IsMinusOneWhenTheCameraColumnIsNotBuilt_AndCappedByTheMaximum()
    {
        Assert.Equal(-1, FarHoleBuilder.CoveredRadius(0, 0, 5, (_, _) => false));
        Assert.Equal(5, FarHoleBuilder.CoveredRadius(0, 0, 5, (_, _) => true));
        Assert.Equal(0, FarHoleBuilder.CoveredRadius(0, 0, 0, (_, _) => true));
    }

    [Fact]
    public void TheHole_NeverIncludesAColumnWhoseChunkIsNotYetVisible()
    {
        var rnd = new Random(11);
        for (int trial = 0; trial < 200; trial++)
        {
            int cx = rnd.Next(-50, 50), cz = rnd.Next(0, 100);
            var built = Square(cx, cz, rnd.Next(0, 6));
            for (int i = rnd.Next(0, 4); i > 0; i--)
                built.Remove((cx + rnd.Next(-5, 6), cz + rnd.Next(-5, 6)));
            built.RemoveWhere(_ => rnd.Next(40) == 0);   // y algún hueco más

            int radius = FarHoleBuilder.CoveredRadius(cx, cz, 8, (x, z) => built.Contains((x, z)));
            var hole = FarHoleBuilder.ForRadius(cx, cz, radius, 16);
            if (hole.IsEmpty) continue;

            // Cada columna de chunks dentro del agujero tiene que estar construida.
            for (int x = hole.MinX / 16; x < hole.MaxX / 16; x++)
            for (int z = hole.MinZ / 16; z < hole.MaxZ / 16; z++)
                Assert.True(built.Contains((x, z)), $"la columna ({x},{z}) está en el agujero pero aún no se ve");
        }
    }

    [Fact]
    public void ForRadius_BuildsTheSquareInBlocks()
    {
        var hole = FarHoleBuilder.ForRadius(2, 5, 1, 16);
        Assert.Equal(new FarHole(16, 64, 64, 112), hole);   // chunks 1..3 en X, 4..6 en Z
        Assert.True(FarHoleBuilder.ForRadius(2, 5, -1, 16).IsEmpty);
    }

    [Fact]
    public void Contains_DecidesWhetherATileBuiltWithAnOlderHoleIsStillSafe()
    {
        var current = new FarHole(0, 0, 100, 100);
        Assert.True(current.Contains(new FarHole(10, 10, 90, 90)));       // el agujero antiguo era menor: la malla solo tiene celdas de más
        Assert.True(current.Contains(current));
        Assert.True(current.Contains(default));                           // la malla se hizo sin agujero: tiene todas las celdas
        Assert.False(current.Contains(new FarHole(-16, 0, 100, 100)));    // el agujero antiguo era mayor: faltan celdas que ahora no cubre ningún chunk
        Assert.False(default(FarHole).Contains(new FarHole(0, 0, 10, 10)));
    }
}
