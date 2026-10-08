using GF.Core;
using GF.World;
using GF.World.Entities;
using GF.World.Events;
using GF.World.Map;
using GF.World.Voxel;
using Xunit;

namespace GF.World.Tests;

public class EventTests
{
    private static readonly EventKind Conversation = new("conversacion");
    private static readonly EventKind Damage = new("dano");
    private static readonly EventKind Fire = Damage.Derive("fuego");
    private static readonly EventKind Fireball = Fire.Derive("bola_de_fuego");
    private static readonly EventKind Alarm = new("alarma");

    private sealed class Receiver : IEventReceiver
    {
        public Receiver(Prototype? prototype, string label) { Prototype = prototype; Label = label; }
        public Prototype? Prototype { get; }
        public string Label { get; }
    }

    private static EntityStore NewStore() =>
        new(new ChunkShape(16, 16, 16), new WorldScale(4, 3, 2, 2, 16, 16));

    private static (EventHub Hub, EntityStore Store) NewHub(Prototype dog)
    {
        var defs = new Registry<EntityDef>();
        defs.Register("dog", new EntityDef("dog", 4, 150, 100, 60, 0.6f, 0.8f, dog));
        var store = NewStore();
        store.Definitions = defs;
        return (new EventHub(store), store);
    }

    // ------------------------------------------------------------------ tipos de evento

    [Fact]
    public void EventKinds_FormAHierarchy()
    {
        Assert.True(Fireball.Is(Fireball));
        Assert.True(Fireball.Is(Fire));
        Assert.True(Fireball.Is(Damage));
        Assert.False(Damage.Is(Fire));
        Assert.False(Conversation.Is(Damage));
    }

    // ------------------------------------------------------------------ aceptar o ignorar

    [Fact]
    public void OnlyReceiversThatAcceptTheEvent_ProcessIt()
    {
        var hub = new EventHub(NewStore());
        var villagerProto = new Prototype("aldeano");
        var treeProto = new Prototype("arbol");
        int heard = 0;
        hub.On<Receiver>(villagerProto, Conversation, (_, _, _) => heard++);
        var deliveries = new List<Delivery>();
        hub.Delivered += deliveries.Add;

        var villager = new Receiver(villagerProto, "Marta");
        var tree = new Receiver(treeProto, "Roble");
        Assert.Equal(DeliveryResult.Processed, hub.Send(villager, new GameEvent(Conversation)));
        Assert.Equal(DeliveryResult.Ignored, hub.Send(tree, new GameEvent(Conversation)));   // lo recibe y no lo procesa

        Assert.Equal(1, heard);
        Assert.True(hub.Accepts(villager, Conversation));
        Assert.False(hub.Accepts(tree, Conversation));
        Assert.Equal(new[] { DeliveryResult.Processed, DeliveryResult.Ignored }, deliveries.Select(d => d.Result));
        Assert.Same(tree, deliveries[1].Target);
    }

    [Fact]
    public void AReceiverWithoutAPrototype_IgnoresEverything()
    {
        var hub = new EventHub(NewStore());
        Assert.Equal(DeliveryResult.Ignored, hub.Send(new Receiver(null, "nada"), new GameEvent(Damage)));
    }

    [Fact]
    public void HandlersAreInheritedByPrototype_AndTheChildCanReplaceThem()
    {
        var hub = new EventHub(NewStore());
        var creature = new Prototype("criatura");
        var dog = creature.Derive("perro");
        var log = new List<string>();
        hub.On<Receiver>(creature, Damage, (_, _, _) => log.Add("criatura"));

        hub.Send(new Receiver(dog, "Rex"), new GameEvent(Damage));
        Assert.Equal(new[] { "criatura" }, log);        // el perro hereda el manejador

        hub.On<Receiver>(dog, Damage, (_, _, _) => log.Add("perro"));
        hub.Send(new Receiver(dog, "Rex"), new GameEvent(Damage));
        hub.Send(new Receiver(creature, "bicho"), new GameEvent(Damage));
        Assert.Equal(new[] { "criatura", "perro", "criatura" }, log);   // el hijo lo sustituye; el padre no cambia
    }

