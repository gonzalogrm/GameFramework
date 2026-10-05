using GF.Core;
using GF.World;
using GF.World.Voxel;
using Xunit;

namespace GF.World.Tests;

public class BlockPropertyTests
{
    private readonly BlockRegistry _blocks = new();
    private readonly World<ushort> _world = new(new ChunkShape(16, 16, 16));
    private readonly ushort _stone, _dirt;

    public BlockPropertyTests()
    {
        var proto = new Prototype("piedra").Float("durability", 3f);
        _stone = _blocks.Register("stone", BlockDef.Cube("stone", 0) with { Props = proto });
        _dirt = _blocks.Register("dirt", BlockDef.Cube("dirt", 1));   // sin prototipo

        var chunk = new Chunk<ushort>(new ChunkCoord(0, 0, 0), _world.Shape);
        Array.Fill(chunk.Cells, _stone);
        _world.AddChunk(chunk);
    }

    private BlockRef At(int x, int y, int z) => new(_world, _blocks, new CellCoord(x, y, z));

    [Fact]
    public void UntouchedBlocks_ShareThePrototype_AndStoreNothing()
    {
        Assert.Equal(3f, At(1, 1, 1).GetFloat("durability"));
        Assert.Equal(0, _world.CellOverrideCount);
        Assert.False(_world.GetChunk(new ChunkCoord(0, 0, 0))!.IsModified);
    }

    [Fact]
    public void ChangingOneBlock_StoresOnlyThatBlock_AndMarksTheChunkForSaving()
    {
        At(1, 1, 1).Set("durability", 2f);

        Assert.Equal(2f, At(1, 1, 1).GetFloat("durability"));
        Assert.Equal(1, At(1, 1, 1).OverrideCount);
        Assert.Equal(3f, At(2, 1, 1).GetFloat("durability"));   // el vecino sigue con el valor del prototipo
        Assert.Equal(1, _world.CellOverrideCount);
        Assert.True(_world.GetChunk(new ChunkCoord(0, 0, 0))!.IsModified);

        At(1, 1, 1).Set("durability", 3f);   // vuelve al valor por defecto
        Assert.Equal(0, _world.CellOverrideCount);
    }

    [Fact]
    public void ReplacingTheBlock_ClearsItsChanges_ButReassigningTheSameBlockKeepsThem()
    {
        var cell = new CellCoord(4, 4, 4);
        At(4, 4, 4).Set("durability", 1f);

        _world.SetCell(cell, _stone);
        Assert.Equal(1, _world.CellOverrideCount);   // mismo bloque: sigue siendo la misma instancia

        _world.SetCell(cell, BlockRegistry.Air);
        Assert.Equal(0, _world.CellOverrideCount);   // bloque nuevo: instancia nueva, sin cambios
    }

    [Fact]
    public void BlocksWithoutAPrototype_HaveNoProperties()
    {
        _world.SetCell(new CellCoord(0, 0, 0), _dirt);
        var dirt = At(0, 0, 0);

        Assert.False(dirt.Has("durability"));
        Assert.False(dirt.TryGetFloat("durability", out _));
        Assert.Throws<InvalidOperationException>(() => dirt.Set("durability", 1f));
        Assert.Empty(dirt.Properties);
    }
}
