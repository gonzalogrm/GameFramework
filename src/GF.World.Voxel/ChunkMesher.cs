using GF.Core;
using GF.Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GF.World.Voxel;

/// <summary>Un sprite Billboard: posición (base, centro) LOCAL al chunk, región UV en el SpriteAtlas y tamaño en bloques.</summary>
public readonly record struct BillboardInstance(Vector3 Position, Vector2 UvMin, Vector2 UvMax, float Width, float Height);

/// <summary>
/// Geometría de un chunk en listas separadas: opaca, translúcida (agua), sprites en cruz (con recorte por alfa)
/// y las instancias de sprites Billboard (que el renderer orienta a la cámara en cada frame).
/// </summary>
public sealed record MeshData(
    VertexPositionColorTexture[] Vertices, int[] Indices,
    VertexPositionColorTexture[] WaterVertices, int[] WaterIndices,
    VertexPositionColorTexture[] SpriteVertices, int[] SpriteIndices,
    BillboardInstance[] Billboards,
    Vector3 ExtentMin, Vector3 ExtentMax, LightChunk? Light = null)   // caja (local al chunk) que cubre los sprites, que pueden sobresalir del chunk
{
    public static readonly MeshData Empty = new(
        Array.Empty<VertexPositionColorTexture>(), Array.Empty<int>(),
        Array.Empty<VertexPositionColorTexture>(), Array.Empty<int>(),
        Array.Empty<VertexPositionColorTexture>(), Array.Empty<int>(),
        Array.Empty<BillboardInstance>(), Vector3.Zero, Vector3.Zero);

    public bool IsEmpty => Indices.Length == 0 && WaterIndices.Length == 0 && SpriteIndices.Length == 0 && Billboards.Length == 0;
}

public static class ChunkMesher
{
    private const int WaterAlpha = 150;
    /// <summary>Brillo según oclusión del vértice: 0 = muy ocluido ... 3 = sin oclusión.</summary>
    private static readonly float[] AoLevels = { 0.45f, 0.65f, 0.82f, 1f };

    /// <summary>Listas reutilizables por hilo (los hilos del ThreadPool se reciclan): evita reservar y crecer listas en cada chunk.</summary>
    private sealed class Builders
    {
        public readonly List<VertexPositionColorTexture> Verts = new(8192), WVerts = new(), SVerts = new();
        public readonly List<int> Idx = new(16384), WIdx = new(), SIdx = new();
        public readonly List<BillboardInstance> Bills = new();

        public void Clear()
        {
            Verts.Clear(); WVerts.Clear(); SVerts.Clear();
            Idx.Clear(); WIdx.Clear(); SIdx.Clear();
            Bills.Clear();
        }
    }

    [ThreadStatic] private static Builders? t_builders;

    /// <summary>Conveniencia para el hilo principal: captura y malla de forma síncrona.</summary>
    public static MeshData Build(IWorld<ushort> world, Chunk<ushort> chunk, BlockRegistry blocks, TextureAtlas atlas, SpriteAtlas sprites,
        LightEnvironment? env = null) =>
        Build(ChunkSnapshot<ushort>.Capture(world, chunk), blocks, atlas, sprites, env);