    [Fact]
    public void AcceptingAnAncestorKind_AcceptsItsDescendants_AndTheMostSpecificHandlerWins()
    {
        var hub = new EventHub(NewStore());
        var anything = new Prototype("criatura");
        var fireOnly = new Prototype("inflamable");
        var log = new List<string>();
        hub.On<Receiver>(anything, Damage, (_, ev, _) => log.Add("general:" + ev.Kind.Name));
        hub.On<Receiver>(fireOnly, Fire, (_, ev, _) => log.Add("fuego:" + ev.Kind.Name));

        // Una bola de fuego es daño y es fuego: la procesa quien acepte cualquiera de los dos.
        hub.Send(new Receiver(anything, "a"), new GameEvent(Fireball));
        hub.Send(new Receiver(fireOnly, "b"), new GameEvent(Fireball));
        // Pero quien solo acepta fuego no procesa un daño genérico.
        Assert.Equal(DeliveryResult.Ignored, hub.Send(new Receiver(fireOnly, "b"), new GameEvent(Damage)));
        Assert.Equal(new[] { "general:bola_de_fuego", "fuego:bola_de_fuego" }, log);

        hub.On<Receiver>(anything, Fireball, (_, _, _) => log.Add("especifico"));
        hub.Send(new Receiver(anything, "a"), new GameEvent(Fireball));
        hub.Send(new Receiver(anything, "a"), new GameEvent(Fire));
        Assert.Equal(new[] { "general:bola_de_fuego", "fuego:bola_de_fuego", "especifico", "general:fuego" }, log);
    }

    [Fact]
    public void AcceptedKinds_IncludesTheInheritedOnes_ChildFirst()
    {
        var hub = new EventHub(NewStore());
        var creature = new Prototype("criatura");
        var dog = creature.Derive("perro");
        hub.On<Receiver>(creature, Damage, (_, _, _) => { });
        hub.On<Receiver>(dog, Conversation, (_, _, _) => { });

        Assert.Equal(new[] { Conversation, Damage }, hub.AcceptedKinds(dog));
        Assert.Equal(new[] { Damage }, hub.AcceptedKinds(creature));
        Assert.Empty(hub.AcceptedKinds(new Prototype("roca")));
    }

    // ------------------------------------------------------------------ entidades: reparto por zona y emisión

    [Fact]
    public void Broadcast_ReachesOnlyTheEntitiesInRange_AndHonoursTheExclusion()
    {
        var dog = new Prototype("perro").Float("hp", 10f);
        var (hub, store) = NewHub(dog);
        int hits = 0;
        hub.On<Entity>(dog, Damage, (_, _, _) => hits++);

        var near1 = store.Spawn(0, new Vec3d(10, 1, 10), 1);
        var near2 = store.Spawn(0, new Vec3d(12, 1, 10), 1);
        store.Spawn(0, new Vec3d(60, 1, 10), 1);   // fuera del radio

        Assert.Equal(2, hub.Broadcast(new Vec3d(10, 1, 10), 5, new GameEvent(Fireball)));
        Assert.Equal(2, hits);
        Assert.Equal(1, hub.Broadcast(new Vec3d(10, 1, 10), 5, new GameEvent(Fireball), exclude: near1));
        Assert.Equal(3, hits);
        Assert.NotNull(near2);
    }

    [Fact]
    public void Broadcast_SurvivesHandlersThatRemoveEntities()
    {
        var dog = new Prototype("perro");
        var (hub, store) = NewHub(dog);
        hub.On<Entity>(dog, Damage, (e, _, h) => h.Entities.Remove(e));
        for (int i = 0; i < 3; i++) store.Spawn(0, new Vec3d(10 + i, 1, 10), 1);

        Assert.Equal(3, hub.Broadcast(new Vec3d(11, 1, 10), 5, new GameEvent(Damage)));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void AnEntityCanEmit_AndOnlyTheOthersHearIt()
    {
        var dog = new Prototype("perro");
        var (hub, store) = NewHub(dog);
        var heardBy = new List<Entity>();
        object? source = null;
        hub.On<Entity>(dog, Alarm, (e, ev, _) => { heardBy.Add(e); source = ev.Source; });

        var shouter = store.Spawn(0, new Vec3d(10, 1, 10), 1);
        var neighbour = store.Spawn(0, new Vec3d(13, 1, 10), 1);
        store.Spawn(0, new Vec3d(100, 1, 10), 1);

        Assert.Equal(1, hub.Emit(shouter, new GameEvent(Alarm), 10));
        Assert.Equal(new[] { neighbour }, heardBy);
        Assert.Same(shouter, source);   // el evento lleva a quien lo emitió
    }

    [Fact]
    public void EventCascades_AreCappedInsteadOfOverflowingTheStack()
    {
        var dog = new Prototype("perro");
        var (hub, store) = NewHub(dog);
        int calls = 0;
        hub.On<Entity>(dog, Damage, (e, ev, h) => { calls++; h.Send(e, ev); });   // se reenvía a sí mismo para siempre
        var entity = store.Spawn(0, new Vec3d(10, 1, 10), 1);

        hub.Send(entity, new GameEvent(Damage));
        Assert.Equal(EventHub.MaxDepth, calls);
    }

    [Fact]
    public void Entities_LabelThemselvesForMessages()
    {
        var proto = new Prototype("perro").Text("name", "").Text("species", "Perro");
        var (_, store) = NewHub(proto);
        var dog = store.Spawn(0, new Vec3d(10, 1, 10), 1);
        Assert.Equal("Perro", dog.Label);
        dog.Set("name", "Rex");
        Assert.Equal("Perro Rex", dog.Label);
    }
}

public class BlockEventTests
{
    private static readonly EventKind Conversation = new("conversacion");
    private static readonly EventKind Damage = new("dano");
    private static readonly EventKind Fire = Damage.Derive("fuego");

