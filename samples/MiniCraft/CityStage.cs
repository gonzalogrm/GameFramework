using GF.Core;
using GF.World;
using GF.World.Map;

namespace MiniCraft;

/// <summary>
/// Ciudades: nivela la zona y construye plaza con fuente, calles de trazado irregular, muralla opcional, edificios de distinto tamaño y, en las parcelas
/// sin edificio, parques con árboles, jardines, estatuas y pozos. Cada columna se calcula de forma independiente a partir de (ciudad, coordenadas),
/// así que no hay costuras entre chunks. Va DESPUÉS de TerrainStage.
/// </summary>
public sealed class CityStage : IGenerationStage<ushort>
{
    private const int WallThickness = 2, WallHeight = 4;

    private enum Kind { Open, House, Park, Garden, Statue, Well, Field }
    private enum Build { House, Tower, Hall, Workshop }
    private readonly record struct Lot(Kind Kind, Build Type, int Height, int Bw, int Bd, int Ox, int Oz, int Lw, int Ld, ulong Hash);
    private readonly record struct Axis(bool Street, int Off, int Start, int End, int Index);

    private readonly CityPlanner _planner;
    private readonly WorldScale _scale;
    private readonly TerrainColumnCache _columns;

    public CityStage(CityPlanner planner, WorldScale scale, TerrainColumnCache columns)
    {
        _planner = planner; _scale = scale; _columns = columns;
    }

    public void Apply(Chunk<ushort> chunk, GenerationContext ctx)
    {
        var shape = chunk.Shape;
        var o = shape.Origin(chunk.Coord);
        // CellSize es múltiplo del chunk: un chunk cae en una sola celda, que tiene una ciudad y sus poblados.
        foreach (var city in _planner.CitiesAt(o.X + shape.SizeX / 2, o.Z + shape.SizeZ / 2)) ApplyCity(chunk, city);
    }

