using GF.Core;
using Microsoft.Xna.Framework;

namespace GF.World.Voxel;

/// <summary>
/// Luz RGB de un volumen alrededor del chunk mallado: el chunk con un margen de 15 bloques por lado (el alcance máximo de la luz), no 3x3x3
/// chunks enteros. Coordenadas LOCALES al chunk central: de -margen a Size+margen-1.
/// Capas: cielo (Sky) y bloques emisores (Block). Se reutiliza por hilo: no conservar tras ExtractCenter.
/// </summary>
public sealed class LightField
{
    public const int Max = 15;

    private const int Margin = 15;
    internal readonly int Sx, Sy, Sz, Ox, Oy, Oz, Nx, Ny, Nz;   // Size, margen (= desplazamiento del chunk en el volumen), tamaño del volumen
    internal readonly ushort[] Sky, Block, Ids;
    internal readonly bool[] Solid;
    internal Vector3 SkyColor, Ambient;

    internal LightField(int sx, int sy, int sz)
    {
        Sx = sx; Sy = sy; Sz = sz;
        Ox = Math.Min(Margin, sx); Oy = Math.Min(Margin, sy); Oz = Math.Min(Margin, sz);
        Nx = sx + 2 * Ox; Ny = sy + 2 * Oy; Nz = sz + 2 * Oz;
        int n = Nx * Ny * Nz;
        Sky = new ushort[n]; Block = new ushort[n]; Ids = new ushort[n];
        Solid = new bool[n];
    }

    internal bool Matches(int sx, int sy, int sz) => Sx == sx && Sy == sy && Sz == sz;
    internal void Clear() { Array.Clear(Sky); Array.Clear(Block); }
    internal int Index(int vx, int vy, int vz) => (vy * Nz + vz) * Nx + vx;

    /// <summary>Nivel (0..15 por canal) en una celda: el mayor entre cielo (con su color), bloques y focos.</summary>
    public Vector3 Levels(int x, int y, int z)
    {
        int vx = x + Ox, vy = y + Oy, vz = z + Oz;
        if ((uint)vx >= (uint)Nx || (uint)vy >= (uint)Ny || (uint)vz >= (uint)Nz) return Vector3.Zero;
        int i = Index(vx, vy, vz);
        ushort s = Sky[i], b = Block[i];
        return new Vector3(
            Math.Max(Rgb4.R(s) * SkyColor.X, Rgb4.R(b)),
            Math.Max(Rgb4.G(s) * SkyColor.Y, Rgb4.G(b)),
            Math.Max(Rgb4.B(s) * SkyColor.Z, Rgb4.B(b)));
    }

    /// <summary>Copia las capas del chunk central (lo que se publica para consultas físicas).</summary>
    public LightChunk ExtractCenter()
    {
        var sky = new ushort[Sx * Sy * Sz];
        var block = new ushort[sky.Length];
        for (int y = 0; y < Sy; y++)
        for (int z = 0; z < Sz; z++)
        for (int x = 0; x < Sx; x++)
        {
            int src = Index(x + Ox, y + Oy, z + Oz), dst = (y * Sz + z) * Sx + x;
            sky[dst] = Sky[src]; block[dst] = Block[src];
        }
        return new LightChunk(sky, block);
    }
}

/// <summary>
/// Iluminación por voxel en color. Se calcula en el hilo de mallado sobre el ChunkSnapshot (26 vecinos).
///  - CIELO: 15 en cada columna abierta, bajando sin pérdida; el vidrio de color tiñe el haz (cada canal se multiplica por el filtro).
///  - BLOQUES: emisores de color (BlockDef.Emission + LightColor), flood-fill por canal; el vidrio los filtra igual.
/// </summary>
public static class ChunkLighting
{
    private static readonly int[] Dx = { 1, -1, 0, 0, 0, 0 };
    private static readonly int[] Dy = { 0, 0, 1, -1, 0, 0 };
    private static readonly int[] Dz = { 0, 0, 0, 0, 1, -1 };
    private static readonly float[] Curve = BuildCurve();

    [ThreadStatic] private static LightField? t_field;

    private static float[] BuildCurve()
    {
        var c = new float[16];
        for (int i = 0; i < 16; i++) c[i] = MathF.Pow(0.82f, 15 - i);   // cada nivel atenúa un 18 %
        return c;
    }