    private readonly BlockRegistry _blocks = new();
    private readonly World<ushort> _world = new(new ChunkShape(16, 16, 16));
    private readonly EventHub _hub = new(new EntityStore(new ChunkShape(16, 16, 16), new WorldScale(4, 3, 2, 2, 16, 16)));
    private readonly ushort _log, _stone;

    public BlockEventTests()
    {
        var logProto = new Prototype("tronco").Float("durability", 2f);
        var stoneProto = new Prototype("piedra").Float("durability", 3f);
        _log = _blocks.Register("log", BlockDef.Cube("log", 0) with { Props = logProto });
        _stone = _blocks.Register("stone", BlockDef.Cube("stone", 1) with { Props = stoneProto });
        _world.AddChunk(new Chunk<ushort>(new ChunkCoord(0, 0, 0), _world.Shape));

        // Solo el tronco arde (como en el juego): la piedra recibe el fuego y lo ignora.
        _hub.On<BlockRef>(logProto, Fire, (block, ev, _) =>
        {
            if (block.TryGetFloat("durability", out float d) && d - ev.Amount > 0f) block.Set("durability", d - ev.Amount);
            else block.Destroy();
        });
    }

    private void Place(ushort id, int x, int y, int z) => _world.SetCell(new CellCoord(x, y, z), id);
    private ushort At(int x, int y, int z) => _world.GetCell(new CellCoord(x, y, z));

    [Fact]
    public void Fire_IsProcessedOnlyByTheBlocksThatAcceptIt()
    {
        Place(_log, 5, 5, 5);
        Place(_stone, 6, 5, 5);

        int processed = BlockEvents.Broadcast(_hub, _world, _blocks, new CellCoord(5, 5, 5), 2, new GameEvent(Fire, Amount: 1f));

        Assert.Equal(1, processed);   // el tronco; la piedra lo recibió y lo ignoró
        Assert.Equal(1f, new BlockRef(_world, _blocks, new CellCoord(5, 5, 5)).GetFloat("durability"));   // chamuscado: 2 -> 1
        Assert.Equal(1, _world.CellOverrideCount);                                                         // y solo ese bloque guarda algo
        Assert.Equal(3f, new BlockRef(_world, _blocks, new CellCoord(6, 5, 5)).GetFloat("durability"));
    }

    [Fact]
    public void StrongFire_DestroysTheFlammableBlocks_AndLeavesTheRest()
    {
        Place(_log, 5, 5, 5);
        Place(_log, 5, 6, 5);
        Place(_stone, 4, 5, 5);

        int processed = BlockEvents.Broadcast(_hub, _world, _blocks, new CellCoord(5, 5, 5), 2, new GameEvent(Fire, Amount: 5f));

        Assert.Equal(2, processed);
        Assert.Equal(BlockRegistry.Air, At(5, 5, 5));
        Assert.Equal(BlockRegistry.Air, At(5, 6, 5));
        Assert.Equal(_stone, At(4, 5, 5));
        Assert.Equal(0, _world.CellOverrideCount);
    }

    [Fact]
    public void ATree_ReceivesAConversationButDoesNotProcessIt()
    {
        Place(_log, 5, 5, 5);
        var tree = new BlockRef(_world, _blocks, new CellCoord(5, 5, 5));
        Assert.Equal(DeliveryResult.Ignored, _hub.Send(tree, new GameEvent(Conversation)));
        Assert.False(_hub.Accepts(tree, Conversation));
        Assert.True(_hub.Accepts(tree, Fire));
        Assert.Equal("log", tree.Label);
    }

    [Fact]
    public void TheBlastIsSpherical()
    {
        Place(_log, 7, 5, 5);   // a 2 celdas del centro: dentro
        Place(_log, 7, 7, 5);   // a 2,8: fuera
        int processed = BlockEvents.Broadcast(_hub, _world, _blocks, new CellCoord(5, 5, 5), 2, new GameEvent(Fire, Amount: 5f));
        Assert.Equal(1, processed);
        Assert.Equal(_log, At(7, 7, 5));
    }
}