    /// <summary>
    /// Apto para hilos de fondo: solo lee el snapshot, las tablas del registro de bloques (inmutables) y el cálculo de UVs del
    /// atlas. No toca GraphicsDevice. Emite solo caras visibles, con ambient occlusion y luz por voxel en cada vértice.
    /// Optimizado: salida inmediata en chunks de aire, tablas en arrays, vecinos de las celdas interiores leídos directamente
    /// del array y listas reutilizadas.
    /// </summary>
    public static MeshData Build(ChunkSnapshot<ushort> snap, BlockRegistry blocks, TextureAtlas atlas, SpriteAtlas sprites,
        LightEnvironment? env = null)
    {
        var cells = snap.Cells;
        if (cells.AsSpan().IndexOfAnyExcept(BlockRegistry.Air) < 0) return MeshData.Empty;

        var shape = snap.Shape;
        var origin = shape.Origin(snap.Coord);
        var table = blocks.GetTable();
        var light = ChunkLighting.Compute(snap, table, env ?? LightEnvironment.Default);   // luz RGB: cielo, bloques y focos
        var bld = t_builders ??= new Builders();
        bld.Clear();
        var verts = bld.Verts; var idx = bld.Idx;
        var wverts = bld.WVerts; var widx = bld.WIdx;
        var sverts = bld.SVerts; var sidx = bld.SIdx;
        var bills = bld.Bills;
        var extentMin = Vector3.Zero;
        var extentMax = Vector3.Zero;

        int sx = shape.SizeX, sy = shape.SizeY, sz = shape.SizeZ;
        int strideY = sx * sz;
        // Desplazamiento en el array para cada cara: +X, -X, +Y, -Y, +Z, -Z (índice = (y * sz + z) * sx + x).
        Span<int> faceOffset = stackalloc int[] { 1, -1, strideY, -strideY, sx, -sx };
        Span<int> ao = stackalloc int[4];
        Span<Vector3> lt = stackalloc Vector3[4];

        for (int y = 0; y < sy; y++)
        for (int z = 0; z < sz; z++)
        for (int x = 0; x < sx; x++)
        {
            int i = (y * sz + z) * sx + x;
            ushort id = cells[i];
            if (id == BlockRegistry.Air) continue;
            var def = table.Defs[id];

            if (def.Render != BlockRender.Cube)
            {
                AddSprite(def, sprites, origin, x, y, z, sverts, sidx, bills, ref extentMin, ref extentMax);
                continue;
            }

            bool water = def.Translucent;
            var vl = water ? wverts : verts;
            var il = water ? widx : idx;
            bool interior = x > 0 && x < sx - 1 && y > 0 && y < sy - 1 && z > 0 && z < sz - 1;

            for (int f = 0; f < 6; f++)
            {
                var n = VoxelFaces.Normals[f];
                int bx = x + n.X, by = y + n.Y, bz = z + n.Z;
                ushort nid = interior ? cells[i + faceOffset[f]] : snap.Get(bx, by, bz);
                if (nid == id) continue;
                if (table.Opaque[nid]) continue;   // el aire no es opaco

                var u = VoxelFaces.TangentU[f];
                var v = VoxelFaces.TangentV[f];
                for (int k = 0; k < 4; k++)
                {
                    int su = (k == 1 || k == 2) ? 1 : -1;
                    int sv = k >= 2 ? 1 : -1;
                    lt[k] = ChunkLighting.VertexBrightness(light, snap, table, bx, by, bz, u, v, su, sv);
                    ao[k] = water ? 3 : VertexAo(snap, table, bx, by, bz, u, v, su, sv);
                }

                int b = vl.Count;
                var basePos = new Vector3(x, y, z);   // vértices LOCALES al chunk (origen flotante)
                var shade = VoxelFaces.Shade[f];
                int tile = def.FaceTiles[f];
                for (int k = 0; k < 4; k++)
                {
                    float a = AoLevels[ao[k]];
                    var color = new Color((int)(shade.R * a * lt[k].X), (int)(shade.G * a * lt[k].Y), (int)(shade.B * a * lt[k].Z),
                        water ? WaterAlpha : 255);
                    vl.Add(new VertexPositionColorTexture(
                        basePos + VoxelFaces.Corners[f][k], color,
                        atlas.GetUV(tile, VoxelFaces.UVs[f][k])));
                }

                // Triángulos en sentido horario (frontal en MonoGame). Se elige la diagonal según el AO
                // para evitar el artefacto de interpolación anisótropa.
                if (ao[1] + ao[3] > ao[0] + ao[2])
                {
                    il.Add(b + 1); il.Add(b + 3); il.Add(b + 2);
                    il.Add(b + 1); il.Add(b); il.Add(b + 3);
                }
                else
                {
                    il.Add(b); il.Add(b + 2); il.Add(b + 1);
                    il.Add(b); il.Add(b + 3); il.Add(b + 2);
                }
            }
        }

        if (verts.Count == 0 && wverts.Count == 0 && sverts.Count == 0 && bills.Count == 0) return MeshData.Empty;
        return new MeshData(verts.ToArray(), idx.ToArray(), wverts.ToArray(), widx.ToArray(),
            sverts.ToArray(), sidx.ToArray(), bills.ToArray(), extentMin, extentMax, light.ExtractCenter());
    }