    private void ApplyCity(Chunk<ushort> chunk, City city)
    {
        var shape = chunk.Shape;
        var o = shape.Origin(chunk.Coord);
        int sx = shape.SizeX, sy = shape.SizeY, sz = shape.SizeZ;

        // Chunks lejos de esta ciudad: nada que hacer.
        double cdx = _scale.DeltaX(city.CenterX, o.X + sx / 2.0), cdz = o.Z + sz / 2.0 - city.CenterZ;
        if (Math.Sqrt(cdx * cdx + cdz * cdz) > city.Extent + (sx + sz) * 0.5) return;
        var st = city.Style;
        int top = city.GroundY + Math.Max(city.MaxFloors * 4 + 3, 9);
        if (o.Y > top || o.Y + sy <= city.GroundY - 24) return;

        var cells = chunk.Cells;
        double extent2 = city.Extent * city.Extent;
        int ph = city.PlazaHalf;   // la plaza es un cuadrado de 2*ph de lado centrado en la ciudad

        for (int z = 0; z < sz; z++)
        for (int x = 0; x < sx; x++)
        {
            int wx = o.X + x, wz = o.Z + z;
            int rx = (int)Math.Round(_scale.DeltaX(city.CenterX, wx)), rz = wz - city.CenterZ;
            double d2 = (double)rx * rx + (double)rz * rz;
            if (d2 > extent2) continue;
            double dist = Math.Sqrt(d2);
            double bound = city.BoundaryRadius(rx, rz);   // el contorno es irregular
            if (dist > bound) continue;

            var ax = AxisAt(city, rx, city.Layout.PitchX, 0x11);
            var az = AxisAt(city, rz, city.Layout.PitchZ, 0x22);
            bool street = ax.Street || az.Street;

            // Fuente en el centro: 4x4, agua en el centro y borde elevado. Plaza: pavimento alrededor.
            bool fountain = rx >= -2 && rx <= 1 && rz >= -2 && rz <= 1;
            bool water = fountain && rx >= -1 && rx <= 0 && rz >= -1 && rz <= 0;
            bool rim = fountain && !water;
            bool plazaSq = rx >= -ph && rx < ph && rz >= -ph && rz < ph;
            bool plazaLamp = plazaSq && !fountain && city.Type.LampSpacing > 0 && (rx == -ph || rx == ph - 1) && (rz == -ph || rz == ph - 1);
            bool wall = st.Walls && !street && !plazaSq && dist > bound - WallThickness;   // la muralla se abre donde llega una calle (puertas)

            Lot lot = default;
            int lx = 0, lz = 0, extra = 0;
            bool post = false;
            ushort surface;
            if (fountain)
            {
                surface = water ? Blocks.Water : st.Plaza;
                extra = rim ? 1 : 0;
            }
            else if (plazaSq) { surface = st.Plaza; extra = plazaLamp ? 3 : 0; }
            else if (street)
            {
                surface = st.Road;
                // Pocas farolas: solo en la mitad de las calles, con la separación del tipo de ciudad (y nunca en los cruces).
                int sp = city.Type.LampSpacing;
                post = sp > 0 && !(ax.Street && az.Street) &&
                       ((ax.Street && ax.Off == 0 && (ax.Index & 1) == 0 && Mod(rz, sp) == 0) ||
                        (az.Street && az.Off == 0 && (az.Index & 1) == 0 && Mod(rx, sp) == 0));
                extra = post ? 3 : 0;
            }
            else
            {
                ushort t = Biomes.Surface[_columns.BiomeAt(wx, wz)];
                surface = t == Blocks.Stone ? Blocks.Grass : t;
                if (wall) extra = WallHeight;
                else
                {
                    lot = GetLot(city, ax, az);
                    lx = rx - ax.Start; lz = rz - az.Start;
                    extra = Extra(lot);
                    if (lot.Kind == Kind.Well && lx == lot.Lw / 2 && lz == lot.Ld / 2) surface = Blocks.Water;   // el agua del pozo
                    if (lot.Kind == Kind.Field && Mod(lz, 3) == 1) surface = Blocks.Dirt;                         // surcos del huerto
                }
            }

            int h = _columns.HeightAt(wx, wz);
            int g = city.GroundY;
            int start = h >= g ? g : h + 1;                  // rellenar hasta g o reescribir la superficie
            int end = Math.Max(Math.Max(h, g), g + extra);   // lo que sobra del terreno se vacía; lo construido llega hasta g + extra
            int y0 = Math.Max(start, o.Y), y1 = Math.Min(end, o.Y + sy - 1);

            for (int wy = y0; wy <= y1; wy++)
            {
                int r = wy - g;
                ushort id;
                if (r < 0) id = Blocks.Dirt;
                else if (r == 0) id = surface;
                else if (rim) id = r == 1 ? st.Plaza : Blocks.Air;
                else if (fountain || plazaSq) id = plazaLamp ? (r <= 2 ? st.Pillar : r == 3 ? Blocks.LampWarm : Blocks.Air) : Blocks.Air;
                else if (street) id = post ? (r <= 2 ? st.Pillar : r == 3 ? Blocks.LampWarm : Blocks.Air) : Blocks.Air;
                else if (wall) id = r < WallHeight || (r == WallHeight && ((rx + rz) & 1) == 0) ? st.WallBlock : Blocks.Air;   // almenas
                else id = LotBlock(city, lot, lx, lz, r, wx, wz);
                cells[((wy - o.Y) * sz + z) * sx + x] = id;
            }
        }
    }

    private static int Mod(int a, int b) => ((a % b) + b) % b;

    // ------------------------------------------------------------------ trazado de calles

    /// <summary>Desplazamiento (±Jitter) de la calle k de un eje: las calles no forman una cuadrícula perfecta, y las parcelas salen de anchos distintos.</summary>
    private static int Jitter(City c, int k, int salt) =>
        c.Layout.Jitter == 0 ? 0 : (int)(Hashing.Combine(c.Id, k, salt, 0x57E) % (ulong)(2 * c.Layout.Jitter + 1)) - c.Layout.Jitter;

