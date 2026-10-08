using GF.World.Voxel;
using Xunit;

namespace GF.World.Tests;

public class FadeTests
{
    [Fact]
    public void ChunkFade_GoesFromZeroToOneOverTheDuration_AndStaysThere()
    {
        Assert.Equal(0f, FadeCurve.ChunkFade(10.0, 10.0, 0.6));
        Assert.Equal(0.5f, FadeCurve.ChunkFade(10.3, 10.0, 0.6), 4);
        Assert.Equal(1f, FadeCurve.ChunkFade(10.6, 10.0, 0.6));
        Assert.Equal(1f, FadeCurve.ChunkFade(99.0, 10.0, 0.6));
        Assert.Equal(0f, FadeCurve.ChunkFade(5.0, 10.0, 0.6));            // aún no ha aparecido (reloj anterior): 0, no negativo
        Assert.Equal(1f, FadeCurve.ChunkFade(10.0, 10.0, 0.0));           // duración 0: sin fundido
    }

    [Fact]
    public void DistanceFade_IsOneNear_ZeroFar_AndMonotonic()
    {
        Assert.Equal(1f, FadeCurve.DistanceFade(10f, 64f, 95f));
        Assert.Equal(1f, FadeCurve.DistanceFade(64f, 64f, 95f));
        Assert.Equal(0f, FadeCurve.DistanceFade(95f, 64f, 95f));
        Assert.Equal(0f, FadeCurve.DistanceFade(500f, 64f, 95f));

        float previous = 1f;
        for (float d = 64f; d <= 95f; d += 0.5f)
        {
            float v = FadeCurve.DistanceFade(d, 64f, 95f);
            Assert.InRange(v, 0f, 1f);
            Assert.True(v <= previous + 1e-6f);   // nunca vuelve a subir al alejarse
            previous = v;
        }
        Assert.Equal(0.5f, FadeCurve.DistanceFade(79.5f, 64f, 95f), 4);   // a mitad de la franja, la mitad
    }

    [Fact]
    public void FadeBand_StartsWhereTheFarTerrainIsGuaranteed_AndEndsBeforeTheLoadedEdge()
    {
        var band = FadeBand.For(closeChunks: 4, viewChunks: 6, chunkSize: 16);
        Assert.True(band.Enabled);
        Assert.Equal(80f, band.Start);   // (4 + 1) chunks: ahí el terreno lejano existe seguro debajo, esté la cámara donde esté en su chunk
        Assert.Equal(95f, band.End);     // un bloque antes del borde de los chunks cargados (6 x 16)

        // Si el terreno lejano empieza demasiado tarde, no hay franja útil: el efecto se apaga en lugar de dejar huecos.
        Assert.False(FadeBand.For(closeChunks: 5, viewChunks: 6, chunkSize: 16).Enabled);
        Assert.False(FadeBand.For(closeChunks: 6, viewChunks: 6, chunkSize: 16).Enabled);
        Assert.Equal(FadeBand.Disabled, FadeBand.For(closeChunks: 9, viewChunks: 6, chunkSize: 16));

        // Más chunks de vista: la franja se ensancha.
        var wide = FadeBand.For(closeChunks: 10, viewChunks: 14, chunkSize: 16);
        Assert.True(wide.Enabled);
        Assert.True(wide.End - wide.Start > band.End - band.Start);
    }

    [Fact]
    public void DisabledBand_NeverFadesAnything()
    {
        var band = FadeBand.Disabled;
        Assert.False(band.Enabled);
        Assert.Equal(1f, FadeCurve.DistanceFade(5000f, band.Start, band.End));   // ni a distancias enormes
    }
}
