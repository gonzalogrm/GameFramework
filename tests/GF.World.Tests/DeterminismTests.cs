using GF.Core;
using GF.World;
using Xunit;

namespace GF.World.Tests;

public class DeterminismTests
{
    private static readonly ChunkShape Shape = new(16, 16, 16);

    private sealed class TestStage : IGenerationStage<ushort>
    {
        public void Apply(Chunk<ushort> chunk, GenerationContext ctx)
        {
            var o = chunk.Shape.Origin(chunk.Coord);
            for (int z = 0; z < 16; z++)
            for (int x = 0; x < 16; x++)
            {
                int h = (int)(ctx.Noise.Fbm2((o.X + x) * 0.05f, (o.Z + z) * 0.05f) * 8 + 8);
                for (int y = 0; y < 16; y++)
                    chunk[x, y, z] = (ushort)((o.Y + y) <= h ? 1 + ctx.Random.Next(2) : 0);
            }
        }
    }

    private static Chunk<ushort> Gen(int seed, ChunkCoord c)
    {
        var chunk = new Chunk<ushort>(c, Shape);
        new WorldGenerator<ushort>(seed).AddStage(new TestStage()).Generate(chunk);
        return chunk;
    }

    [Fact]
    public void SameSeedAndCoord_ProduceIdenticalChunks() =>
        Assert.Equal(Gen(42, new(3, 0, -2)).Cells, Gen(42, new(3, 0, -2)).Cells);

    [Fact]
    public void DifferentSeeds_ProduceDifferentChunks() =>
        Assert.NotEqual(Gen(1, new(0, 0, 0)).Cells, Gen(2, new(0, 0, 0)).Cells);

    [Fact]
    public void GenerationOrder_DoesNotMatter()
    {
        var a1 = Gen(7, new(0, 0, 0)); var b1 = Gen(7, new(1, 0, 0));
        var b2 = Gen(7, new(1, 0, 0)); var a2 = Gen(7, new(0, 0, 0));
        Assert.Equal(a1.Cells, a2.Cells);
        Assert.Equal(b1.Cells, b2.Cells);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(-1, -1, -1, -1, -1, -1)]
    [InlineData(16, 15, -17, 1, -1, -2)]
    public void CellToChunk_HandlesNegatives(int x, int y, int z, int cx, int cy, int cz)
    {
        var cc = Shape.ToChunk(new CellCoord(x, y, z), out int lx, out int ly, out int lz);
        Assert.Equal(new ChunkCoord(cx, cy, cz), cc);
        Assert.InRange(lx, 0, 15); Assert.InRange(ly, 0, 15); Assert.InRange(lz, 0, 15);
        Assert.Equal(new CellCoord(x, y, z), new CellCoord(cc.X * 16 + lx, cc.Y * 16 + ly, cc.Z * 16 + lz));
    }
}