    /// <summary>
    /// A lo largo de un eje (coordenada r relativa al centro): ¿es calle? y, si no, entre qué calles queda la parcela (Start inclusivo, End exclusivo).
    /// La calle k empieza en k * pitch + Jitter(k) y mide StreetW; el desplazamiento nunca solapa dos calles.
    /// </summary>
    private static Axis AxisAt(City c, int r, int pitch, int salt)
    {
        int w = c.Layout.StreetW;
        int k = IntMath.FloorDiv(r, pitch);
        int sk = k * pitch + Jitter(c, k, salt), s1 = (k + 1) * pitch + Jitter(c, k + 1, salt);
        if (r >= s1) return new Axis(true, r - s1, 0, 0, k + 1);
        if (r >= sk + w) return new Axis(false, 0, sk + w, s1, k);
        if (r >= sk) return new Axis(true, r - sk, 0, 0, k);
        int sPrev = (k - 1) * pitch + Jitter(c, k - 1, salt);
        return new Axis(false, 0, sPrev + w, sk, k - 1);
    }

    // ------------------------------------------------------------------ parcelas

    private static int Extra(Lot l) => l.Kind switch
    {
        Kind.House => l.Type switch { Build.Tower => l.Height + 2, Build.Workshop => l.Height + 3, _ => l.Height + 1 },
        Kind.Park => 8,
        Kind.Garden => 2,
        Kind.Statue => 7,
        Kind.Well => 5,
        Kind.Field => 2,
        _ => 0,
    };

    /// <summary>
    /// Lugares notables (para colocar NPCs), en coordenadas relativas al centro: delante de la puerta de cada salón, taller, torre y casa. Recorre las
    /// parcelas igual que Apply, así que coincide exactamente con lo construido.
    /// </summary>
    public static List<CityLandmark> Landmarks(City c)
    {
        var list = new List<CityLandmark>();
        int R = (int)Math.Ceiling(c.Extent);
        for (int rz = -R; rz <= R;)
        {
            var az = AxisAt(c, rz, c.Layout.PitchZ, 0x22);
            if (az.Street) { rz++; continue; }
            for (int rx = -R; rx <= R;)
            {
                var ax = AxisAt(c, rx, c.Layout.PitchX, 0x11);
                if (ax.Street) { rx++; continue; }
                var lot = GetLot(c, ax, az);
                if (lot.Kind == Kind.House)
                {
                    string role = lot.Type switch { Build.Hall => "hall", Build.Workshop => "workshop", Build.Tower => "tower", _ => "house" };
                    list.Add(new CityLandmark(role, ax.Start + lot.Ox + lot.Bw / 2, az.Start + lot.Oz - 1));
                }
                rx = ax.End;
            }
            rz = az.End;
        }
        return list;
    }

    private static bool Inside(City c, int rx, int rz, int margin) =>
        Math.Sqrt((double)rx * rx + (double)rz * rz) + margin <= c.BoundaryRadius(rx, rz);