    public static LightField Compute(ChunkSnapshot<ushort> snap, BlockTable table, LightEnvironment env)
    {
        var s = snap.Shape;
        int sx = s.SizeX, sy = s.SizeY, sz = s.SizeZ;
        var f = t_field;
        if (f == null || !f.Matches(sx, sy, sz)) t_field = f = new LightField(sx, sy, sz);
        else f.Clear();
        f.SkyColor = env.SkyColor;
        f.Ambient = env.Ambient;

        int nx = f.Nx, ny = f.Ny, nz = f.Nz, ox = f.Ox, oy = f.Oy, oz = f.Oz;
        var skyQ = new Queue<int>(4096);
        var blockQ = new Queue<int>(256);

        for (int vy = 0; vy < ny; vy++)
        for (int vz = 0; vz < nz; vz++)
        for (int vx = 0; vx < nx; vx++)
        {
            int i = (vy * nz + vz) * nx + vx;
            ushort id = snap.Get(vx - ox, vy - oy, vz - oz);
            f.Ids[i] = id;
            f.Solid[i] = table.Opaque[id];
            ushort e = table.EmitRgb[id];
            if (e != 0) { f.Block[i] = e; blockQ.Enqueue(i); }
        }

        // Cielo: cada columna recibe 15 desde arriba hasta el primer bloque opaco; el vidrio de color filtra el haz.
        for (int vz = 0; vz < nz; vz++)
        for (int vx = 0; vx < nx; vx++)
        {
            int r = 15, g = 15, b = 15;
            for (int vy = ny - 1; vy >= 0; vy--)
            {
                int i = (vy * nz + vz) * nx + vx;
                if (f.Solid[i]) break;
                ushort id = f.Ids[i];
                r = r * table.FilterR[id] / 255; g = g * table.FilterG[id] / 255; b = b * table.FilterB[id] / 255;
                if ((r | g | b) == 0) break;
                f.Sky[i] = Rgb4.Pack(r, g, b);
            }
        }

        // Solo se propaga desde las celdas de cielo con un vecino horizontal más oscuro: el resto de una columna abierta ya está al máximo
        // (antes se encolaba todo el aire abierto: cientos de miles de celdas por chunk).
        for (int vy = 0; vy < ny; vy++)
        for (int vz = 0; vz < nz; vz++)
        for (int vx = 0; vx < nx; vx++)
        {
            int i = (vy * nz + vz) * nx + vx;
            ushort v = f.Sky[i];
            if (v == 0) continue;
            if ((vx > 0 && Dimmer(f, i - 1, v)) || (vx < nx - 1 && Dimmer(f, i + 1, v)) ||
                (vz > 0 && Dimmer(f, i - nx, v)) || (vz < nz - 1 && Dimmer(f, i + nx, v)))
                skyQ.Enqueue(i);
        }

        Flood(f, table, f.Sky, skyQ, sky: true);
        Flood(f, table, f.Block, blockQ, sky: false);
        return f;
    }

    /// <summary>¿Recibiría el vecino n más cielo desde una celda con la luz v? (aproximado: ignora el filtro, así que nunca se omite nada necesario)</summary>
    private static bool Dimmer(LightField f, int n, ushort v)
    {
        if (f.Solid[n]) return false;
        ushort w = f.Sky[n];
        return Rgb4.R(v) - 1 > Rgb4.R(w) || Rgb4.G(v) - 1 > Rgb4.G(w) || Rgb4.B(v) - 1 > Rgb4.B(w);
    }

    private static void Flood(LightField f, BlockTable table, ushort[] light, Queue<int> q, bool sky)
    {
        int nx = f.Nx, ny = f.Ny, nz = f.Nz, strideY = nx * nz;
        while (q.Count > 0)
        {
            int i = q.Dequeue();
            ushort v = light[i];
            int r = Rgb4.R(v), g = Rgb4.G(v), b = Rgb4.B(v);
            if (r <= 1 && g <= 1 && b <= 1) continue;
            int x = i % nx, z = (i / nx) % nz, y = i / strideY;

            for (int d = 0; d < 6; d++)
            {
                int px = x + Dx[d], py = y + Dy[d], pz = z + Dz[d];
                if ((uint)px >= (uint)nx || (uint)py >= (uint)ny || (uint)pz >= (uint)nz) continue;
                int n = (py * nz + pz) * nx + px;
                if (f.Solid[n]) continue;

                ushort id = f.Ids[n];
                bool down = sky && d == 3;   // la luz de cielo a pleno brillo baja sin perder nivel
                int nr = Math.Max(0, down && r == 15 ? r : r - 1) * table.FilterR[id] / 255;
                int ng = Math.Max(0, down && g == 15 ? g : g - 1) * table.FilterG[id] / 255;
                int nb = Math.Max(0, down && b == 15 ? b : b - 1) * table.FilterB[id] / 255;

                ushort cur = light[n];
                int cr = Rgb4.R(cur), cg = Rgb4.G(cur), cb = Rgb4.B(cur);
                if (nr <= cr && ng <= cg && nb <= cb) continue;
                light[n] = Rgb4.Pack(Math.Max(nr, cr), Math.Max(ng, cg), Math.Max(nb, cb));
                q.Enqueue(n);
            }
        }
    }

    private static float Shade(float level, float ambient)
    {
        int i = (int)level;
        float t = level - i;
        float v = i >= 15 ? Curve[15] : Curve[i] + (Curve[i + 1] - Curve[i]) * t;
        return MathF.Max(ambient, v);
    }

    /// <summary>
    /// Brillo RGB (0..1) de un vértice: media de la luz de la celda que mira la cara y de las 2 laterales y la esquina que no sean opacas
    /// (las mismas muestras que el AO), con el brillo mínimo ambiental como suelo.
    /// </summary>
    public static Vector3 VertexBrightness(LightField light, ChunkSnapshot<ushort> snap, BlockTable table, int bx, int by, int bz,
        (int X, int Y, int Z) u, (int X, int Y, int Z) v, int su, int sv)
    {
        var sum = light.Levels(bx, by, bz);
        int n = 1;

        int ax = bx + su * u.X, ay = by + su * u.Y, az = bz + su * u.Z;
        bool a = !table.Opaque[snap.Get(ax, ay, az)];
        int cx = bx + sv * v.X, cy = by + sv * v.Y, cz = bz + sv * v.Z;
        bool b = !table.Opaque[snap.Get(cx, cy, cz)];

        if (a) { sum += light.Levels(ax, ay, az); n++; }
        if (b) { sum += light.Levels(cx, cy, cz); n++; }
        if (a || b)
        {
            int ex = ax + sv * v.X, ey = ay + sv * v.Y, ez = az + sv * v.Z;
            if (!table.Opaque[snap.Get(ex, ey, ez)]) { sum += light.Levels(ex, ey, ez); n++; }
        }

        sum /= n;
        var amb = light.Ambient;
        return new Vector3(Shade(sum.X, amb.X), Shade(sum.Y, amb.Y), Shade(sum.Z, amb.Z));
    }
}
