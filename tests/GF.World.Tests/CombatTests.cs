using GF.Core;
using GF.World;
using GF.World.Items;
using GF.World.Voxel;
using Xunit;

namespace GF.World.Tests;

public class PathfinderTests
{
    private readonly BlockRegistry _blocks = new();
    private readonly World<ushort> _world = new(new ChunkShape(16, 16, 16));
    private readonly ushort _stone;

    public PathfinderTests()
    {
        _stone = _blocks.Register("stone", BlockDef.Cube("stone", 0));
        var chunk = new Chunk<ushort>(new ChunkCoord(0, 0, 0), _world.Shape);
        for (int y = 0; y < 4; y++)
        for (int z = 0; z < 16; z++)
        for (int x = 0; x < 16; x++)
            chunk[x, y, z] = _stone;
        _world.AddChunk(chunk);   // suelo hasta y = 3: se camina sobre y = 4
    }

    private void Wall(int x, int z0, int z1, int height)
    {
        for (int z = z0; z <= z1; z++)
        for (int y = 4; y < 4 + height; y++)
            _world.SetCell(new CellCoord(x, y, z), _stone);
    }

    private bool Solid(CellCoord c) => _blocks.Get(_world.GetCell(c)).Solid;

    [Fact]
    public void AWall_IsGoneAround_AndThePathEndsAtTheGoal()
    {
        Wall(7, 0, 11, 3);   // hay que rodearla por z >= 12
        var path = VoxelPathfinder.FindPath(_world, _blocks, new CellCoord(2, 4, 5), new CellCoord(12, 4, 5), maxNodes: 3000);

        Assert.NotNull(path);
        Assert.Equal(new CellCoord(2, 4, 5), path![0]);
        Assert.Equal((12, 5), (path[^1].X, path[^1].Z));
        Assert.Contains(path, c => c.Z >= 12);
        Assert.All(path, c => { Assert.False(Solid(c)); Assert.False(Solid(c.Offset(0, 1, 0))); Assert.True(Solid(c.Offset(0, -1, 0))); });
    }

    [Fact]
    public void AOneBlockStep_IsClimbed()
    {
        _world.SetCell(new CellCoord(6, 4, 5), _stone);
        Wall(6, 0, 4, 3);
        Wall(6, 6, 15, 3);   // solo se puede cruzar por el escalón de z = 5
        var path = VoxelPathfinder.FindPath(_world, _blocks, new CellCoord(2, 4, 5), new CellCoord(10, 4, 5), maxNodes: 3000);

        Assert.NotNull(path);
        Assert.Contains(path!, c => c.X == 6 && c.Y == 5);
        Assert.Equal(10, path[^1].X);
    }

    [Fact]
    public void WhenTheGoalIsUnreachable_ThePathGoesToTheClosestPoint()
    {
        Wall(7, 0, 15, 3);   // pared completa: más allá está sin cargar
        var path = VoxelPathfinder.FindPath(_world, _blocks, new CellCoord(2, 4, 5), new CellCoord(12, 4, 5), maxNodes: 3000);

        Assert.NotNull(path);
        Assert.True(path![^1].X <= 6);
        Assert.True(path[^1].X >= 5);   // se acerca todo lo que puede a la pared
    }
}

public class InventoryTests
{
    private const int Hand = 0, Head = 1;   // índices del SlotLayout por defecto: hand, head, chest, feet
    private static readonly Registry<ItemDef> Defs = new();
    private static readonly ushort Bone, Sword, Cap;

    static InventoryTests()
    {
        Bone = Defs.Register("bone", new ItemDef("bone", "Hueso", 4));
        Sword = Defs.Register("sword", new ItemDef("sword", "Espada", 1, "hand", Attack: 2f));
        Cap = Defs.Register("cap", new ItemDef("cap", "Gorro", 1, "head", Defense: 1f));
    }

    [Fact]
    public void Add_FillsStacks_ThenSlots_AndReturnsWhatDidNotFit()
    {
        var inv = new Inventory(2);   // 2 ranuras de 4 = 8 huesos
        Assert.Equal(0, inv.Add(Defs, Bone, 6));
        Assert.Equal(6, inv.CountOf(Bone));
        Assert.Equal(2, inv.Stacks.Count());
        Assert.Equal(3, inv.Add(Defs, Bone, 5));   // caben 2 más
        Assert.Equal(8, inv.CountOf(Bone));
        Assert.Equal(3, inv.Remove(Bone, 3));
        Assert.Equal(5, inv.CountOf(Bone));
    }

    [Fact]
    public void Equip_MovesTheItemToItsSlot_SwapsThePreviousOne_AndAddsToTheStats()
    {
        var inv = new Inventory(4);
        var eq = new Equipment();
        inv.Add(Defs, Sword, 1);
        inv.Add(Defs, Cap, 1);

        Assert.True(ItemOps.Equip(inv, eq, Defs, 0));
        Assert.Equal(Sword, eq.Get(Hand));
        Assert.Equal(0, inv.CountOf(Sword));
        Assert.Equal(2f, eq.Attack(Defs));

        Assert.True(ItemOps.Equip(inv, eq, Defs, 1));
        Assert.Equal(1f, eq.Defense(Defs));

        Assert.True(ItemOps.Unequip(inv, eq, Defs, Hand));
        Assert.Null(eq.Get(Hand));
        Assert.Equal(1, inv.CountOf(Sword));
        Assert.False(ItemOps.Equip(inv, eq, Defs, 3));   // ranura vacía: nada que equipar
    }

    [Fact]
    public void ExportImport_RoundTripsByName_AndEmptyHoldersCostNothing()
    {
        var inv = new Inventory(4);
        var eq = new Equipment();
        Assert.Null(inv.Export(Defs));   // vacío: null (no ocupa nada en el archivo)
        Assert.Null(eq.Export(Defs));

        inv.Add(Defs, Bone, 3);
        eq.Set(Head, Cap);
        var inv2 = new Inventory(4);
        var eq2 = new Equipment();
        inv2.Import(inv.Export(Defs), Defs);
        eq2.Import(eq.Export(Defs), Defs);

        Assert.Equal(3, inv2.CountOf(Bone));
        Assert.Equal(Cap, eq2.Get(Head));
    }
}