    private static Lot GetLot(City c, Axis ax, Axis az)
    {
        int sx = ax.Start, ex = ax.End, sz = az.Start, ez = az.End;
        int lw = ex - sx, ld = ez - sz;
        ulong h = Hashing.Combine(c.Id, ax.Index, 0xC17, az.Index);
        Lot Make(Kind k, Build type = Build.House, int height = 0, int bw = 0, int bd = 0, int ox = 0, int oz = 0) => new(k, type, height, bw, bd, ox, oz, lw, ld, h);

        // Las parcelas que tocan la plaza se quedan abiertas.
        int ph = c.PlazaHalf;
        if (sx < ph && ex > -ph && sz < ph && ez > -ph) return Make(Kind.Open);

        // ¿Cabe entera dentro del contorno (y de la muralla)?
        int margin = c.Style.Walls ? WallThickness + 2 : 2;
        bool fits = Inside(c, sx, sz, margin) && Inside(c, ex - 1, sz, margin) && Inside(c, sx, ez - 1, margin) && Inside(c, ex - 1, ez - 1, margin);

        // Zona de la parcela según su distancia al centro: núcleo urbano (denso y alto), zona media y periferia (casas bajas y huertos).
        int midX = (sx + ex) / 2, midZ = (sz + ez) / 2;
        double nd = Math.Sqrt((double)midX * midX + (double)midZ * midZ) / Math.Max(1.0, c.BoundaryRadius(midX, midZ));
        int zone = nd < 0.40 ? 0 : nd < 0.70 ? 1 : 2;

        // Qué hay en la parcela: porcentajes según el tipo de ciudad, ajustados por la zona.
        var t = c.Type;
        int park = t.ParkPct, garden = t.GardenPct, statue = t.StatuePct, well = t.WellPct, field = 0;
        int hall = t.HallPct, tower = t.TowerPct, work = t.WorkshopPct;
        if (zone == 0) { park = park * 35 / 100; garden = garden * 35 / 100; statue = statue > 0 ? statue + 4 : 0; hall = hall * 16 / 10; tower = tower * 16 / 10; }
        else if (zone == 2) { park = park * 8 / 10; garden = garden * 8 / 10; statue = 0; field = t.FieldPct; hall = 0; tower = 0; work = work * 13 / 10; }

        int roll = (int)(h % 100);
        Kind kind;
        if (!fits) kind = (roll & 1) == 0 && park > 0 ? Kind.Park : Kind.Garden;   // los bordes no se quedan pelados
        else if (roll < park) kind = Kind.Park;
        else if (roll < park + garden) kind = Kind.Garden;
        else if (roll < park + garden + statue) kind = Kind.Statue;
        else if (roll < park + garden + statue + well) kind = Kind.Well;
        else if (roll < park + garden + statue + well + field) kind = Kind.Field;
        else kind = Kind.House;
        if (kind == Kind.Field && (lw < 6 || ld < 6 || c.Style.Name is "desierto" or "nevada")) kind = Kind.Garden;

        if ((kind == Kind.Statue || kind == Kind.Well) && (lw < 5 || ld < 5)) kind = Kind.Garden;
        if (kind == Kind.House)
        {
            int maxW = lw - 2, maxD = ld - 2;   // deja 1 bloque de margen por lado
            if (maxW < 6 || maxD < 6) return Make(Kind.Park);

            // Tipo de edificio según lo que quepa: salón grande, torre (la más alta que permite el mundo), taller o casa.
            int pick = (int)((h >> 40) % 100);
            var type = Build.House;
            if (maxW >= 10 && maxD >= 10 && pick < hall) type = Build.Hall;
            else if (c.MaxFloors >= 2 && pick >= hall && pick < hall + tower) type = Build.Tower;
            else if (maxW >= 7 && pick >= hall + tower && pick < hall + tower + work) type = Build.Workshop;

            int bw, bd, height;
            switch (type)
            {
                case Build.Hall:
                    bw = Math.Max(10, maxW - (int)((h >> 16) % 3));
                    bd = Math.Max(10, maxD - (int)((h >> 24) % 3));
                    height = 6;
                    break;
                case Build.Tower:
                    bw = bd = Math.Min(5 + (int)((h >> 16) % 3), Math.Min(maxW, maxD));
                    height = c.MaxFloors * 4;
                    break;
                case Build.Workshop:
                    bw = 7 + (int)((h >> 16) % (ulong)(Math.Min(maxW, 10) - 7 + 1));
                    bd = 6 + (int)((h >> 24) % (ulong)(Math.Min(maxD, 8) - 6 + 1));
                    height = 4;
                    break;
                default:
                    bw = 6 + (int)((h >> 16) % (ulong)(maxW - 6 + 1));
                    bd = 6 + (int)((h >> 24) % (ulong)(maxD - 6 + 1));
                    // Núcleo: edificios altos; periferia: casas de 1 o 2 plantas.
                    int fMax = zone == 2 ? Math.Min(2, c.MaxFloors) : c.MaxFloors;
                    int fMin = zone == 0 ? Math.Min(c.MaxFloors, Math.Max(2, c.MaxFloors - 1)) : 1;
                    height = (fMin + (int)((h >> 8) % (ulong)(fMax - fMin + 1))) * 4;
                    break;
            }
            return Make(Kind.House, type, height, bw, bd, (lw - bw) / 2, (ld - bd) / 2);
        }
        return Make(kind);
    }

