using GF.World.Voxel;
using Microsoft.Xna.Framework;
using Xunit;

namespace GF.World.Tests;

public class FarHoleTests
{
    private sealed class FlatSource : IFarTerrainSource
    {
        public void Sample(int wx, int wz, int spacing, out float height, out Color color)
        {
            height = 50f;
            color = new Color(200, 200, 200);
        }
    }

    [Fact]
    public void Hole_Geometry()
    {
        var hole = new FarHole(100, 100, 300, 300);
        Assert.False(hole.IsEmpty);
        Assert.True(FarHole.Empty.IsEmpty);

        Assert.True(hole.ContainsCell(100, 100, 200));      // exacto
        Assert.True(hole.ContainsCell(150, 150, 50));
        Assert.False(hole.ContainsCell(90, 150, 50));       // sobresale por la izquierda
        Assert.False(hole.ContainsCell(280, 150, 50));      // sobresale por la derecha
        Assert.True(hole.Intersects(280, 150, 50));
        Assert.False(hole.Intersects(300, 100, 50));        // solo toca el borde
        Assert.False(FarHole.Empty.ContainsCell(0, 0, 1));

        Assert.True(hole.ContainsPoint(200, 200));
        Assert.False(hole.ContainsPoint(100, 200));         // el borde no cuenta
    }

    [Fact]
    public void ClipTo_IsTheOnlyThingATileDependsOn()
    {
        const long tileX = 1024, tileZ = 0, size = 512;
        var near = new FarHole(1100, 100, 1400, 400);        // dentro del tile
        var movedInside = new FarHole(1200, 100, 1500, 400);
        var farAway = new FarHole(-5000, -5000, -4000, -4000);
        var elsewhere = new FarHole(9000, 9000, 9500, 9500);

        Assert.Equal(new FarHole(1100, 100, 1400, 400), near.ClipTo(tileX, tileZ, size));
        Assert.NotEqual(near.ClipTo(tileX, tileZ, size), movedInside.ClipTo(tileX, tileZ, size));   // cambia lo que recorta: rehacer
        Assert.Equal(farAway.ClipTo(tileX, tileZ, size), elsewhere.ClipTo(tileX, tileZ, size));     // ninguno lo toca: misma malla
        Assert.True(farAway.ClipTo(tileX, tileZ, size).IsEmpty);

        // Un agujero que sobresale del tile se recorta al tile.
        var big = new FarHole(0, -100, 5000, 100);
        Assert.Equal(new FarHole(1024, 0, 1536, 100), big.ClipTo(tileX, tileZ, size));
    }

    [Fact]
    public void Selector_SkipsTilesEntirelyInsideTheHole()
    {
        var all = new List<(FarTileKey Key, double Distance)>();
        var ring = new List<(FarTileKey Key, double Distance)>();
        var hole = new FarHole(4400, 4400, 5600, 5600);   // 1200 x 1200 alrededor de la cámara

        FarTileSelector.Select(5000, 5000, 2048, 128, 1.6, 100_000, all);
        FarTileSelector.Select(5000, 5000, 2048, 128, 1.6, 100_000, ring, hole);

        Assert.True(ring.Count < all.Count);
        Assert.All(ring, t =>
        {
            long size = 128L << t.Key.Level;
            Assert.False(hole.ContainsCell(t.Key.X * size, t.Key.Z * size, size), $"{t.Key} está entero dentro del agujero");
        });
        // Los tiles fuera del agujero siguen todos ahí.
        Assert.All(all.Where(t =>
        {
            long size = 128L << t.Key.Level;
            return !hole.ContainsCell(t.Key.X * size, t.Key.Z * size, size);
        }), t => Assert.Contains(t.Key, ring.Select(r => r.Key)));
    }

    [Fact]
    public void Selector_NeverSelectsAnythingCloserThanTheHole_WhenTheHoleIsBigEnough()
    {
        var ring = new List<(FarTileKey Key, double Distance)>();
        // Agujero de 2 x 640 bloques alrededor de la cámara (ya alineado con los tiles de 128).
        var hole = new FarHole(4352, 4352, 5632, 5632);
        FarTileSelector.Select(5000, 5000, 2048, 128, 1.6, 100_000, ring, hole);
        Assert.NotEmpty(ring);
        Assert.All(ring, t => Assert.True(t.Distance > 0, "ningún tile contiene la cámara"));
    }

    [Fact]
    public void Builder_DoesNotGenerateCellsInsideTheHole()
    {
        var key = new FarTileKey(0, 0, 0);   // tile de 128 bloques, celdas de 4
        var full = FarTileBuilder.Build(new FlatSource(), key, 128, 32);
        var holed = FarTileBuilder.Build(new FlatSource(), key, 128, 32, new FarHole(32, 32, 96, 96));

        // 16 x 16 celdas enteras dentro del agujero: se quitan 256 cuadrados = 512 triángulos.
        Assert.Equal(full.Indices.Length - 256 * 6, holed.Indices.Length);
        Assert.Equal(full.Vertices.Length, holed.Vertices.Length);
        Assert.All(holed.Indices, i => Assert.InRange((int)i, 0, holed.Vertices.Length - 1));

        // Ningún triángulo de la superficie tiene su centro dentro del agujero.
        int surface = holed.Indices.Length - 4 * 32 * 6;
        for (int t = 0; t < surface; t += 3)
        {
            var a = holed.Vertices[holed.Indices[t]].Position;
            var b = holed.Vertices[holed.Indices[t + 1]].Position;
            var c = holed.Vertices[holed.Indices[t + 2]].Position;
            float cx = (a.X + b.X + c.X) / 3f, cz = (a.Z + b.Z + c.Z) / 3f;
            Assert.False(cx > 32 && cx < 96 && cz > 32 && cz < 96, $"triángulo en ({cx}, {cz})");
        }
    }

    [Fact]
    public void Builder_SkipsSkirtsOnEdgesInsideTheHole_ButKeepsTheOthers()
    {
        var key = new FarTileKey(0, 0, 0);
        var full = FarTileBuilder.Build(new FlatSource(), key, 128, 32);
        // El agujero cubre el tile entero: ni superficie ni faldas (las del borde quedan justo en el límite, no dentro).
        var covered = FarTileBuilder.Build(new FlatSource(), key, 128, 32, new FarHole(-10, -10, 138, 138));
        Assert.True(covered.Indices.Length < full.Indices.Length / 3);
        Assert.Empty(FarTileBuilder.Build(new FlatSource(), key, 128, 32, new FarHole(-10, -10, 138, 138)).Indices);
    }
}
