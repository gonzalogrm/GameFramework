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

    /// <summary>Niveles (0..15 por canal) de cielo y de bloques en una celda, por separado: el cielo se escala luego por la hora del día.</summary>
    public void Levels(int x, int y, int z, out Vector3 sky, out Vector3 block)
    {
        int vx = x + Ox, vy = y + Oy, vz = z + Oz;
        if ((uint)vx >= (uint)Nx || (uint)vy >= (uint)Ny || (uint)vz >= (uint)Nz) { sky = block = Vector3.Zero; return; }
        int i = Index(vx, vy, vz);
        ushort s = Sky[i], b = Block[i];
        sky = new Vector3(Rgb4.R(s), Rgb4.G(s), Rgb4.B(s));
        block = new Vector3(Rgb4.R(b), Rgb4.G(b), Rgb4.B(b));
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
        return new LightChunk(RleU16.Encode(sky), RleU16.Encode(block));
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
    /// <summary>Curva nivel de luz (0..15) -> brillo (0..1): (nivel/15)^Gamma. Más suave que la exponencial anterior: los escalones entre bloques son menores y los bordes de la luz no se notan tan duros.</summary>
    private const float Gamma = 2.0f;
    private static readonly float[] Curve = BuildCurve();

    [ThreadStatic] private static LightField? t_field;
    [ThreadStatic] private static Queue<int>? t_skyQ, t_blockQ;   // colas reutilizadas por hilo

    private static float[] BuildCurve()
    {
        var c = new float[16];
        for (int i = 0; i < 16; i++) c[i] = MathF.Pow(i / 15f, Gamma);   // el nivel 0 es negro (el suelo lo pone el ambiente)
        return c;
    }

    public static LightField Compute(ChunkSnapshot<ushort> snap, BlockTable table, LightEnvironment env)
    {
        var s = snap.Shape;
        int sx = s.SizeX, sy = s.SizeY, sz = s.SizeZ;
        var f = t_field;
        if (f == null || !f.Matches(sx, sy, sz)) t_field = f = new LightField(sx, sy, sz);
        else f.Clear();

        int nx = f.Nx, ny = f.Ny, nz = f.Nz, ox = f.Ox, oy = f.Oy, oz = f.Oz;
        var skyQ = t_skyQ ??= new Queue<int>(4096);
        var blockQ = t_blockQ ??= new Queue<int>(256);
        skyQ.Clear();
        blockQ.Clear();

        // Ids del volumen copiados por filas (antes: una llamada a snap.Get por celda, ~240.000 por chunk).
        snap.CopyVolume(f.Ids, ox, oy, oz);
        var ids = f.Ids; var solid = f.Solid; var opaque = table.Opaque; var emit = table.EmitRgb;
        for (int i = 0; i < ids.Length; i++)
        {
            ushort id = ids[i];
            solid[i] = opaque[id];
            ushort e = emit[id];
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
            int m = Math.Max(r, Math.Max(g, b));   // canal más fuerte
            if (m <= 1) continue;
            int x = i % nx, z = (i / nx) % nz, y = i / strideY;

            for (int d = 0; d < 6; d++)
            {
                int px = x + Dx[d], py = y + Dy[d], pz = z + Dz[d];
                if ((uint)px >= (uint)nx || (uint)py >= (uint)ny || (uint)pz >= (uint)nz) continue;
                int n = (py * nz + pz) * nx + px;
                if (f.Solid[n]) continue;

                ushort id = f.Ids[n];
                bool down = sky && d == 3;   // la luz de cielo a pleno brillo baja sin perder nivel
                // El canal más fuerte pierde un nivel y los demás en la misma PROPORCIÓN: restar 1 a cada canal por separado desplazaba el
                // tono hacia el canal dominante (la luz cálida acababa roja y saturada). Así el color de la lámpara se conserva con la distancia.
                int nr, ng, nb;
                if (down && m == LightField.Max) { nr = r; ng = g; nb = b; }
                else { nr = (r * (m - 1) + m / 2) / m; ng = (g * (m - 1) + m / 2) / m; nb = (b * (m - 1) + m / 2) / m; }
                nr = nr * table.FilterR[id] / 255; ng = ng * table.FilterG[id] / 255; nb = nb * table.FilterB[id] / 255;

                ushort cur = light[n];
                int cr = Rgb4.R(cur), cg = Rgb4.G(cur), cb = Rgb4.B(cur);
                if (nr <= cr && ng <= cg && nb <= cb) continue;
                light[n] = Rgb4.Pack(Math.Max(nr, cr), Math.Max(ng, cg), Math.Max(nb, cb));
                q.Enqueue(n);
            }
        }
    }

    /// <summary>Brillo 0..1 de un nivel de luz (0..15, con decimales).</summary>
    private static float Shade(float level)
    {
        int i = (int)level;
        float t = level - i;
        return i >= 15 ? Curve[15] : Curve[i] + (Curve[i + 1] - Curve[i]) * t;
    }

    /// <summary>
    /// Brillo RGB de unos niveles por canal. El brillo sale del canal MÁS FUERTE y los demás conservan su proporción: así el color de la luz
    /// no se satura (aplicar la curva a cada canal por separado exageraba el tono: una luz cálida (13,10,5) acababa casi roja).
    /// </summary>
    private static Vector3 Shade(Vector3 level)
    {
        float m = MathF.Max(level.X, MathF.Max(level.Y, level.Z));
        return m <= 0f ? Vector3.Zero : level * (Shade(m) / m);
    }

    /// <summary>Brillo con el que se ve un bloque emisor por sí mismo (su luz y su color, sin depender del cielo ni del entorno).</summary>
    public static Vector3 EmitBrightness(ushort packedRgb) => Shade(new Vector3(Rgb4.R(packedRgb), Rgb4.G(packedRgb), Rgb4.B(packedRgb)));

    /// <summary>
    /// AO (0..3) y brillo de un vértice con las MISMAS muestras (celda de la cara, 2 laterales y esquina): cada celda vecina se lee una vez.
    /// El brillo sale en dos capas, cielo y bloques (0..1 por canal): el ciclo día/noche escala la de cielo en el shader, sin remallar.
    /// </summary>
    public static int VertexAoBrightness(LightField light, ChunkSnapshot<ushort> snap, BlockTable table, int bx, int by, int bz,
        (int X, int Y, int Z) u, (int X, int Y, int Z) v, int su, int sv, out Vector3 sky, out Vector3 block)
    {
        light.Levels(bx, by, bz, out var skySum, out var blockSum);
        int n = 1;

        int ax = bx + su * u.X, ay = by + su * u.Y, az = bz + su * u.Z;
        bool oa = table.Opaque[snap.Get(ax, ay, az)];
        int cx = bx + sv * v.X, cy = by + sv * v.Y, cz = bz + sv * v.Z;
        bool ob = table.Opaque[snap.Get(cx, cy, cz)];

        bool oc = true;   // con las dos laterales opacas la esquina no cuenta ni para el AO ni para la luz
        if (!oa || !ob)
        {
            int ex = ax + sv * v.X, ey = ay + sv * v.Y, ez = az + sv * v.Z;
            oc = table.Opaque[snap.Get(ex, ey, ez)];
            if (!oc) { light.Levels(ex, ey, ez, out var s2, out var b2); skySum += s2; blockSum += b2; n++; }
        }
        if (!oa) { light.Levels(ax, ay, az, out var s2, out var b2); skySum += s2; blockSum += b2; n++; }
        if (!ob) { light.Levels(cx, cy, cz, out var s2, out var b2); skySum += s2; blockSum += b2; n++; }

        sky = Shade(skySum / n);
        block = Shade(blockSum / n);
        return oa && ob ? 0 : 3 - ((oa ? 1 : 0) + (ob ? 1 : 0) + (oc ? 1 : 0));
    }

    /// <summary>Brillo (cielo y bloques) de una celda: lo usan los sprites, que se iluminan con la luz de su celda.</summary>
    public static void CellLight(LightField light, int x, int y, int z, out Vector3 sky, out Vector3 block)
    {
        light.Levels(x, y, z, out var s, out var b);
        sky = Shade(s);
        block = Shade(b);
    }
}
