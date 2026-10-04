using GF.Core;

namespace GF.World.Entities;

/// <summary>
/// Dormant: no se simula (solo se guarda). Approximate: no hay chunk cargado, pero tiene destino y avanza en línea recta
/// con un reloj grueso. Active: su columna de chunks está cargada; el juego la simula con todo detalle.
/// </summary>
public enum SimulationTier { Dormant, Approximate, Active }

/// <summary>Definición de un tipo de entidad. Name es la clave del registro y también se usa al guardar.</summary>
public sealed record EntityDef(string Name, double DefaultSpeed, byte R, byte G, byte B, float Width = 0.6f, float Height = 1.8f);

/// <summary>Entidad serializable (el estado de ejecución, Entity.Data, no se guarda).</summary>
public sealed record EntityRecord(long Id, string Type, double X, double Y, double Z,
    double? DestX, double? DestY, double? DestZ, double Speed, string? Tag);

/// <summary>
/// Una entidad del mundo. La posición solo cambia a través de EntityStore.Move (para mantener los índices
/// espaciales coherentes); las coordenadas son canónicas: X siempre en [0, ancho del mundo).
/// </summary>
public sealed class Entity
{
    internal Entity(long id, ushort typeId)
    {
        Id = id;
        TypeId = typeId;
    }

    public long Id { get; }
    public ushort TypeId { get; }
    public Vec3d Position { get; internal set; }
    public Vec3d? Destination { get; internal set; }
    public double Speed { get; set; }
    public SimulationTier Tier { get; internal set; }

    /// <summary>Dato pequeño serializable definido por el juego (profesión, facción...).</summary>
    public string? Tag { get; set; }
    /// <summary>Estado de ejecución del juego (IA, física...). No se guarda; se crea en Activated y se descarta en Deactivated.</summary>
    public object? Data { get; set; }

    internal double LastSim;
    internal ChunkCoord Column;     // columna de chunks (X canónica, Y = 0, Z)
    internal (int X, int Z) Region;
}
