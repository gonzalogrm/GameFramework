using GF.World.Voxel;

namespace MiniCraft;

/// <summary>
/// Distancias de visión y reparto de hilos que salen de WorldSettings: chunks cercanos, terreno lejano (LOD), franja de fundido, niebla de distancia y
/// hilos de generación y mallado. Es solo cálculo (sin GPU): PlayScene lo lee al montar los sistemas.
/// </summary>
internal sealed record ViewConfig(int ChunkSize, int Layers, int View, float FarBlocks, bool FarEnabled, int FarClose, FadeBand Fade,
    float FogRange, float FogStart, float FogEnd, int GenThreads, int MeshThreads)
{
    public static ViewConfig From(WorldSettings s)
    {
        int cs = s.ChunkSize, view = s.ViewDistance;
        float viewBlocks = view * cs;
        // Terreno lejano (LOD): si está activo, la niebla y el plano lejano de la cámara se alejan hasta su límite.
        float farBlocks = s.FarDistanceChunks * cs;
        bool farEnabled = farBlocks > viewBlocks * 1.25f;
        // Distancia CERCANA del terreno lejano: por dentro no hay malla lejana (0 = el borde de los chunks).
        // Fundido: los bloques se disuelven en las últimas FadeChunks filas de chunks y el terreno lejano empieza justo debajo, así que
        // lo que se descarta deja ver el relieve aproximado. Sin terreno lejano no hay nada que mostrar detrás: sin fundido.
        int fadeChunks = Math.Clamp(s.FadeChunks, 0, Math.Max(0, view - 2));
        int farClose = s.FarCloseChunks > 0 ? s.FarCloseChunks : (farEnabled && fadeChunks > 0 ? view - fadeChunks : view);
        var fade = farEnabled && fadeChunks > 0 ? FadeBand.For(farClose, view, cs) : FadeBand.Disabled;
        float fogRange = farEnabled ? farBlocks : viewBlocks;
        float fogStart = fogRange * (farEnabled ? 0.05f : 0.55f), fogEnd = fogRange * (farEnabled ? 0.98f : 0.95f);
        // Hilos de fondo: ~60 % a generar chunks y el resto a mallarlos; se dejan libres un par de núcleos para el juego.
        int workers = s.WorkerThreads > 0 ? s.WorkerThreads : Math.Max(2, Environment.ProcessorCount - 2);
        int gen = Math.Max(1, (int)Math.Round(workers * 0.6));
        int mesh = Math.Max(1, workers - gen);
        return new ViewConfig(cs, s.VerticalChunks, view, farBlocks, farEnabled, farClose, fade, fogRange, fogStart, fogEnd, gen, mesh);
    }

    /// <summary>Sin el shader de los chunks no hay fundido: tampoco hay solape entre chunks y terreno lejano.</summary>
    public ViewConfig WithoutFade(WorldSettings s) =>
        this with { Fade = FadeBand.Disabled, FarClose = s.FarCloseChunks > 0 ? s.FarCloseChunks : View };
}
