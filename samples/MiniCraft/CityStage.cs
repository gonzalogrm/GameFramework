using GF.Core;
using GF.World;
using GF.World.Map;

namespace MiniCraft;

/// <summary>
/// Ciudades: nivela la zona y construye plaza con fuente, calles (cuadrícula recta con manzanas de tamaños muy distintos), muralla opcional y edificios.
/// Cada MANZANA (el espacio entre calles de la cuadrícula) se divide en solares (árbol binario determinista, sin calles entre ellos) y cada solar lleva un
/// edificio (casa, torre, salón o taller, con la puerta hacia la calle) o, si no, UN solo elemento decorativo (parque, jardín, estatua, pozo, puesto, huerto).
/// Un edificio solo se coloca si su planta cabe en el contorno y no pisa la plaza; si el centrado no cabe, se prueba pegado a cada lado del solar.
/// Cada columna se calcula de forma independiente a partir de (ciudad, coordenadas), así que no hay costuras entre chunks. Va DESPUÉS de TerrainStage.
/// </summary>
public sealed class CityStage : IGenerationStage<ushort>
{
    private const int WallThickness = 2, WallHeight = 4, MinLeaf = 8;

    private enum Kind { None, House, Park, Garden, Statue, Well, Field, Stall }
    private enum Build { House, Tower, Hall, Workshop }
    /// <summary>Un solar: origen (X0, Z0) relativo al centro de la ciudad, tamaño, qué lleva y, si es un edificio, su planta (Ox, Oz, Bw, Bd) y su puerta (0 -Z, 1 +Z, 2 -X, 3 +X).</summary>
    private readonly record struct Lot(Kind Kind, Build Type, int Height, int Bw, int Bd, int Ox, int Oz, int Lw, int Ld, ulong Hash, int Door, int X0, int Z0, int Base);
    private readonly record struct Axis(bool Street, int Off, int Start, int End, int Index);
    private readonly record struct Leaf(int X0, int Z0, int X1, int Z1, ulong H, int Bx0, int Bz0, int Bx1, int Bz1);

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
        bool ruin = city.Abandoned;   // una ruina: la misma ciudad, deteriorada
        int top = city.GroundY + 16 + Math.Max(city.MaxFloors * 4 + 3, 9);
        if (o.Y > top || o.Y + sy <= city.GroundY - 24) return;

        var cells = chunk.Cells;
        double extent2 = city.Extent * city.Extent;
        int plazaY = city.Target(0, 0);   // la plaza es plana
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
            double fade = city.FadeAt(dist, bound);       // 0 dentro, 1 en el contorno: el suelo se funde con el terreno original

            var ax = AxisAt(city, rx, city.Layout.PitchX, 0x11);
            var az = AxisAt(city, rz, city.Layout.PitchZ, 0x22);
            bool street = ax.Street || az.Street;

            // Fuente en el centro: 4x4, agua en el centro y borde elevado. Plaza: pavimento alrededor.
            bool fountain = rx >= -2 && rx <= 1 && rz >= -2 && rz <= 1;
            bool water = fountain && rx >= -1 && rx <= 0 && rz >= -1 && rz <= 0;
            bool rim = fountain && !water;
            bool plazaSq = rx >= -ph && rx < ph && rz >= -ph && rz < ph;
            bool plazaLamp = !ruin && plazaSq && !fountain && city.Type.LampSpacing > 0 && (rx == -ph || rx == ph - 1) && (rz == -ph || rz == ph - 1);
            bool wall = st.Walls && !street && !plazaSq && dist > bound - WallThickness;   // la muralla se abre donde llega una calle (puertas)

