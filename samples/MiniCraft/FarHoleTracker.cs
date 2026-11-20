using GF.Core;
using GF.World;
using GF.World.Map;
using GF.World.Voxel;

namespace MiniCraft;

/// <summary>
/// Agujero central del terreno lejano: el cuadrado de chunks con su MALLA ya construida alrededor de la cámara (no basta con que
/// sus datos estén cargados: hasta que no se ve, el terreno lejano sigue ahí), con un máximo de 'closeChunks'. Dentro no hay malla lejana; si los
/// chunks van por detrás (carga inicial, vuelo rápido), el agujero se encoge y el terreno lejano rellena lo que falte.
/// </summary>
internal sealed class FarHoleTracker
{
    private readonly VoxelWorldRenderer _renderer;
    private readonly World<ushort> _world;
    private readonly WorldScale _scale;
    private readonly int _verticalChunks, _closeChunks;
    private (int X, int Z, int Version) _key = (int.MinValue, 0, 0);   // para no recalcular si nada cambió
    private int _covered;

    public FarHoleTracker(VoxelWorldRenderer renderer, World<ushort> world, WorldScale scale, int verticalChunks, int closeChunks)
    {
        _renderer = renderer; _world = world; _scale = scale; _verticalChunks = verticalChunks; _closeChunks = closeChunks;
    }

    /// <summary>Radio (en chunks) del último agujero calculado.</summary>
    public int Radius { get; private set; }

    public FarHole Compute(Vec3d pos)
    {
        var cc = _world.Shape.ToChunk(new CellCoord(IntMath.FloorToInt(pos.X), 0, IntMath.FloorToInt(pos.Z)));
        var key = (cc.X, cc.Z, _renderer.VisibilityVersion);   // cambia al construirse una malla, terminar su fundido o descargarse
        if (key != _key)
        {
            _key = key;
            _covered = FarHoleBuilder.CoveredRadius(cc.X, cc.Z, _closeChunks, ColumnCovered);
        }

        Radius = Math.Min(_closeChunks, _covered);
        if (Radius < 0) { Radius = 0; return default; }
        return FarHoleBuilder.ForRadius(cc.X, cc.Z, Radius, _world.Shape.SizeX);
    }

    private bool ColumnCovered(int cx, int cz)
    {
        if (cz < 0 || cz >= _scale.ChunkCountZ) return true;   // más allá de los polos no hay nada que cubrir
        for (int cy = 0; cy < _verticalChunks; cy++)
            if (!_renderer.IsFullyVisible(new ChunkCoord(cx, cy, cz))) return false;   // aún sin malla o disolviéndose: no se puede quitar el lejano
        return true;
    }
}