    /// <summary>
    /// Sprite 2D de una celda, anclado en el centro de su base. Puede ser mayor que la celda: su tamaño sale del propio sprite
    /// (o de BlockDef.SpriteWidth/Height) y se anota en 'min/max' para que el renderer no lo descarte al recortar chunks.
    /// Cross: dos planos que se cruzan en diagonal, cuadrado de lado 'ancho' en planta, dentro de la malla del chunk.
    /// Billboard: solo se anota la instancia. Un pequeño desplazamiento determinista evita el aspecto de rejilla.
    /// </summary>
    private static void AddSprite(BlockDef def, SpriteAtlas sprites, CellCoord origin, int x, int y, int z,
        List<VertexPositionColorTexture> verts, List<int> idx, List<BillboardInstance> billboards,
        ref Vector3 min, ref Vector3 max)
    {
        var region = sprites.Get(def.SpriteName);
        float w = def.SpriteWidth > 0 ? def.SpriteWidth : region.Size.X;
        float h = def.SpriteHeight > 0 ? def.SpriteHeight : region.Size.Y;

        ulong hash = Hashing.Combine(0x5B21, origin.X + x, origin.Y + y, origin.Z + z);
        float jx = ((hash & 0xFF) / 255f - 0.5f) * 0.4f;
        float jz = (((hash >> 8) & 0xFF) / 255f - 0.5f) * 0.4f;
        float cx = x + 0.5f + jx, cz = z + 0.5f + jz;

        if (def.Render == BlockRender.Billboard)
        {
            billboards.Add(new BillboardInstance(new Vector3(cx, y, cz), region.UvMin, region.UvMax, w, h));
            float r = w * 0.5f;   // gira alrededor del eje vertical: ocupa un círculo de ese radio
            Include(ref min, ref max, new Vector3(cx - r, y, cz - r));
            Include(ref min, ref max, new Vector3(cx + r, y + h, cz + r));
            return;
        }

        float a = 0.5f * w, top = y + h;
        AddQuad(verts, idx, sprites, region, new Vector3(cx - a, y, cz - a), new Vector3(cx + a, y, cz + a), top);
        AddQuad(verts, idx, sprites, region, new Vector3(cx + a, y, cz - a), new Vector3(cx - a, y, cz + a), top);
        Include(ref min, ref max, new Vector3(cx - a, y, cz - a));
        Include(ref min, ref max, new Vector3(cx + a, top, cz + a));
    }

    private static void Include(ref Vector3 min, ref Vector3 max, Vector3 p)
    {
        min = Vector3.Min(min, p);
        max = Vector3.Max(max, p);
    }

    /// <summary>Plano vertical entre dos puntos de la base; se dibuja sin culling, así que el sentido de giro da igual.</summary>
    private static void AddQuad(List<VertexPositionColorTexture> verts, List<int> idx, SpriteAtlas sprites, SpriteRegion region,
        Vector3 bottomLeft, Vector3 bottomRight, float top)
    {
        int b = verts.Count;
        verts.Add(new VertexPositionColorTexture(bottomLeft, Color.White, sprites.GetUV(region, new Vector2(0, 1))));
        verts.Add(new VertexPositionColorTexture(bottomRight, Color.White, sprites.GetUV(region, new Vector2(1, 1))));
        verts.Add(new VertexPositionColorTexture(new Vector3(bottomRight.X, top, bottomRight.Z), Color.White, sprites.GetUV(region, new Vector2(1, 0))));
        verts.Add(new VertexPositionColorTexture(new Vector3(bottomLeft.X, top, bottomLeft.Z), Color.White, sprites.GetUV(region, new Vector2(0, 0))));
        idx.Add(b); idx.Add(b + 1); idx.Add(b + 2);
        idx.Add(b); idx.Add(b + 2); idx.Add(b + 3);
    }

    private static int VertexAo(ChunkSnapshot<ushort> snap, BlockTable table, int bx, int by, int bz,
        (int X, int Y, int Z) u, (int X, int Y, int Z) v, int su, int sv)
    {
        bool side1 = table.Opaque[snap.Get(bx + su * u.X, by + su * u.Y, bz + su * u.Z)];
        bool side2 = table.Opaque[snap.Get(bx + sv * v.X, by + sv * v.Y, bz + sv * v.Z)];
        bool corner = table.Opaque[snap.Get(
            bx + su * u.X + sv * v.X, by + su * u.Y + sv * v.Y, bz + su * u.Z + sv * v.Z)];
        if (side1 && side2) return 0;
        return 3 - ((side1 ? 1 : 0) + (side2 ? 1 : 0) + (corner ? 1 : 0));
    }
}