    private static ushort LotBlock(City c, Lot l, int lx, int lz, int r, int wx, int wz)
    {
        var st = c.Style;
        switch (l.Kind)
        {
            case Kind.House: return BuildingBlock(l, lx - l.Ox, lz - l.Oz, r, st);
            case Kind.Park:
            {
                var t = TreeBlock(l, lx, lz, r);
                return t != Blocks.Air ? t : Ground(c, wx, wz, r, 0.14f);
            }
            case Kind.Garden: return Ground(c, wx, wz, r, 0.45f);
            case Kind.Statue: return StatueBlock(l, lx, lz, r, st);
            case Kind.Well: return WellBlock(l, lx, lz, r, st);
            case Kind.Field: return FieldBlock(c, l, lx, lz, r, wx, wz);
            default: return Blocks.Air;
        }
    }

    /// <summary>Hierba, flores, matorrales o (en desierto y nieve) alguna roca sobre el suelo.</summary>
    private static ushort Ground(City c, int wx, int wz, int r, float density)
    {
        if (r != 1) return Blocks.Air;
        ulong ph = Hashing.Combine(c.Id, wx, 0xF10, wz);
        float u = (ph >> 40) * (1f / (1 << 24));
        if (c.Style.Name is "desierto" or "nevada") return u < 0.03f ? Blocks.Rock : Blocks.Air;
        if (u < density * 0.5f) return Blocks.GrassTuft;
        if (u < density * 0.7f) return Blocks.TallGrass;
        if (u < density) return ((ph >> 4) & 1) == 0 ? Blocks.FlowerRed : Blocks.FlowerYellow;
        if (u < density + 0.012f) return Blocks.Bush;
        return Blocks.Air;
    }

    /// <summary>Huerto: franjas de cultivo (hierba alta) entre surcos de tierra, con un borde libre de un bloque.</summary>
    private static ushort FieldBlock(City c, Lot l, int lx, int lz, int r, int wx, int wz)
    {
        if (r != 1 || lx < 1 || lz < 1 || lx >= l.Lw - 1 || lz >= l.Ld - 1 || Mod(lz, 3) == 1) return Blocks.Air;
        ulong ph = Hashing.Combine(c.Id, wx, 0xF1E, wz);
        return ((ph >> 40) * (1f / (1 << 24))) < 0.8f ? Blocks.TallGrass : Blocks.GrassTuft;
    }

    private static ushort StatueBlock(Lot l, int lx, int lz, int r, CityStyle st)
    {
        int dx = lx - l.Lw / 2, dz = lz - l.Ld / 2;
        if (Math.Abs(dx) > 1 || Math.Abs(dz) > 1) return Blocks.Air;
        if (r == 1) return st.Plaza;                                  // pedestal 3x3
        if (dx != 0 || dz != 0) return Blocks.Air;
        return r <= 5 ? st.Plaza : r == 6 ? Blocks.LampWarm : Blocks.Air;   // columna con luz
    }

    private static ushort WellBlock(Lot l, int lx, int lz, int r, CityStyle st)
    {
        int dx = lx - l.Lw / 2, dz = lz - l.Ld / 2;
        if (Math.Abs(dx) > 1 || Math.Abs(dz) > 1) return Blocks.Air;
        bool center = dx == 0 && dz == 0, corner = Math.Abs(dx) == 1 && Math.Abs(dz) == 1;
        if (r == 1) return center ? Blocks.Air : st.Plaza;   // brocal (el agua está en el suelo, en el centro)
        if (r <= 3) return corner ? st.Pillar : Blocks.Air;
        return r == 4 ? st.Roof : Blocks.Air;
    }

