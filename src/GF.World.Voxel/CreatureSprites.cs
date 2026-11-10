using Microsoft.Xna.Framework;

namespace GF.World.Voxel;

/// <summary>
/// Sprites de criaturas dibujados por código, al estilo roguelike (siluetas con contorno y sombreado de dos tonos). Salen del TAMAÑO de la caja de la entidad:
/// alta y estrecha = humanoide (o bruto si es ancha), baja y larga = cuadrúpedo, muy pequeña = alimaña, cuadrada = cofre. Los detalles (cuernos, capa, arma,
/// orejas, cola, cuello largo...) salen de un hash del tamaño, así que cada especie con tamaño propio tiene su propia silueta. Los sprites son GRISES: el
/// color de la entidad (ya con su luz) los tiñe al dibujarlos. Sin dependencias de la GPU.
/// </summary>
internal static class CreatureSprites
{
    private static readonly Color Base = new(235, 235, 235), Light = new(255, 255, 255), Shade = new(190, 190, 190),
                                  Detail = new(150, 150, 150), Outline = new(96, 96, 96), Eye = new(70, 70, 70);

    private sealed class Canvas
    {
        public readonly int W, H;
        public readonly Color[] Px;
        public Canvas(int w, int h) { W = w; H = h; Px = new Color[w * h]; }
        public void Set(int x, int y, Color c) { if ((uint)x < (uint)W && (uint)y < (uint)H) Px[y * W + x] = c; }
        public void Rect(int x0, int y0, int x1, int y1, Color c)
        {
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++) Set(x, y, c);
        }
        public void Ellipse(float cx, float cy, float rx, float ry, Color c)
        {
            for (int y = (int)MathF.Floor(cy - ry); y <= (int)MathF.Ceiling(cy + ry); y++)
            for (int x = (int)MathF.Floor(cx - rx); x <= (int)MathF.Ceiling(cx + rx); x++)
            {
                float nx = (x - cx) / rx, ny = (y - cy) / ry;
                if (nx * nx + ny * ny <= 1f) Set(x, y, c);
            }
        }
        public void Line(int x0, int y0, int x1, int y1, Color c)
        {
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx - dy;
            while (true)
            {
                Set(x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }
        public bool Opaque(int x, int y) => (uint)x < (uint)W && (uint)y < (uint)H && Px[y * W + x].A > 0;
    }

    private static uint Hash(Vector3 size)
    {
        uint h = unchecked((uint)((int)MathF.Round(size.X * 20f) * 73856093) ^ (uint)((int)MathF.Round(size.Y * 20f) * 19349663));
        h ^= h >> 13; h = unchecked(h * 0x5bd1e995u); h ^= h >> 15;
        return h;
    }

    /// <summary>Crea el sprite gris de una criatura de este tamaño (ancho, alto, fondo). Devuelve los píxeles y el tamaño del lienzo.</summary>
    public static Color[] Create(Vector3 size, out int w, out int h)
    {
        float width = size.X, height = size.Y;
        uint hash = Hash(size);
        Canvas c;
        if (MathF.Abs(width - height) < 0.02f && width >= 0.7f) { c = new Canvas(14, 12); Chest(c); }
        else if (height >= 1.5f && height / MathF.Max(0.01f, width) >= 1.9f)
        {
            bool brute = width >= 0.95f;
            c = brute ? new Canvas(16, 22) : new Canvas(12, 20);
            if (brute) Brute(c, hash); else Humanoid(c, hash);
        }
        else if (height < 0.75f) { c = new Canvas(12, 9); Critter(c, hash); }
        else { c = new Canvas(20, 14); Quadruped(c, hash, height > 1.4f); }
        Finish(c);
        w = c.W; h = c.H;
        return c.Px;
    }

    /// <summary>Sombreado de dos tonos (claro donde no hay nada encima, oscuro donde no hay nada debajo) y contorno oscuro.</summary>
    private static void Finish(Canvas c)
    {
        for (int y = 0; y < c.H; y++)
        for (int x = 0; x < c.W; x++)
        {
            if (!c.Opaque(x, y) || c.Px[y * c.W + x] != Base) continue;
            if (!c.Opaque(x, y - 1)) c.Px[y * c.W + x] = Light;
            else if (!c.Opaque(x, y + 1) || !c.Opaque(x + 1, y)) c.Px[y * c.W + x] = Shade;
        }
        var outline = new List<int>();
        for (int y = 0; y < c.H; y++)
        for (int x = 0; x < c.W; x++)
        {
            if (c.Opaque(x, y)) continue;
            if (c.Opaque(x - 1, y) || c.Opaque(x + 1, y) || c.Opaque(x, y - 1) || c.Opaque(x, y + 1)) outline.Add(y * c.W + x);
        }
        foreach (int i in outline) c.Px[i] = Outline;
    }

    private static void Humanoid(Canvas c, uint h)   // 12 x 20
    {
        int hat = (int)(h & 3);
        bool weapon = ((h >> 2) & 1) != 0, cape = ((h >> 3) & 1) != 0;
        if (cape) c.Rect(2, 9, 9, 17, Shade);
        c.Rect(1, 9, 2, 14, Base); c.Rect(9, 9, 10, 14, Base);          // brazos
        c.Rect(3, 9, 8, 15, Base); c.Rect(3, 13, 8, 13, Detail);        // cuerpo y cinturón
        c.Rect(3, 16, 5, 19, Base); c.Rect(6, 16, 8, 19, Base);         // piernas
        c.Ellipse(5.5f, 5f, 3f, 3.2f, Base);                            // cabeza
        c.Set(4, 5, Eye); c.Set(7, 5, Eye);
        if (hat == 1) c.Rect(3, 2, 8, 3, Detail);                       // gorro
        else if (hat == 2) { c.Set(3, 1, Detail); c.Set(2, 0, Detail); c.Set(8, 1, Detail); c.Set(9, 0, Detail); }   // cuernos
        else if (hat == 3) { c.Ellipse(5.5f, 4.5f, 3.8f, 3.8f, Detail); c.Ellipse(5.5f, 5.5f, 2.4f, 2.4f, Base); c.Set(4, 5, Eye); c.Set(7, 5, Eye); }   // capucha
        if (weapon) c.Line(10, 14, 11, 7, Detail);
    }

    private static void Brute(Canvas c, uint h)   // 16 x 22
    {
        bool horns = (h & 1) != 0, club = ((h >> 1) & 1) != 0;
        c.Rect(1, 9, 3, 16, Base); c.Rect(12, 9, 14, 16, Base);          // brazos
        c.Rect(2, 9, 13, 11, Base);                                      // hombros
        c.Rect(4, 9, 11, 17, Base); c.Rect(4, 14, 11, 14, Detail);       // torso y cinturón
        c.Rect(4, 18, 7, 21, Base); c.Rect(8, 18, 11, 21, Base);         // piernas
        c.Ellipse(7.5f, 5.5f, 3f, 3f, Base);                             // cabeza pequeña
        c.Set(6, 5, Eye); c.Set(9, 5, Eye);
        if (horns) { c.Set(5, 2, Detail); c.Set(4, 1, Detail); c.Set(3, 1, Detail); c.Set(10, 2, Detail); c.Set(11, 1, Detail); c.Set(12, 1, Detail); }
        if (club) c.Rect(14, 3, 15, 13, Detail);
    }

    private static void Quadruped(Canvas c, uint h, bool longNeck)   // 20 x 14
    {
        int tail = (int)((h >> 1) & 3), ears = (int)((h >> 3) & 3);
        c.Ellipse(9f, 7f, 7f, 3.6f, Base);                               // cuerpo
        c.Rect(4, 9, 5, 13, Base); c.Rect(7, 9, 8, 13, Base);            // patas
        c.Rect(11, 9, 12, 13, Base); c.Rect(14, 9, 15, 13, Base);
        int headTop;
        if (longNeck)
        {
            c.Rect(14, 1, 15, 8, Base); c.Ellipse(16.5f, 2f, 2.6f, 1.8f, Base); c.Rect(18, 2, 19, 3, Base); c.Set(17, 2, Eye);
            headTop = 0;
        }
        else
        {
            c.Rect(13, 4, 15, 8, Base); c.Ellipse(16.5f, 5f, 2.8f, 2.3f, Base); c.Rect(18, 5, 19, 6, Base); c.Set(17, 4, Eye);
            headTop = 3;
        }
        if (tail == 1) { c.Set(1, 6, Base); c.Set(1, 5, Base); }
        else if (tail == 2) c.Line(2, 6, 0, 3, Detail);
        else if (tail == 3) { c.Line(2, 7, 0, 8, Detail); c.Set(0, 9, Detail); }
        if (ears == 1) { c.Set(15, headTop - 1, Base); c.Set(17, headTop - 1, Base); }
        else if (ears == 2) { c.Set(16, headTop - 1, Detail); c.Set(16, headTop - 2, Detail); }
        else if (ears == 3) { c.Line(15, headTop, 14, headTop - 2, Detail); c.Line(18, headTop, 19, headTop - 2, Detail); }
    }

    private static void Critter(Canvas c, uint h)   // 12 x 9
    {
        c.Ellipse(6f, 5.5f, 4.5f, 3f, Base);
        if ((h & 1) != 0) { c.Rect(3, 0, 3, 2, Base); c.Rect(8, 0, 8, 2, Base); }       // orejas largas
        else { c.Rect(3, 1, 4, 2, Base); c.Rect(7, 1, 8, 2, Base); }                     // orejas redondas
        c.Set(11, 5, Base); c.Set(11, 4, Base);                                          // cola
        c.Set(4, 4, Eye); c.Set(7, 4, Eye);
        c.Set(3, 8, Detail); c.Set(8, 8, Detail);
    }

    private static void Chest(Canvas c)   // 14 x 12
    {
        c.Rect(1, 5, 12, 10, Base);
        c.Rect(1, 3, 12, 5, Shade);                                                      // tapa
        c.Rect(6, 3, 7, 10, Detail);                                                     // banda
        c.Set(6, 7, Eye); c.Set(7, 7, Eye);                                              // cerradura
    }
}
