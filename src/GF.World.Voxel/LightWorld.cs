using GF.Core;
using Microsoft.Xna.Framework;

namespace GF.World.Voxel;

/// <summary>
/// Mapa de luz del mundo, con presencia física: se puede MEDIR (Sample / Level) en cualquier celda cargada y se usa para disparar
/// eventos (LightWatcher). Capas por celda y canal RGB:
///   Ambiente  luz mínima global (Ambient).
///   Estática  cielo (SkyColor) y bloques emisores de color; la calcula el mallador y se publica aquí al construir cada chunk.
/// Solo hilo principal. Los datos de un chunk existen cuando su malla está construida; un chunk de puro aire sin malla se
/// considera a cielo abierto; en otro caso Known = false.
/// </summary>
public sealed class LightWorld
{
    private readonly World<ushort> _world;
    private readonly Dictionary<ChunkCoord, LightChunk> _chunks = new();
    private Vector3 _ambient = new(0.12f), _sky = Vector3.One;
    private LightEnvironment _env = LightEnvironment.Default;

    public LightWorld(World<ushort> world) => _world = world;

    /// <summary>Cambió el ambiente o el color del cielo: hay que remallar todo.</summary>
    public event Action? EnvironmentChanged;

    /// <summary>Captura inmutable para los hilos de mallado.</summary>
    public LightEnvironment Environment => _env;
    public int MeasuredChunks => _chunks.Count;

    /// <summary>Brillo mínimo por canal (0..1). Remalla todo al cambiar.</summary>
    public Vector3 Ambient
    {
        get => _ambient;
        set { if (_ambient == value) return; _ambient = value; Rebuild(); }
    }

    /// <summary>Color de la luz del cielo (0..1 por canal): blanco = día, azul tenue = noche. Remalla todo al cambiar.</summary>
    public Vector3 SkyColor
    {
        get => _sky;
        set { if (_sky == value) return; _sky = value; Rebuild(); }
    }

    private void Rebuild()
    {
        _env = new LightEnvironment(_ambient, _sky);
        EnvironmentChanged?.Invoke();
    }

    // ------------------------------------------------------------------ datos del mallador

    /// <summary>El renderer publica la luz de un chunk al subir su malla (null = sin datos).</summary>
    public void Publish(ChunkCoord c, LightChunk? data)
    {
        if (data == null) _chunks.Remove(c);
        else _chunks[c] = data;
    }

    public void Forget(ChunkCoord c) => _chunks.Remove(c);

    // ------------------------------------------------------------------ medición

    /// <summary>Luz medida en una celda (coordenadas de los chunks cargados), por capas y canal.</summary>
    public LightLevels Sample(CellCoord cell)
    {
        var shape = _world.Shape;
        var cc = shape.ToChunk(cell, out int lx, out int ly, out int lz);
        var amb = _ambient * LightField.Max;

        if (_chunks.TryGetValue(cc, out var data))
        {
            int i = shape.Index(lx, ly, lz);
            ushort s = data.Sky[i], b = data.Block[i];
            return new LightLevels(amb,
                new Vector3(Rgb4.R(s), Rgb4.G(s), Rgb4.B(s)) * _sky,
                new Vector3(Rgb4.R(b), Rgb4.G(b), Rgb4.B(b)), true);
        }

        var chunk = _world.GetChunk(cc);
        if (chunk != null && chunk.Cells.AsSpan().IndexOfAnyExcept(BlockRegistry.Air) < 0)
            return new LightLevels(amb, _sky * LightField.Max, Vector3.Zero, true);   // aire sin malla: cielo abierto
        return new LightLevels(amb, Vector3.Zero, Vector3.Zero, false);
    }

    /// <summary>Nivel de luz 0..15 de una celda (luminancia del total). 0 si aún no se conoce.</summary>
    public float Level(CellCoord cell) => Sample(cell).Level;

    /// <summary>Nivel de luz en una posición (por ejemplo, la de una entidad o el jugador).</summary>
    public float Level(Vec3d position) =>
        Level(new CellCoord(IntMath.FloorToInt(position.X), IntMath.FloorToInt(position.Y + 0.5), IntMath.FloorToInt(position.Z)));
}