    // ------------------------------------------------------------------ árboles de los parques

    private static (int X, int Z, int Trunk) TreeSpot(Lot l, int i)
    {
        ulong hh = Hashing.Combine(unchecked((int)l.Hash), i, 0x7EE, 0);
        int spanX = l.Lw - 6, spanZ = l.Ld - 6;
        int tx = spanX > 0 ? 3 + (int)(hh % (ulong)spanX) : l.Lw / 2;
        int tz = spanZ > 0 ? 3 + (int)((hh >> 16) % (ulong)spanZ) : l.Ld / 2;
        return (tx, tz, 4 + (int)((hh >> 32) % 3));
    }

    private static ushort TreeBlock(Lot l, int lx, int lz, int r)
    {
        var a = TreeSpot(l, 0);
        ushort b = TreeAt(lx - a.X, lz - a.Z, r, a.Trunk);
        if (b != Blocks.Air) return b;
        if (l.Lw >= 12 && l.Ld >= 12)   // los parques grandes llevan un segundo árbol, separado del primero
        {
            var s = TreeSpot(l, 1);
            if (Math.Abs(s.X - a.X) >= 5 || Math.Abs(s.Z - a.Z) >= 5) return TreeAt(lx - s.X, lz - s.Z, r, s.Trunk);
        }
        return Blocks.Air;
    }

    /// <summary>Mismo árbol que TreeStage: tronco y copa de radio 2 (sin esquinas), luego 1 y punta en cruz. r = 1 es justo sobre el suelo.</summary>
    private static ushort TreeAt(int dx, int dz, int r, int trunk)
    {
        if (dx == 0 && dz == 0 && r >= 1 && r <= trunk) return Blocks.Log;
        int dy = r - 1;
        if (dy < trunk - 2 || dy > trunk + 1) return Blocks.Air;
        int rad = dy >= trunk ? 1 : 2;
        if (Math.Abs(dx) > rad || Math.Abs(dz) > rad) return Blocks.Air;
        if (rad == 2 && Math.Abs(dx) == 2 && Math.Abs(dz) == 2) return Blocks.Air;
        if (dy == trunk + 1 && Math.Abs(dx) + Math.Abs(dz) > 1) return Blocks.Air;
        return Blocks.Leaves;
    }

    // ------------------------------------------------------------------ edificios

    /// <summary>Bloque del edificio a altura relativa r (1 = justo sobre el suelo). px, pz: posición dentro de su planta.</summary>
    private static ushort BuildingBlock(Lot l, int px, int pz, int r, CityStyle st)
    {
        if (px < 0 || px >= l.Bw || pz < 0 || pz >= l.Bd) return Blocks.Air;
        bool edgeX = px == 0 || px == l.Bw - 1, edgeZ = pz == 0 || pz == l.Bd - 1;
        return l.Type switch
        {
            Build.Tower => TowerBlock(l, px, pz, r, st, edgeX, edgeZ),
            Build.Hall => HallBlock(l, px, pz, r, st, edgeX, edgeZ),
            Build.Workshop => WorkshopBlock(l, px, pz, r, st, edgeX, edgeZ),
            _ => HouseBlock(l, px, pz, r, st, edgeX, edgeZ),
        };
    }

    private static ushort HouseBlock(Lot l, int px, int pz, int r, CityStyle st, bool edgeX, bool edgeZ)
    {
        int height = l.Height;
        if (r > height + 1) return Blocks.Air;
        if (r == height + 1) return st.Roof;   // tejado

        if (!edgeX && !edgeZ) return r == height && px == l.Bw / 2 && pz == l.Bd / 2 ? Blocks.LampWarm : Blocks.Air;   // interior + lámpara
        if (edgeX && edgeZ) return st.Pillar;                                                                          // esquinas
        if (pz == 0 && px == l.Bw / 2 && r <= 2) return Blocks.Air;                                                    // puerta

        bool window = (r % 4 == 2 || r % 4 == 3) && ((edgeX ? pz : px) & 1) == 1;
        return window ? Blocks.GlassBlue : st.Wall;
    }

