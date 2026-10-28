using GF.Core;
using GF.World;
using GF.World.Map;

namespace MiniCraft;

/// <summary>
/// Ciudades: nivela la zona, dibuja calles, plaza con fuente, muralla con puertas, edificios y farolas. Cada columna se calcula de forma
/// independiente a partir de (ciudad, coordenadas), así que no hay costuras entre chunks. Va DESPUÉS de TerrainStage.
/// </summary>
public sealed class CityStage : IGenerationStage<ushort>
{
    public const int Pitch = 20, Street = 4;   // parcela de 16 + calle de 4
    private const int WallThickness = 2, WallHeight = 4;

    private readonly record struct Lot(bool Built, int Floors, int Bw, int Bd, int Ox, int Oz);

    private readonly CityPlanner _planner;
    private readonly WorldScale _scale;
    private readonly TerrainColumnCache _columns;

    public CityStage(CityPlanner planner, WorldScale scale, TerrainColumnCache columns)
    {
        _planner = planner; _scale = scale; _columns = columns;
    }

    /// <summary>Las 4 parcelas alrededor del centro de la ciudad forman la plaza.</summary>
    private static bool IsPlaza(int lotX, int lotZ) => (lotX == -1 || lotX == 0) && (lotZ == -1 || lotZ == 0);

    public void Apply(Chunk<ushort> chunk, GenerationContext ctx)
    {
        var shape = chunk.Shape;
        var o = shape.Origin(chunk.Coord);
        int sx = shape.SizeX, sy = shape.SizeY, sz = shape.SizeZ;

        var city = _planner.CityAt(o.X + sx / 2, o.Z + sz / 2);   // CellSize es múltiplo del chunk: un chunk cae en una sola celda
        if (city == null) return;
        int top = city.GroundY + city.MaxFloors * 4 + 1;
        if (o.Y > top || o.Y + sy <= city.GroundY - 24) return;

        var cells = chunk.Cells;
        long r2 = (long)city.Radius * city.Radius;
        long wallInner = (long)(city.Radius - WallThickness) * (city.Radius - WallThickness);

        for (int z = 0; z < sz; z++)
        for (int x = 0; x < sx; x++)
        {
            int wx = o.X + x, wz = o.Z + z;
            int rx = (int)Math.Round(_scale.DeltaX(city.CenterX, wx)), rz = wz - city.CenterZ;
            long d2 = (long)rx * rx + (long)rz * rz;
            if (d2 > r2) continue;

            int lotX = IntMath.FloorDiv(rx, Pitch), lotZ = IntMath.FloorDiv(rz, Pitch);
            int lx = rx - lotX * Pitch, lz = rz - lotZ * Pitch;
            bool street = lx < Street || lz < Street;

            // Fuente en el centro: 4x4, agua en el centro y borde de piedra elevado.
            bool fountain = rx >= -2 && rx <= 1 && rz >= -2 && rz <= 1;
            bool water = fountain && rx >= -1 && rx <= 0 && rz >= -1 && rz <= 0;
            bool rim = fountain && !water;

            bool wall = !street && d2 > wallInner;   // la muralla se abre donde llega una calle (puertas)

            Lot lot = default;
            bool post = false;
            int extra = 0;
            ushort surface;
            if (fountain)
            {
                surface = water ? Blocks.Water : Blocks.Stone;
                extra = rim ? 1 : 0;
            }
            else if (street)
            {
                surface = Blocks.Stone;
                post = lx == 1 && lz == 1 && ((lotX + lotZ) & 1) == 0;   // farola en algunas esquinas
                extra = post ? 3 : 0;
            }
            else if (IsPlaza(lotX, lotZ))
            {
                surface = Blocks.Stone;
            }
            else
            {
                ushort t = Biomes.Surface[_columns.BiomeAt(wx, wz)];
                surface = t == Blocks.Stone ? Blocks.Grass : t;
                if (wall) extra = WallHeight;
                else
                {
                    lot = GetLot(city, lotX, lotZ);
                    extra = lot.Built ? lot.Floors * 4 + 1 : 0;
                }
            }

            int h = _columns.HeightAt(wx, wz);
            int g = city.GroundY;
            int start = h >= g ? g : h + 1;               // rellenar hasta g o reescribir la superficie
            int end = Math.Max(Math.Max(h, g), g + extra);   // lo que sobra del terreno se vacía; lo construido llega hasta g + extra
            int y0 = Math.Max(start, o.Y), y1 = Math.Min(end, o.Y + sy - 1);

            for (int wy = y0; wy <= y1; wy++)
            {
                int r = wy - g;
                ushort id;
                if (r < 0) id = Blocks.Dirt;
                else if (r == 0) id = surface;
                else if (rim) id = r == 1 ? Blocks.Stone : Blocks.Air;
                else if (street) id = post ? (r <= 2 ? Blocks.Log : r == 3 ? Blocks.LampWarm : Blocks.Air) : Blocks.Air;
                else if (wall) id = r < WallHeight || (r == WallHeight && ((rx + rz) & 1) == 0) ? Blocks.Stone : Blocks.Air;   // almenas
                else id = lot.Built ? BuildingBlock(lot, lx - lot.Ox, lz - lot.Oz, r) : Blocks.Air;
                cells[((wy - o.Y) * sz + z) * sx + x] = id;
            }
        }
    }

    private static Lot GetLot(City c, int lotX, int lotZ)
    {
        if (IsPlaza(lotX, lotZ)) return default;
        int mx = lotX * Pitch + Street + (Pitch - Street) / 2, mz = lotZ * Pitch + Street + (Pitch - Street) / 2;
        int lim = c.Radius - 12;   // la parcela entera debe caber dentro del círculo (y de la muralla)
        if (lim <= 0 || (long)mx * mx + (long)mz * mz > (long)lim * lim) return default;

        ulong h = Hashing.Combine(c.Id, lotX, 0xC17, lotZ);
        if ((h & 3) == 0) return default;   // parcela vacía (25 %)

        int floors = 1 + (int)((h >> 8) % (ulong)c.MaxFloors);
        int bw = 7 + (int)((h >> 16) % 6), bd = 7 + (int)((h >> 24) % 6);   // 7..12
        int ox = Street + (Pitch - Street - bw) / 2, oz = Street + (Pitch - Street - bd) / 2;
        return new Lot(true, floors, bw, bd, ox, oz);
    }

    /// <summary>Bloque del edificio a altura relativa r (1 = justo sobre el suelo). px, pz: posición dentro de su planta.</summary>
    private static ushort BuildingBlock(Lot l, int px, int pz, int r)
    {
        if (px < 0 || px >= l.Bw || pz < 0 || pz >= l.Bd) return Blocks.Air;
        int height = l.Floors * 4;
        if (r > height + 1) return Blocks.Air;
        if (r == height + 1) return Blocks.Log;   // tejado

        bool edgeX = px == 0 || px == l.Bw - 1, edgeZ = pz == 0 || pz == l.Bd - 1;
        if (!edgeX && !edgeZ) return r == height && px == l.Bw / 2 && pz == l.Bd / 2 ? Blocks.LampWarm : Blocks.Air;   // interior + lámpara
        if (edgeX && edgeZ) return Blocks.Log;                                                                         // esquinas
        if (pz == 0 && px == l.Bw / 2 && r <= 2) return Blocks.Air;                                                    // puerta

        bool window = (r % 4 == 2 || r % 4 == 3) && ((edgeX ? pz : px) & 1) == 1;
        return window ? Blocks.GlassBlue : Blocks.Stone;
    }
}