            Lot lot = default;
            int lx = 0, lz = 0, extra = 0;
            bool post = false;
            ushort surface;
            if (fountain)
            {
                surface = water ? (ruin ? Blocks.Dirt : Blocks.Water) : st.Plaza;   // la fuente de una ruina está seca
                extra = rim ? 1 : 0;
            }
            else if (plazaSq) { surface = st.Plaza; extra = plazaLamp ? 3 : 0; }
            else if (street)
            {
                surface = st.Road;
                // Pocas farolas: solo en la mitad de las calles de la cuadrícula, con la separación del tipo de ciudad (y nunca en los cruces).
                int sp = city.Type.LampSpacing;
                post = !ruin && sp > 0 && !(ax.Street && az.Street) &&
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
                    lot = LotAt(city, ax, az, rx, rz);
                    lx = rx - lot.X0; lz = rz - lot.Z0;
                    extra = Extra(lot);
                    if (lot.Kind == Kind.Well && lx == lot.Lw / 2 && lz == lot.Ld / 2) surface = Blocks.Water;   // el agua del pozo
                    if (lot.Kind == Kind.Field && Mod(lz, 3) == 1) surface = Blocks.Dirt;                         // surcos del huerto
                }
            }

            // Hacia el borde las calles se van deshaciendo: el firme da paso poco a poco al suelo natural (un camino de tierra).
            if (street && !plazaSq && fade > 0.35)
            {
                ulong fh = Hashing.Combine(city.Id, wx, 0xFAD1, wz);
                if ((fh >> 40) * (1f / (1 << 24)) < (fade - 0.35) / 0.65)
                {
                    ushort nt = Biomes.Surface[_columns.BiomeAt(wx, wz)];
                    surface = nt == Blocks.Stone ? Blocks.Grass : nt;
                }
            }

            // Ruinas: el firme está agrietado y por las grietas asoma el suelo natural y la tierra.
            if (ruin && (street || plazaSq) && !water)
            {
                ulong ch = Hashing.Combine(city.Id, wx, 0xC4AC, wz);
                if ((ch >> 40) * (1f / (1 << 24)) < city.Decay * 0.6)
                {
                    ushort nt2 = Biomes.Surface[_columns.BiomeAt(wx, wz)];
                    surface = (ch & 1) == 0 ? (nt2 == Blocks.Stone ? Blocks.Grass : nt2) : Blocks.Dirt;
                }
            }

            int h = _columns.HeightAt(wx, wz);
            // Plaza plana; calles y muralla siguen el relieve suavizado y, hacia el borde, se funden con el terreno original; cada solar es una terraza a su
            // propio nivel (ya fundido en PlotOf) y los solares sin nada conservan el terreno.
            int g;
            if (fountain || plazaSq) g = plazaY;
            else if (street || wall)
            {
                int tgt = city.Target(rx, rz);
                g = fade <= 0 ? tgt : (int)Math.Round(tgt + (h - tgt) * fade);
            }
            else g = lot.Kind == Kind.None ? h : lot.Base;
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
                if (ruin && id != Blocks.Air && r > 0 && (wall || rim || lot.Kind is Kind.House or Kind.Statue or Kind.Well or Kind.Stall))
                    id = RuinBlock(city, id, wx, wy, wz, r, wall ? WallHeight : extra, wall || rim ? 0UL : lot.Hash);
                cells[((wy - o.Y) * sz + z) * sx + x] = id;
            }
        }
    }

    /// <summary>
    /// Deterioro de una ruina: los bloques altos caen más que los bajos (los muros se desmoronan desde arriba), los cristales están rotos, las lámparas
    /// apagadas y algunos edificios se han venido abajo casi enteros. Parte de lo que cae lo ocupa la maleza (hojas). 'top' es la altura de la estructura
    /// en esa columna y 'plotHash' (0 si no es un edificio) decide si el solar se derrumbó.
    /// </summary>
    private static ushort RuinBlock(City c, ushort id, int wx, int wy, int wz, int r, int top, ulong plotHash)
    {
        if (id == Blocks.LampWarm) return Blocks.Air;
        double d = c.Decay;
        ulong h = Hashing.Combine(c.Id, wx, wy, wz);
        double u = (h >> 40) * (1.0 / (1 << 24)), v = ((h >> 16) & 0xFFFFF) / (double)0x100000;
        double rel = top <= 0 ? 0.0 : Math.Min(1.0, r / (double)top);
        double p = d * (0.25 + 0.75 * rel);
        if (plotHash != 0 && (plotHash >> 56) / 255.0 < d * 0.5 && rel > 0.3) p = 0.9;   // edificio derrumbado
        if (id == Blocks.GlassBlue) p = Math.Max(p, 0.55 + 0.4 * d);                      // cristales rotos
        if (u < p) return v < 0.15 ? Blocks.Leaves : Blocks.Air;
        return id;
    }

    private static int Mod(int a, int b) => ((a % b) + b) % b;

    // ------------------------------------------------------------------ calles de la cuadrícula

    /// <summary>Desplazamiento (±Jitter) de la calle k de un eje: las calles no forman una cuadrícula perfecta, y las manzanas salen de anchos distintos.</summary>
    private static int Jitter(City c, int k, int salt) =>
        c.Layout.Jitter == 0 ? 0 : (int)(Hashing.Combine(c.Id, k, salt, 0x57E) % (ulong)(2 * c.Layout.Jitter + 1)) - c.Layout.Jitter;

    private static int LineStart(City c, int k, int pitch, int salt) => k * pitch + Jitter(c, k, salt);

    /// <summary>
    /// A lo largo de un eje (coordenada r relativa al centro): ¿es calle? y, si no, entre qué calles queda la manzana (Start inclusivo, End exclusivo).
    /// La calle k empieza en k * pitch + Jitter(k) y mide StreetW; el desplazamiento nunca solapa dos calles.
    /// </summary>
    private static Axis AxisAt(City c, int r, int pitch, int salt)
    {
        int w = c.Layout.StreetW;
        int k = IntMath.FloorDiv(r, pitch);
        int sk = LineStart(c, k, pitch, salt), s1 = LineStart(c, k + 1, pitch, salt);
        if (r >= s1) return new Axis(true, r - s1, 0, 0, k + 1);
        if (r >= sk + w) return new Axis(false, 0, sk + w, s1, k);
        if (r >= sk) return new Axis(true, r - sk, 0, 0, k);
        int sPrev = LineStart(c, k - 1, pitch, salt);
        return new Axis(false, 0, sPrev + w, sk, k - 1);
    }

    // ------------------------------------------------------------------ manzanas y solares

    /// <summary>
    /// Divide una manzana en solares: un árbol binario determinista que parte por el lado largo (entre el 40 y el 60 %) mientras queden al menos 2 x MinLeaf,
    /// y a veces se detiene antes (solares grandes). Los solares no tienen calles entre ellos.
    /// </summary>
    private static bool TrySplit(ulong h, int depth, int x0, int z0, int x1, int z1, out bool splitX, out int cut)
    {
        int w = x1 - x0, d = z1 - z0;
        splitX = w >= d;
        int len = splitX ? w : d;
        cut = 0;
        if (depth >= 3 || len < 2 * MinLeaf) return false;
        ulong hh = Hashing.Mix(h + (ulong)depth * 0x9E3779B1UL);
        if (len <= 20 && (hh >> 8) % 100UL < 55UL) return false;   // a veces el solar se queda entero
        cut = Math.Clamp(len * (40 + (int)(hh % 21UL)) / 100, MinLeaf, len - MinLeaf);
        return true;
    }

    private static ulong ChildHash(ulong h, bool right, int depth) =>
        Hashing.Mix((h ^ (right ? 0xA5A5A5A5UL : 0x5A5A5A5AUL)) + (ulong)depth * 0x9E3779B97F4A7C15UL);

    private static Leaf LeafAt(City c, Axis ax, Axis az, int rx, int rz)
    {
        int x0 = ax.Start, x1 = ax.End, z0 = az.Start, z1 = az.End;
        ulong h = Hashing.Combine(c.Id, ax.Index, 0xB10C, az.Index);
        int depth = 0;
        while (TrySplit(h, depth, x0, z0, x1, z1, out bool splitX, out int cut))
        {
            bool right = splitX ? rx >= x0 + cut : rz >= z0 + cut;
            if (splitX) { if (right) x0 += cut; else x1 = x0 + cut; }
            else { if (right) z0 += cut; else z1 = z0 + cut; }
            h = ChildHash(h, right, depth);
            depth++;
        }
        return new Leaf(x0, z0, x1, z1, h, ax.Start, az.Start, ax.End, az.End);
    }

    private static void EnumLeaves(int x0, int z0, int x1, int z1, ulong h, int depth, int bx0, int bz0, int bx1, int bz1, List<Leaf> list)
    {
        if (!TrySplit(h, depth, x0, z0, x1, z1, out bool splitX, out int cut)) { list.Add(new Leaf(x0, z0, x1, z1, h, bx0, bz0, bx1, bz1)); return; }
        if (splitX)
        {
            EnumLeaves(x0, z0, x0 + cut, z1, ChildHash(h, false, depth), depth + 1, bx0, bz0, bx1, bz1, list);
            EnumLeaves(x0 + cut, z0, x1, z1, ChildHash(h, true, depth), depth + 1, bx0, bz0, bx1, bz1, list);
        }
        else
        {
            EnumLeaves(x0, z0, x1, z0 + cut, ChildHash(h, false, depth), depth + 1, bx0, bz0, bx1, bz1, list);
            EnumLeaves(x0, z0 + cut, x1, z1, ChildHash(h, true, depth), depth + 1, bx0, bz0, bx1, bz1, list);
        }
    }

    /// <summary>El solar de la columna (rx, rz). Su contenido se decide una vez y se guarda en la ciudad (es puro: da igual el hilo o el orden).</summary>
    private static Lot LotAt(City c, Axis ax, Axis az, int rx, int rz)
    {
        var leaf = LeafAt(c, ax, az, rx, rz);
        return (Lot)c.Plots.GetOrAdd((leaf.X0, leaf.Z0, leaf.X1, leaf.Z1), _ => (object)PlotOf(c, leaf));
    }

    private static int Extra(Lot l) => l.Kind switch
    {
        Kind.House => l.Type switch { Build.Tower => l.Height + 2, Build.Workshop => l.Height + 3, _ => l.Height + 1 },
        Kind.Park => 8,
        Kind.Garden => 2,
        Kind.Statue => 7,
        Kind.Well => 5,
        Kind.Field => 2,
        Kind.Stall => 4,
        _ => 0,
    };

    private static bool Inside(City c, int rx, int rz, int margin) =>
        Math.Sqrt((double)rx * rx + (double)rz * rz) + margin <= c.BoundaryRadius(rx, rz);

    /// <summary>
    /// ¿Cabe el rectángulo [x0, x1] x [z0, z1] (relativo al centro) entero dentro del contorno, con 'margin' de holgura? Se comprueba TODO el perímetro
    /// (cada 2 bloques), no solo las esquinas: el contorno es irregular y puede entrar entre dos esquinas.
    /// </summary>
    private static bool RectInside(City c, int x0, int z0, int x1, int z1, int margin)
    {
        for (int x = x0; ; x += 2)
        {
            if (x > x1) x = x1;
            if (!Inside(c, x, z0, margin) || !Inside(c, x, z1, margin)) return false;
            if (x == x1) break;
        }
        for (int z = z0; ; z += 2)
        {
            if (z > z1) z = z1;
            if (!Inside(c, x0, z, margin) || !Inside(c, x1, z, margin)) return false;
            if (z == z1) break;
        }
        return true;
    }

    /// <summary>Lado de la manzana hacia el que mira la puerta: uno de los que dan a una calle.</summary>
    private static int DoorSide(Leaf lf, ulong h)
    {
        Span<int> sides = stackalloc int[4];
        int n = 0;
        if (lf.Z0 == lf.Bz0) sides[n++] = 0;
        if (lf.Z1 == lf.Bz1) sides[n++] = 1;
        if (lf.X0 == lf.Bx0) sides[n++] = 2;
        if (lf.X1 == lf.Bx1) sides[n++] = 3;
        return n == 0 ? 0 : sides[(int)((h >> 48) % (ulong)n)];
    }

    /// <summary>
    /// Qué lleva un solar. Primero se intenta un EDIFICIO (la mayoría: casi todos en el núcleo, menos en la periferia); si la planta no cabe en el contorno
    /// o por azar, lleva UN elemento decorativo. En desierto y nieve casi no hay decoración (solo pozos, estatuas, algún puesto).
    /// </summary>
    private static Lot PlotOf(City c, Leaf lf)
    {
        int lw = lf.X1 - lf.X0, ld = lf.Z1 - lf.Z0;
        ulong h = lf.H;
        // El suelo del solar: una terraza al nivel del relieve suavizado, que hacia el borde de la ciudad se acerca al terreno original.
        int baseY = c.Target((lf.X0 + lf.X1) / 2, (lf.Z0 + lf.Z1) / 2);
        double baseFade = c.Fade((lf.X0 + lf.X1) / 2, (lf.Z0 + lf.Z1) / 2);
        if (baseFade > 0) baseY = (int)Math.Round(baseY + (c.OriginalHeight(c.CenterX + (lf.X0 + lf.X1) / 2, c.CenterZ + (lf.Z0 + lf.Z1) / 2) - baseY) * baseFade);
        Lot Make(Kind k, Build type = Build.House, int height = 0, int bw = 0, int bd = 0, int ox = 0, int oz = 0, int door = 0) =>
            new(k, type, height, bw, bd, ox, oz, lw, ld, h, door, lf.X0, lf.Z0, baseY);

        var t = c.Type;
        var st = c.Style;
        bool dry = st.Name is "desierto" or "nevada";
        int ph = c.PlazaHalf, margin = st.Walls ? WallThickness + 2 : 2;
        int midX = (lf.X0 + lf.X1) / 2, midZ = (lf.Z0 + lf.Z1) / 2;
        double nd = Math.Sqrt((double)midX * midX + (double)midZ * midZ) / Math.Max(1.0, c.BoundaryRadius(midX, midZ));
        int zone = nd < 0.40 ? 0 : nd < 0.70 ? 1 : 2;   // núcleo urbano, zona media, periferia

        bool touchesPlaza = lf.X0 < ph + 1 && lf.X1 > -ph - 1 && lf.Z0 < ph + 1 && lf.Z1 > -ph - 1;

        // ---- edificio
        int buildPct = zone == 0 ? 97 : zone == 1 ? 88 : t.Name == "aldea" ? 55 : 70;   // el centro, casi todo edificios
        if (t.Name == "fortaleza") buildPct += 8;
        // Hacia el contorno la edificación se aclara (hasta un 40 % de lo normal) y gana peso el campo abierto: la ciudad no acaba en seco.
        double edge = Math.Clamp((nd - 0.85) / 0.15, 0.0, 1.0);
        buildPct = (int)(buildPct * (1 - 0.6 * edge));
        if (c.Decay > 0) buildPct = (int)(buildPct * (1 - 0.35 * c.Decay));   // en una ruina ya no quedan tantos edificios
        int maxW = lw - 2, maxD = ld - 2;   // deja 1 bloque de margen por lado
        if ((int)(h % 100UL) < buildPct && maxW >= 5 && maxD >= 5)
        {
            int hall = t.HallPct, tower = t.TowerPct, work = t.WorkshopPct;
            if (zone == 0) { hall = hall * 16 / 10; tower = tower * 16 / 10; }
            else if (zone == 2) { hall = 0; tower = 0; work = work * 13 / 10; }

            int pick = (int)((h >> 40) % 100UL);
            var type = Build.House;
            if (maxW >= 10 && maxD >= 10 && pick < hall) type = Build.Hall;
            else if (c.MaxFloors >= 2 && pick >= hall && pick < hall + tower) type = Build.Tower;
            else if (maxW >= 7 && maxD >= 6 && pick >= hall + tower && pick < hall + tower + work) type = Build.Workshop;

            int bw, bd, height;
            switch (type)
            {
                case Build.Hall:
                    bw = Math.Max(10, maxW - (int)((h >> 16) % 3UL));
                    bd = Math.Max(10, maxD - (int)((h >> 24) % 3UL));
                    height = 6;
                    break;
                case Build.Tower:
                    bw = bd = Math.Min(5 + (int)((h >> 16) % 3UL), Math.Min(maxW, maxD));
                    height = c.MaxFloors * 4;
                    break;
                case Build.Workshop:
                    bw = 7 + (int)((h >> 16) % (ulong)(Math.Min(maxW, 10) - 7 + 1));
                    bd = 6 + (int)((h >> 24) % (ulong)(Math.Min(maxD, 8) - 6 + 1));
                    height = 4;
                    break;
                default:
                    // En el núcleo, edificios que llenan el solar; fuera, de tamaños más variados.
                    bw = zone == 0 ? Math.Max(5, maxW - (int)((h >> 16) % 4UL)) : 5 + (int)((h >> 16) % (ulong)(maxW - 5 + 1));
                    bd = zone == 0 ? Math.Max(5, maxD - (int)((h >> 24) % 4UL)) : 5 + (int)((h >> 24) % (ulong)(maxD - 5 + 1));
                    // Núcleo: edificios altos; periferia: casas de 1 o 2 plantas.
                    int fMax = zone == 2 ? Math.Min(2, c.MaxFloors) : c.MaxFloors;
                    int fMin = zone == 0 ? Math.Min(c.MaxFloors, Math.Max(2, c.MaxFloors - 1)) : 1;
                    height = (fMin + (int)((h >> 8) % (ulong)(fMax - fMin + 1))) * 4;
                    break;
            }

            // Posiciones candidatas en el solar: centrado y pegado a cada lado (junto a la plaza o al contorno, el centrado puede no caber).
            int[] oxs = { (lw - bw) / 2, 1, lw - bw - 1 }, ozs = { (ld - bd) / 2, 1, ld - bd - 1 };
            // El edificio entero (con tejado, almenas o chimenea) tiene que caber bajo el techo del mundo: si no, quedaría sin terminar.
            int needed = type == Build.Tower ? height + 2 : type == Build.Workshop ? height + 3 : height + 1;
            if (c.WorldHeight <= 0 || needed <= c.WorldHeight - 1 - baseY)
            foreach (int ox in oxs)
            foreach (int oz in ozs)
            {
                int x0 = lf.X0 + ox, z0 = lf.Z0 + oz, x1 = x0 + bw - 1, z1 = z0 + bd - 1;
                if (!RectInside(c, x0, z0, x1, z1, margin)) continue;
                if (x0 < ph + 1 && x1 > -ph - 1 && z0 < ph + 1 && z1 > -ph - 1) continue;   // no pisa la plaza
                return Make(Kind.House, type, height, bw, bd, ox, oz, DoorSide(lf, h));
            }
        }

        // El solar toca la plaza y no cabe un edificio: jardín (en desierto y nieve, un puesto de mercado si cabe fuera de la plaza).
        if (touchesPlaza)
        {
            if (!dry) return Make(Kind.Garden);
            return Make(lw >= 6 && ld >= 5 && (Math.Abs(midX) > ph + 3 || Math.Abs(midZ) > ph + 3) ? Kind.Stall : Kind.None);
        }

        // ---- un elemento decorativo (pesos por tipo de ciudad; poco verde en el núcleo, huertos solo en la periferia)
        int wPark = dry ? 0 : t.ParkPct, wGarden = dry ? 0 : t.GardenPct, wStatue = t.StatuePct + 2, wWell = t.WellPct + 3, wStall = 4;
        int wField = zone == 2 && !dry ? t.FieldPct : 0, wNone = dry ? 40 : 0;
        wPark += dry ? 0 : (int)(edge * 10);
        wNone += (int)(edge * 12);
        if (c.Decay > 0) { wNone += (int)(c.Decay * 25); wPark += dry ? 0 : (int)(c.Decay * 15); }   // la naturaleza lo recupera
        if (zone == 0) { wPark = wPark * 35 / 100; wGarden = wGarden * 35 / 100; wStatue += 4; }
        int roll = (int)((h >> 8) % (ulong)(wPark + wGarden + wStatue + wWell + wStall + wField + wNone));
        Kind kind;
        if ((roll -= wPark) < 0) kind = Kind.Park;
        else if ((roll -= wGarden) < 0) kind = Kind.Garden;
        else if ((roll -= wStatue) < 0) kind = Kind.Statue;
        else if ((roll -= wWell) < 0) kind = Kind.Well;
        else if ((roll -= wStall) < 0) kind = Kind.Stall;
        else if ((roll -= wField) < 0) kind = Kind.Field;
        else kind = Kind.None;

        if ((kind == Kind.Park || kind == Kind.Garden || kind == Kind.Field) && (lw < 5 || ld < 5)) kind = Kind.Statue;
        if (kind == Kind.Field && (lw < 6 || ld < 6)) kind = Kind.Garden;
        if (kind == Kind.Stall && (lw < 6 || ld < 5)) kind = Kind.Well;
        // Lo que ocupa todo el solar (parque, jardín, huerto, puesto) tiene que caber ENTERO en el contorno (y bajo la muralla): si no, quedaría cortado.
        int dm = st.Walls ? WallThickness + 1 : 1;
        if ((kind == Kind.Park || kind == Kind.Garden || kind == Kind.Field || kind == Kind.Stall) &&
            !RectInside(c, lf.X0, lf.Z0, lf.X1 - 1, lf.Z1 - 1, dm)) kind = Kind.Well;
        // Estatuas y pozos: 7 x 7 centrados en el solar, dentro del contorno y fuera de la plaza.
        if ((kind == Kind.Statue || kind == Kind.Well) &&
            !(lw >= 5 && ld >= 5 && RectInside(c, midX - 3, midZ - 3, midX + 3, midZ + 3, dm) && (Math.Abs(midX) > ph + 3 || Math.Abs(midZ) > ph + 3))) kind = Kind.None;
        return Make(kind);
    }

    // ------------------------------------------------------------------ lugares notables

    /// <summary>
    /// Lugares notables (para colocar NPCs), en coordenadas relativas al centro: delante de la puerta de cada salón, taller, torre y casa. Recorre
    /// manzanas y solares igual que Apply, así que coincide exactamente con lo construido.
    /// </summary>
    public static List<CityLandmark> Landmarks(City c)
    {
        var list = new List<CityLandmark>();
        var leaves = new List<Leaf>();
        int R = (int)Math.Ceiling(c.Extent);
        for (int rz = -R; rz <= R;)
        {
            var az = AxisAt(c, rz, c.Layout.PitchZ, 0x22);
            if (az.Street) { rz++; continue; }
            for (int rx = -R; rx <= R;)
            {
                var ax = AxisAt(c, rx, c.Layout.PitchX, 0x11);
                if (ax.Street) { rx++; continue; }
                leaves.Clear();
                ulong h = Hashing.Combine(c.Id, ax.Index, 0xB10C, az.Index);
                EnumLeaves(ax.Start, az.Start, ax.End, az.End, h, 0, ax.Start, az.Start, ax.End, az.End, leaves);
                foreach (var lf in leaves)
                {
                    var leaf = lf;
                    var lot = (Lot)c.Plots.GetOrAdd((leaf.X0, leaf.Z0, leaf.X1, leaf.Z1), _ => (object)PlotOf(c, leaf));
                    if (lot.Kind != Kind.House) continue;
                    string role = lot.Type switch { Build.Hall => "hall", Build.Workshop => "workshop", Build.Tower => "tower", _ => "house" };
                    int x0 = lot.X0 + lot.Ox, z0 = lot.Z0 + lot.Oz;
                    var (fx, fz) = lot.Door switch
                    {
                        1 => (x0 + lot.Bw - 1 - lot.Bw / 2, z0 + lot.Bd),
                        2 => (x0 - 1, z0 + lot.Bd / 2),
                        3 => (x0 + lot.Bw, z0 + lot.Bd / 2),
                        _ => (x0 + lot.Bw / 2, z0 - 1),
                    };
                    list.Add(new CityLandmark(role, fx, fz));
                }
                rx = ax.End;
            }
            rz = az.End;
        }
        return list;
    }

    // ------------------------------------------------------------------ contenido de los solares

    private static ushort LotBlock(City c, Lot l, int lx, int lz, int r, int wx, int wz)
    {
        var st = c.Style;
        switch (l.Kind)
        {
            case Kind.House: return BuildingBlock(l, lx - l.Ox, lz - l.Oz, r, st);
            case Kind.Park:
            {
                var t = TreeBlock(l, lx, lz, r);
                return t != Blocks.Air ? t : Ground(c, wx, wz, r, 0.05f);
            }
            case Kind.Garden:
            {
                var hedge = HedgeBlock(l, lx, lz, r);
                return hedge != Blocks.Air ? hedge : Ground(c, wx, wz, r, 0.18f);
            }
            case Kind.Statue: return StatueBlock(l, lx, lz, r, st);
            case Kind.Well: return WellBlock(l, lx, lz, r, st);
            case Kind.Field: return FieldBlock(c, l, lx, lz, r, wx, wz);
            case Kind.Stall: return StallBlock(l, lx, lz, r, st);
            default: return Blocks.Air;
        }
    }

    /// <summary>Hierba, flores o matorrales sueltos sobre el suelo (nada en desierto y nieve).</summary>
    private static ushort Ground(City c, int wx, int wz, int r, float density)
    {
        if (r != 1 || c.Style.Name is "desierto" or "nevada") return Blocks.Air;
        ulong ph = Hashing.Combine(c.Id, wx, 0xF10, wz);
        float u = (ph >> 40) * (1f / (1 << 24));
        if (u < density * 0.5f) return Blocks.GrassTuft;
        if (u < density * 0.7f) return Blocks.TallGrass;
        if (u < density) return ((ph >> 4) & 1) == 0 ? Blocks.FlowerRed : Blocks.FlowerYellow;
        if (u < density + 0.006f) return Blocks.Bush;
        return Blocks.Air;
    }

    /// <summary>Seto de hojas alrededor de un jardín (con una entrada en la cara frontal).</summary>
    private static ushort HedgeBlock(Lot l, int lx, int lz, int r)
    {
        if (r != 1 || l.Lw < 8 || l.Ld < 8) return Blocks.Air;
        bool border = lx == 0 || lz == 0 || lx == l.Lw - 1 || lz == l.Ld - 1;
        if (!border || (lz == 0 && Math.Abs(lx - l.Lw / 2) <= 1)) return Blocks.Air;
        return Blocks.Leaves;
    }

    /// <summary>Huerto: franjas de cultivo (hierba alta) entre surcos de tierra, con un borde libre de un bloque.</summary>
    private static ushort FieldBlock(City c, Lot l, int lx, int lz, int r, int wx, int wz)
    {
        if (r != 1 || lx < 1 || lz < 1 || lx >= l.Lw - 1 || lz >= l.Ld - 1 || Mod(lz, 3) == 1) return Blocks.Air;
        ulong ph = Hashing.Combine(c.Id, wx, 0xF1E, wz);
        return ((ph >> 40) * (1f / (1 << 24))) < 0.8f ? Blocks.TallGrass : Blocks.GrassTuft;
    }

    /// <summary>Puesto de mercado: 5 x 3, cuatro postes, mostrador delante, tejadillo y una lámpara.</summary>
    private static ushort StallBlock(Lot l, int lx, int lz, int r, CityStyle st)
    {
        int dx = lx - l.Lw / 2, dz = lz - l.Ld / 2;
        if (Math.Abs(dx) > 2 || Math.Abs(dz) > 1) return Blocks.Air;
        bool post = Math.Abs(dx) == 2 && Math.Abs(dz) == 1;
        if (r == 1) return post ? st.Pillar : dz == -1 ? st.Plaza : Blocks.Air;
        if (r <= 3) return post ? st.Pillar : (r == 3 && dx == 0 && dz == 0 ? Blocks.LampWarm : Blocks.Air);
        return r == 4 ? st.Roof : Blocks.Air;
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
        return (tx, tz, 4 + (int)((hh >> 32) % 3UL));
    }

    private static ushort TreeBlock(Lot l, int lx, int lz, int r)
    {
        int n = Math.Clamp(l.Lw * l.Ld / 150, 1, 6);
        for (int i = 0; i < n; i++)
        {
            var a = TreeSpot(l, i);
            ushort b = TreeAt(lx - a.X, lz - a.Z, r, a.Trunk);
            if (b != Blocks.Air) return b;
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

    /// <summary>
    /// Bloque del edificio a altura relativa r (1 = justo sobre el suelo). px, pz: posición dentro de su planta. Los cuatro modelos se definen con la puerta
    /// en la cara pz == 0; aquí se giran la planta según el lado de la puerta del solar.
    /// </summary>
    private static ushort BuildingBlock(Lot l, int px, int pz, int r, CityStyle st)
    {
        if (px < 0 || px >= l.Bw || pz < 0 || pz >= l.Bd) return Blocks.Air;
        int qx, qz, qw, qd;
        switch (l.Door)
        {
            case 1: qx = l.Bw - 1 - px; qz = l.Bd - 1 - pz; qw = l.Bw; qd = l.Bd; break;
            case 2: qx = pz; qz = px; qw = l.Bd; qd = l.Bw; break;
            case 3: qx = pz; qz = l.Bw - 1 - px; qw = l.Bd; qd = l.Bw; break;
            default: qx = px; qz = pz; qw = l.Bw; qd = l.Bd; break;
        }
        bool edgeX = qx == 0 || qx == qw - 1, edgeZ = qz == 0 || qz == qd - 1;
        return l.Type switch
        {
            Build.Tower => TowerBlock(l.Height, qx, qz, qw, qd, r, st, edgeX, edgeZ),
            Build.Hall => HallBlock(l.Height, qx, qz, qw, qd, r, st, edgeX, edgeZ),
            Build.Workshop => WorkshopBlock(l.Height, qx, qz, qw, qd, r, st, edgeX, edgeZ),
            _ => HouseBlock(l.Height, qx, qz, qw, qd, r, st, edgeX, edgeZ),
        };
    }

    private static ushort HouseBlock(int height, int px, int pz, int bw, int bd, int r, CityStyle st, bool edgeX, bool edgeZ)
    {
        if (r > height + 1) return Blocks.Air;
        if (r == height + 1) return st.Roof;   // tejado

        if (!edgeX && !edgeZ) return InteriorBlock(height, px, pz, bw, bd, r, st);
        if (edgeX && edgeZ) return st.Pillar;                                                                      // esquinas
        if (pz == 0 && px == bw / 2 && r <= 2) return Blocks.Air;                                                  // puerta

        bool window = (r % 4 == 2 || r % 4 == 3) && ((edgeX ? pz : px) & 1) == 1;
        return window ? Blocks.GlassBlue : st.Wall;
    }

    /// <summary>
    /// Posición de una celda del interior (ix, iz) en el anillo que recorre su perímetro (0 en la esquina (0,0), siguiendo +x, luego +z, -x y -z); -1 si no
    /// está en el perímetro. Para un interior de w x d, el anillo tiene 2w + 2d - 4 celdas.
    /// </summary>
    private static int RingIndex(int ix, int iz, int w, int d)
    {
        if (iz == 0) return ix;
        if (ix == w - 1) return (w - 1) + iz;
        if (iz == d - 1) return (w - 1) + (d - 1) + (w - 1 - ix);
        if (ix == 0) return 2 * (w - 1) + (d - 1) + (d - 1 - iz);
        return -1;
    }

    /// <summary>
    /// Interior de un edificio de varias plantas (plantas de 4 de alto: suelo en las filas 4, 8, ...). La escalera sube por el anillo del perímetro del
    /// interior: 4 escalones de 1 bloque (alturas 1 a 4) y el último queda a ras del suelo de arriba; encima de los escalones 2 y 3 el suelo tiene un hueco
    /// para pasar. Cada planta empieza su escalera en el lado opuesto a la de abajo y la primera queda lejos de la puerta. Hay que saltar los escalones.
    /// Una lámpara cuelga del techo de cada planta.
    /// </summary>
    private static ushort InteriorBlock(int height, int px, int pz, int bw, int bd, int r, CityStyle st)
    {
        int ix = px - 1, iz = pz - 1, w = bw - 2, d = bd - 2;
        int floors = height / 4;
        if (floors >= 2 && w >= 3 && d >= 3 && r >= 1 && r <= height - 1)
        {
            int ring = 2 * w + 2 * d - 4;
            int idx = RingIndex(ix, iz, w, d);

            if (r % 4 == 0 && r <= height - 4)   // suelo de la planta de arriba
            {
                int k = r / 4 - 1;                        // la escalera que sube de la planta k a la k+1
                int start = ((k + 1) * (ring / 2)) % ring;
                if (idx >= 0)
                {
                    int rel = (idx - start + ring) % ring;
                    if (rel == 1 || rel == 2) return Blocks.Air;   // hueco sobre los escalones 2 y 3
                }
                return st.Pillar;
            }
            if (idx >= 0)   // escalones
            {
                int k = (r - 1) / 4;
                if (k <= floors - 2)
                {
                    int rel = (idx - ((k + 1) * (ring / 2)) % ring + ring) % ring;
                    if (rel <= 3 && r <= 4 * k + 1 + rel) return st.Pillar;
                }
            }
        }
        bool lamp = px == bw / 2 && pz == bd / 2 && (r == height || (r % 4 == 3 && r < height));
        return lamp ? Blocks.LampWarm : Blocks.Air;
    }

    /// <summary>Torre: planta pequeña y muy alta, con troneras y almenas en la cima.</summary>
    private static ushort TowerBlock(int height, int px, int pz, int bw, int bd, int r, CityStyle st, bool edgeX, bool edgeZ)
    {
        if (r > height + 2) return Blocks.Air;
        if (r == height + 2) return (edgeX || edgeZ) && ((px + pz) & 1) == 0 ? st.Wall : Blocks.Air;   // almenas
        if (r == height + 1) return st.Roof;

        if (!edgeX && !edgeZ) return InteriorBlock(height, px, pz, bw, bd, r, st);
        if (edgeX && edgeZ) return st.Pillar;
        if (pz == 0 && px == bw / 2 && r <= 2) return Blocks.Air;   // puerta

        bool slit = (r % 4 == 2 || r % 4 == 3) && (edgeX ? pz : px) == (edgeX ? bd : bw) / 2;   // tronera centrada en cada cara
        return slit ? Blocks.GlassBlue : st.Wall;
    }

    /// <summary>Salón grande: planta amplia y techo alto (6), doble puerta, ventanales y columnas interiores.</summary>
    private static ushort HallBlock(int height, int px, int pz, int bw, int bd, int r, CityStyle st, bool edgeX, bool edgeZ)
    {
        if (r > height + 1) return Blocks.Air;
        if (r == height + 1) return st.Roof;

        if (!edgeX && !edgeZ)
        {
            if (px % 4 == 2 && pz % 4 == 2) return st.Pillar;   // columnas
            bool lamp = r == height && pz == bd / 2 && (px == bw / 3 || px == 2 * bw / 3);
            return lamp ? Blocks.LampWarm : Blocks.Air;
        }
        if (edgeX && edgeZ) return st.Pillar;
        if (pz == 0 && Math.Abs(px - bw / 2) <= 1 && r <= 3) return Blocks.Air;   // puerta de 3 de ancho

        bool window = r >= 2 && r <= 4 && ((edgeX ? pz : px) % 3) == 1;
        return window ? Blocks.GlassBlue : st.Wall;
    }

    /// <summary>Taller: fachada abierta con pilares, fragua con chimenea al fondo.</summary>
    private static ushort WorkshopBlock(int height, int px, int pz, int bw, int bd, int r, CityStyle st, bool edgeX, bool edgeZ)
    {
        // Fragua: base de piedra, fuego encima y chimenea que atraviesa el tejado.
        if (px == bw / 2 && pz == bd - 2)
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