    /// <summary>Torre: planta pequeña y muy alta, con troneras y almenas en la cima.</summary>
    private static ushort TowerBlock(Lot l, int px, int pz, int r, CityStyle st, bool edgeX, bool edgeZ)
    {
        int height = l.Height;
        if (r > height + 2) return Blocks.Air;
        if (r == height + 2) return (edgeX || edgeZ) && ((px + pz) & 1) == 0 ? st.Wall : Blocks.Air;   // almenas
        if (r == height + 1) return st.Roof;

        if (!edgeX && !edgeZ) return r == height && px == l.Bw / 2 && pz == l.Bd / 2 ? Blocks.LampWarm : Blocks.Air;
        if (edgeX && edgeZ) return st.Pillar;
        if (pz == 0 && px == l.Bw / 2 && r <= 2) return Blocks.Air;   // puerta

        bool slit = (r % 4 == 2 || r % 4 == 3) && (edgeX ? pz : px) == (edgeX ? l.Bd : l.Bw) / 2;   // tronera centrada en cada cara
        return slit ? Blocks.GlassBlue : st.Wall;
    }

    /// <summary>Salón grande: planta amplia y techo alto (6), doble puerta, ventanales y columnas interiores.</summary>
    private static ushort HallBlock(Lot l, int px, int pz, int r, CityStyle st, bool edgeX, bool edgeZ)
    {
        int height = l.Height;
        if (r > height + 1) return Blocks.Air;
        if (r == height + 1) return st.Roof;

        if (!edgeX && !edgeZ)
        {
            if (px % 4 == 2 && pz % 4 == 2) return st.Pillar;   // columnas
            bool lamp = r == height && pz == l.Bd / 2 && (px == l.Bw / 3 || px == 2 * l.Bw / 3);
            return lamp ? Blocks.LampWarm : Blocks.Air;
        }
        if (edgeX && edgeZ) return st.Pillar;
        if (pz == 0 && Math.Abs(px - l.Bw / 2) <= 1 && r <= 3) return Blocks.Air;   // puerta de 3 de ancho

        bool window = r >= 2 && r <= 4 && ((edgeX ? pz : px) % 3) == 1;
        return window ? Blocks.GlassBlue : st.Wall;
    }

    /// <summary>Taller: fachada abierta con pilares, fragua con chimenea al fondo.</summary>
    private static ushort WorkshopBlock(Lot l, int px, int pz, int r, CityStyle st, bool edgeX, bool edgeZ)
    {
        int height = l.Height;
        // Fragua: base de piedra, fuego encima y chimenea que atraviesa el tejado.
        if (px == l.Bw / 2 && pz == l.Bd - 2)
        {
            if (r == 1) return Blocks.Stone;
            if (r == 2) return Blocks.LampWarm;
            return r <= height + 3 ? Blocks.Stone : Blocks.Air;
        }
        if (r > height + 1) return Blocks.Air;
        if (r == height + 1) return st.Roof;

        if (edgeX && edgeZ) return st.Pillar;
        if (pz == 0 && !edgeX)   // fachada abierta
            return r == height ? st.Wall : (px % 3 == 0 ? st.Pillar : Blocks.Air);
        if (!edgeX && !edgeZ) return Blocks.Air;

        bool window = (r == 2 || r == 3) && ((edgeX ? pz : px) & 1) == 1;
        return window ? Blocks.GlassBlue : st.Wall;
    }
}

/// <summary>Un lugar notable de una ciudad (delante de la puerta de un edificio), relativo a su centro.</summary>
public readonly record struct CityLandmark(string Role, int Rx, int Rz);
