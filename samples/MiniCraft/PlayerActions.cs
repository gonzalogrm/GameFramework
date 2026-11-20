using GF.Core;
using GF.World;
using GF.World.Entities;
using GF.World.Events;
using GF.World.Voxel;
using GF.World.Map;

namespace MiniCraft;

/// <summary>Lo que el jugador hace sobre el mundo con el ratón y las teclas de acción: golpear, picar, hablar y lanzar bolas de fuego.</summary>
public sealed class PlayerActions
{
    private readonly EventHub _events;
    private readonly World<ushort> _world;
    private readonly WorldScale _scale;
    private readonly PlayerState _player;
    private readonly GrenadeKit? _grenades;
    private readonly Action<string> _message;

    public PlayerActions(EventHub events, World<ushort> world, WorldScale scale, PlayerState player, GrenadeKit? grenades, Action<string> message)
    {
        _events = events; _world = world; _scale = scale; _player = player; _grenades = grenades; _message = message;
    }

    /// <summary>
    /// Un golpe es un EVENTO de daño enviado a la entidad: lo procesa quien acepte "dano" (toda criatura) y baja su "hp";
    /// al llegar a 0 muere. Cualquier otra cosa que lo reciba lo ignora.
    /// </summary>
    public void Attack(Entity entity) => _events.Send(entity, new GameEvent(GameEvents.Melee, _player, _player.AttackPower));

    /// <summary>
    /// Cada golpe resta 1 a la durabilidad del bloque (stone 3, log 2, el resto 1). Solo el bloque golpeado guarda su valor nuevo;
    /// los demás comparten el del prototipo. Los sprites y otros tipos sin propiedades se rompen de un golpe.
    /// </summary>
    public void Mine(CellCoord cell)
    {
        var block = new BlockRef(_world, Blocks.Registry, cell);
        if (block.TryGetFloat("durability", out float durability) && durability > 1f)
        {
            block.Set("durability", durability - 1f);
            return;
        }
        ushort brokenId = _world.GetCell(cell);
        if (brokenId == Blocks.Fungus || brokenId == Blocks.SporeCap)
            _grenades?.ReleaseSpores(new Vec3d(cell.X + 0.5, cell.Y + 0.5, cell.Z + 0.5));
        _world.SetCell(cell, Blocks.Air);   // sustituir el bloque borra también sus cambios de instancia
    }

    /// <summary>Emite un evento de conversación al objetivo (entidad o bloque). Quien no lo acepte lo recibe y lo ignora.</summary>
    public void Talk(Entity? entity, VoxelHit? block)
    {
        var ev = new GameEvent(GameEvents.Conversation);
        if (entity != null) _events.Send(entity, ev);
        else if (block is { } t) _events.Send(new BlockRef(_world, Blocks.Registry, t.Cell), ev);
        else _message("No hay nadie a quien hablar: apunta a algo");
    }

    /// <summary>
    /// Emite una bola de fuego (3 de daño) sobre lo apuntado. La reciben TODAS las entidades a menos de 3 bloques y los bloques a menos
    /// de 2; cada una la procesa solo si su tipo acepta el fuego. Criaturas: sufren daño. Tronco y hojas: arden. El resto: la ignoran.
    /// </summary>
    public void CastFireball(Entity? entity, VoxelHit? block, Vec3d playerPos)
    {
        Vec3d center;
        if (entity != null)
        {
            // La posición de una entidad es canónica (X envuelta); los bloques usan las coordenadas cercanas al jugador.
            double x = playerPos.X + _scale.DeltaX(playerPos.X, entity.Position.X);
            center = new Vec3d(x, entity.Position.Y, entity.Position.Z);
        }
        else if (block is { } t) center = new Vec3d(t.Cell.X + 0.5, t.Cell.Y + 0.5, t.Cell.Z + 0.5);
        else { _message("Apunta a algo para lanzar la bola de fuego"); return; }

        var ev = new GameEvent(GameEvents.Fireball, Amount: 3f);
        var cell = new CellCoord((int)Math.Floor(center.X), (int)Math.Floor(center.Y), (int)Math.Floor(center.Z));
        int entities = _events.Broadcast(center, 3.0, ev);
        int blocks = BlockEvents.Broadcast(_events, _world, Blocks.Registry, cell, 2, ev);
        _message($"Bola de fuego: la procesan {entities} entidades y {blocks} bloques");
    }
}
