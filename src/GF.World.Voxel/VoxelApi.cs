using GF.Core;
using Microsoft.Xna.Framework;

namespace GF.World.Voxel;

/// <summary>
/// Fachada sencilla para operar sobre el mundo de bloques: coordenadas sueltas (x, y, z) y bloques por NOMBRE, sin CellCoord, BlockRegistry ni ids.
///     var api = new VoxelApi(world, Blocks.Registry);
///     api.Set(10, 70, 5, "stone");
///     api.Fill(new CellCoord(0, 64, 0), new CellCoord(9, 66, 9), "glass_blue");
///     api.Sphere(new CellCoord(50, 70, 50), 4, "air");              // cavidad
///     int y = api.TopSolidY(10, 5, maxY: 127);                      // suelo bajo una columna (-1 si no hay chunk o suelo)
/// Solo hilo principal (usa World). Solo afecta a chunks CARGADOS: lo demás se ignora y no cuenta en los resultados.
/// Cada celda cambiada dispara los eventos normales del mundo (remallado, luz, guardado): los volúmenes enormes en un solo frame son caros.
/// </summary>
public sealed class VoxelApi
{
    public VoxelApi(World<ushort> world, BlockRegistry blocks)
    {
        World = world;
        Blocks = blocks;
    }

    public World<ushort> World { get; }
    public BlockRegistry Blocks { get; }

    // ------------------------------------------------------------------ nombres e ids

    /// <summary>Id de un bloque por nombre. Lanza un error con el nombre pedido si no existe.</summary>
    public ushort Id(string name) =>
        Blocks.TryGetId(name, out ushort id) ? id : throw new KeyNotFoundException($"No existe el bloque '{name}'.");

    public bool TryId(string name, out ushort id) => Blocks.TryGetId(name, out id);

    public BlockDef Def(ushort id) => Blocks.Get(id);

    // ------------------------------------------------------------------ lectura

    public ushort Get(int x, int y, int z) => World.GetCell(new CellCoord(x, y, z));
    public ushort Get(CellCoord c) => World.GetCell(c);

    public string NameAt(int x, int y, int z) => Blocks.Get(Get(x, y, z)).Name;

    /// <summary>¿Está cargado el chunk que contiene esta celda?</summary>
    public bool IsLoaded(int x, int y, int z) => World.GetChunk(World.Shape.ToChunk(new CellCoord(x, y, z))) != null;

    public bool IsAir(int x, int y, int z) => Get(x, y, z) == BlockRegistry.Air;
    public bool IsSolid(int x, int y, int z) => Blocks.Get(Get(x, y, z)).Solid;
    public bool Is(int x, int y, int z, string name) => Get(x, y, z) == Id(name);

    // ------------------------------------------------------------------ escritura

    /// <summary>Pone un bloque. false si el chunk no está cargado. Si ya era ese bloque no hace nada (y no dispara eventos).</summary>
    public bool Set(int x, int y, int z, ushort id)
    {
        var c = new CellCoord(x, y, z);
        if (World.GetChunk(World.Shape.ToChunk(c)) == null) return false;
        if (World.GetCell(c) == id) return true;
        return World.SetCell(c, id);
    }

    public bool Set(int x, int y, int z, string name) => Set(x, y, z, Id(name));
    public bool Set(CellCoord c, string name) => Set(c.X, c.Y, c.Z, Id(name));

    /// <summary>Rompe el bloque (lo deja en aire).</summary>
    public bool Break(int x, int y, int z) => Set(x, y, z, BlockRegistry.Air);

    // ------------------------------------------------------------------ volúmenes (devuelven cuántas celdas CAMBIARON)

