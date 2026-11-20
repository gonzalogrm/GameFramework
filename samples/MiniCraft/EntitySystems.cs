using GF.Core;
using GF.World;
using GF.World.Entities;
using GF.World.Events;
using GF.World.Map;

namespace MiniCraft;

/// <summary>
/// Entidades de un mundo: almacén con índice espacial, eventos, IA de los NPC y todos los spawners (enemigos y fauna, ciudades, ruinas, campos de estatuas).
/// Los spawners se enganchan solos al mundo (ChunkLoaded): basta con mantenerlos vivos aquí.
/// </summary>
internal sealed class EntitySystems
{
    private readonly CitySpawner _citySpawner;
    private readonly RuinSpawner _ruinSpawner;
    private readonly FieldSpawner _fieldSpawner;

    public EntityStore Entities { get; }
    public EventHub Events { get; }
    public NpcSystem Npcs { get; }
    public EnemySpawner Enemies { get; }

    /// <param name="map">El mapamundi (null mientras se calcula).</param>
    /// <param name="playerPos">Posición del jugador, o null si aún no se puede aparecer (cargando).</param>
    public EntitySystems(World<ushort> world, WorldTerrain terrain, WorldSettings settings, WorldScale scale, int seed, PlayerState player,
        Func<World<MapCell>?> map, Func<Vec3d?> playerPos)
    {
        var climate = terrain.Climate;

        // Entidades: índice espacial + IA. El almacén se engancha al mundo para activarse al cargar chunks.
        Entities = new EntityStore(world.Shape, scale);
        Entities.Attach(world);
        Entities.Definitions = EntityTypes.Registry;   // cada entidad remite a su tipo y a su prototipo de propiedades

        // Eventos: cada prototipo declara cuáles procesa (GameEvents.Register). Los manejadores avisan al jugador con Notify.
        Events = new EventHub(Entities);
        GameEvents.Register(Events);
        CityNpcs.Register(Events);
        Npcs = new NpcSystem(Entities, world, climate, scale, map, seed);
        Entities.Items = ItemTypes.Registry;   // guardar el inventario y el equipo de las entidades (por nombre de objeto)
        Npcs.Player = player;                  // los enemigos persiguen y golpean al jugador
        Npcs.Events = Events;
        Enemies = new EnemySpawner(Entities, world, climate, scale, Npcs, seed, settings, playerPos);
        _citySpawner = new CitySpawner(Entities, world, terrain.Cities, Npcs, scale, settings.Cities);
        _ruinSpawner = new RuinSpawner(Entities, world, terrain.Cities, scale, settings.Ruins);
        _fieldSpawner = new FieldSpawner(Entities, world, terrain.Cities, scale, terrain.Columns, seed, climate.SeaLevel, settings.Ruins);   // hallazgos en campos kilométricos
    }
}
