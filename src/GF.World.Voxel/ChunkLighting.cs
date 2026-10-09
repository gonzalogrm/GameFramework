using GF.Core;

namespace GF.World.Voxel;

/// <summary>
/// Luz (0..15) de un volumen de 3x3x3 chunks centrado en el chunk mallado. Coordenadas LOCALES al chunk central,
/// admite desde -Size hasta 2*Size-1 en cada eje. Fuera de rango devuelve 0.
/// </summary>
public sealed class LightField
{
    public const int Max = 15;

    internal readonly byte[] Sky, Block;
    private readonly int _sx, _sy, _sz, _nx, _nz;

    internal LightField(int sx, int sy, int sz)
    {
        _sx = sx; _sy = sy; _sz = sz;
        _nx = 3 * sx; _nz = 3 * sz;
        Sky = new byte[3 * sx * 3 * sy * 3 * sz];
        Block = new byte[Sky.Length];
    }

    /// <summary>Luz efectiva (la mayor entre cielo y bloque) en una celda.</summary>
    public int Get(int x, int y, int z)
    {
        int vx = x + _sx, vy = y + _sy, vz = z + _sz;
        if ((uint)vx >= (uint)_nx || (uint)vy >= (uint)(3 * _sy) || (uint)vz >= (uint)_nz) return 0;
        int i = (vy * _nz + vz) * _nx + vx;
        return Math.Max(Sky[i], Block[i]);
    }
}

/// <summary>
/// Iluminación por voxel con flood-fill (BFS): luz de cielo y luz de bloques emisores (BlockDef.Emission).
/// Se calcula en el hilo de mallado sobre el ChunkSnapshot (que ya incluye los 26 vecinos), así que es exacta para
/// fuentes dentro de ese volumen (con chunks >= 16 la luz de 15 niveles nunca necesita más).
/// Limitación: la luz de cielo se siembra desde lo alto del volumen; un techo a más de 1 chunk por encima no se ve.
/// </summary>
public static class ChunkLighting
{
    private static readonly int[] Dx = { 1, -1, 0, 0, 0, 0 };
    private static readonly int[] Dy = { 0, 0, 1, -1, 0, 0 };
    private static readonly int[] Dz = { 0, 0, 0, 0, 1, -1 };

    public static LightField Compute(ChunkSnapshot<ushort> snap, BlockTable table)
    {
        var s = snap.Shape;
        int sx = s.SizeX, sy = s.SizeY, sz = s.SizeZ;
        int nx = 3 * sx, ny = 3 * sy, nz = 3 * sz;
        var field = new LightField(sx, sy, sz);
        var solid = new bool[nx * ny * nz];
        var skyQ = new Queue<int>(4096);
        var blockQ = new Queue<int>(256);

        for (int vy = 0; vy < ny; vy++)
        for (int vz = 0; vz < nz; vz++)
        for (int vx = 0; vx < nx; vx++)
        {
            int i = (vy * nz + vz) * nx + vx;
            ushort id = snap.Get(vx - sx, vy - sy, vz - sz);
            solid[i] = table.Opaque[id];
            byte e = table.Emission[id];
            if (e > 0) { field.Block[i] = e; blockQ.Enqueue(i); }
        }

        // Cielo: cada columna recibe 15 desde arriba hasta el primer bloque opaco.
        for (int vz = 0; vz < nz; vz++)
        for (int vx = 0; vx < nx; vx++)
            for (int vy = ny - 1; vy >= 0; vy--)
            {
                int i = (vy * nz + vz) * nx + vx;
                if (solid[i]) break;
                field.Sky[i] = LightField.Max;
                skyQ.Enqueue(i);
            }

        Flood(field.Sky, solid, skyQ, nx, ny, nz, sky: true);
        Flood(field.Block, solid, blockQ, nx, ny, nz, sky: false);
        return field;
    }

    private static void Flood(byte[] light, bool[] solid, Queue<int> q, int nx, int ny, int nz, bool sky)
    {
        int strideY = nx * nz;
        while (q.Count > 0)
        {
            int i = q.Dequeue();
            int l = light[i];
            if (l <= 1) continue;
            int x = i % nx, z = (i / nx) % nz, y = i / strideY;

            for (int d = 0; d < 6; d++)
            {
                int px = x + Dx[d], py = y + Dy[d], pz = z + Dz[d];
                if ((uint)px >= (uint)nx || (uint)py >= (uint)ny || (uint)pz >= (uint)nz) continue;
                int n = (py * nz + pz) * nx + px;
                if (solid[n]) continue;

                // La luz de cielo a pleno brillo baja sin perder nivel.
                int nl = sky && d == 3 && l == LightField.Max ? l : l - 1;
                if (nl <= light[n]) continue;
                light[n] = (byte)nl;
                q.Enqueue(n);
            }
        }
    }

    /// <summary>
    /// Brillo 0..1 de un vértice: media de la luz de la celda que mira la cara y de las 2 laterales y la esquina que no sean opacas
    /// (las mismas muestras que el AO). Cada nivel de luz atenúa un 20 %.
    /// </summary>
    public static float VertexBrightness(LightField light, ChunkSnapshot<ushort> snap, BlockTable table, int bx, int by, int bz,
        (int X, int Y, int Z) u, (int X, int Y, int Z) v, int su, int sv)
    {
        int sum = light.Get(bx, by, bz), n = 1;

        int ax = bx + su * u.X, ay = by + su * u.Y, az = bz + su * u.Z;
        bool a = !table.Opaque[snap.Get(ax, ay, az)];
        int cx = bx + sv * v.X, cy = by + sv * v.Y, cz = bz + sv * v.Z;
        bool b = !table.Opaque[snap.Get(cx, cy, cz)];

        if (a) { sum += light.Get(ax, ay, az); n++; }
        if (b) { sum += light.Get(cx, cy, cz); n++; }
        if (a || b)
        {
            int ex = ax + sv * v.X, ey = ay + sv * v.Y, ez = az + sv * v.Z;
            if (!table.Opaque[snap.Get(ex, ey, ez)]) { sum += light.Get(ex, ey, ez); n++; }
        }

		//return MathF.Pow(0.8f, LightField.Max - sum / (float)n);
		const float Ambient = 0.20f;   // brillo mínimo: sube/baja a gusto
		return Math.Max(Ambient, MathF.Pow(0.85f, LightField.Max - sum / (float)n));
	}
}