    /// <summary>Todas las celdas de la caja entre dos esquinas (inclusive, en cualquier orden).</summary>
    public static IEnumerable<CellCoord> Cells(CellCoord a, CellCoord b)
    {
        int x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X);
        int y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
        int z0 = Math.Min(a.Z, b.Z), z1 = Math.Max(a.Z, b.Z);
        for (int y = y0; y <= y1; y++)
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
            yield return new CellCoord(x, y, z);
    }

    /// <summary>Rellena una caja con un bloque.</summary>
    public int Fill(CellCoord a, CellCoord b, ushort id) => Apply(Cells(a, b), id);
    public int Fill(CellCoord a, CellCoord b, string name) => Fill(a, b, Id(name));

    /// <summary>Solo las paredes, suelo y techo de la caja (el interior no se toca).</summary>
    public int Shell(CellCoord a, CellCoord b, ushort id)
    {
        int x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X);
        int y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
        int z0 = Math.Min(a.Z, b.Z), z1 = Math.Max(a.Z, b.Z);
        return Apply(Cells(a, b).Where(c => c.X == x0 || c.X == x1 || c.Y == y0 || c.Y == y1 || c.Z == z0 || c.Z == z1), id);
    }

    public int Shell(CellCoord a, CellCoord b, string name) => Shell(a, b, Id(name));

    /// <summary>Cambia 'from' por 'to' dentro de la caja (el resto de bloques no se toca).</summary>
    public int Replace(CellCoord a, CellCoord b, ushort from, ushort to) =>
        Apply(Cells(a, b).Where(c => World.GetCell(c) == from), to);

    public int Replace(CellCoord a, CellCoord b, string from, string to) => Replace(a, b, Id(from), Id(to));

    /// <summary>Esfera maciza de 'radius' bloques.</summary>
    public int Sphere(CellCoord center, int radius, ushort id)
    {
        int r2 = radius * radius;
        return Apply(Cells(center.Offset(-radius, -radius, -radius), center.Offset(radius, radius, radius))
            .Where(c => Dist2(c, center) <= r2), id);
    }

    public int Sphere(CellCoord center, int radius, string name) => Sphere(center, radius, Id(name));

    /// <summary>Cilindro vertical (eje Y) desde y0 hasta y1, ambos inclusive.</summary>
    public int Cylinder(int cx, int cz, int radius, int y0, int y1, ushort id)
    {
        int r2 = radius * radius;
        return Apply(Cells(new CellCoord(cx - radius, y0, cz - radius), new CellCoord(cx + radius, y1, cz + radius))
            .Where(c => (c.X - cx) * (c.X - cx) + (c.Z - cz) * (c.Z - cz) <= r2), id);
    }

    public int Cylinder(int cx, int cz, int radius, int y0, int y1, string name) => Cylinder(cx, cz, radius, y0, y1, Id(name));

    /// <summary>Línea recta entre dos celdas (Bresenham 3D).</summary>
    public int Line(CellCoord a, CellCoord b, ushort id) => Apply(LineCells(a, b), id);
    public int Line(CellCoord a, CellCoord b, string name) => Line(a, b, Id(name));

    /// <summary>Cuántas celdas de la caja son de este bloque.</summary>
    public int Count(CellCoord a, CellCoord b, ushort id) => Cells(a, b).Count(c => World.GetCell(c) == id);
    public int Count(CellCoord a, CellCoord b, string name) => Count(a, b, Id(name));

    // ------------------------------------------------------------------ consultas del terreno

    /// <summary>
    /// Y de la celda sólida más alta de la columna (x, z), buscando de maxY hacia minY. -1 si no hay ninguna (o no hay chunks cargados).
    /// Los sprites (hierba, flores) no cuentan: solo bloques sólidos.
    /// </summary>
    public int TopSolidY(int x, int z, int maxY, int minY = 0)
    {
        for (int y = maxY; y >= minY; y--)
            if (Blocks.Get(World.GetCell(new CellCoord(x, y, z))).Solid) return y;
        return -1;
    }

    /// <summary>Primer bloque apuntado por un rayo (solo seleccionables: sólidos y sprites).</summary>
    public bool Raycast(Vec3d origin, Vector3 direction, float maxDistance, out VoxelHit hit) =>
        VoxelRaycaster.Raycast(World, Blocks, origin, direction, maxDistance, out hit);

    /// <summary>¿Cabe un cuerpo (ancho, alto, fondo; pies en pos) sin chocar con bloques sólidos?</summary>
    public bool Fits(Vec3d feet, Vector3 size) => !VoxelPhysics.Overlaps(World, Blocks, feet, size);

    // ------------------------------------------------------------------ internos

    private int Apply(IEnumerable<CellCoord> cells, ushort id)
    {
        int changed = 0;
        foreach (var c in cells)
        {
            if (World.GetChunk(World.Shape.ToChunk(c)) == null || World.GetCell(c) == id) continue;
            if (World.SetCell(c, id)) changed++;
        }
        return changed;
    }

    private static long Dist2(CellCoord a, CellCoord b)
    {
        long dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    private static IEnumerable<CellCoord> LineCells(CellCoord a, CellCoord b)
    {
        int dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y), dz = Math.Abs(b.Z - a.Z);
        int sx = a.X < b.X ? 1 : -1, sy = a.Y < b.Y ? 1 : -1, sz = a.Z < b.Z ? 1 : -1;
        int n = Math.Max(dx, Math.Max(dy, dz));
        int ex = n / 2, ey = n / 2, ez = n / 2;
        int x = a.X, y = a.Y, z = a.Z;
        for (int i = 0; i <= n; i++)
        {
            yield return new CellCoord(x, y, z);
            if ((ex += dx) >= n) { ex -= n; x += sx; }
            if ((ey += dy) >= n) { ey -= n; y += sy; }
            if ((ez += dz) >= n) { ez -= n; z += sz; }
        }
    }
}
