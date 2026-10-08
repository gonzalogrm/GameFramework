namespace GF.World.Voxel;

/// <summary>
/// Las mismas fórmulas que usa el shader ChunkFade.fx, en C#, para poder probarlas y para decidir cuándo un chunk ya es del todo visible.
/// </summary>
public static class FadeCurve
{
    /// <summary>Aparición de un chunk: 0 al construirse su malla, 1 cuando pasan 'duration' segundos. duration &lt;= 0: sin fundido.</summary>
    public static float ChunkFade(double now, double createdAt, double duration) =>
        duration <= 0 ? 1f : (float)Math.Clamp((now - createdAt) / duration, 0.0, 1.0);

    /// <summary>smoothstep de HLSL.</summary>
    public static float Smoothstep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Visibilidad por distancia: 1 cerca, 0 desde 'end'.</summary>
    public static float DistanceFade(float distance, float start, float end) => 1f - Smoothstep(start, end, distance);
}

/// <summary>Franja (en bloques desde la cámara) donde los bloques se disuelven sobre el terreno lejano.</summary>
public readonly record struct FadeBand(float Start, float End, bool Enabled)
{
    /// <summary>Valores que dejan el efecto sin actuar nunca (bordes distintos: smoothstep no admite bordes iguales).</summary>
    public static readonly FadeBand Disabled = new(1e8f, 2e8f, false);

    /// <summary>
    /// El terreno lejano empieza (en el peor caso: cámara en un lado de su chunk) a closeChunks + 1 chunks de la cámara, así que los bloques
    /// no empiezan a desvanecerse antes: debajo de todo píxel descartado siempre hay terreno lejano. Han desaparecido del todo un bloque
    /// antes del borde de los chunks cargados. Si no queda sitio para una franja útil, el efecto se desactiva.
    /// </summary>
    public static FadeBand For(int closeChunks, int viewChunks, int chunkSize)
    {
        float start = (closeChunks + 1) * chunkSize, end = viewChunks * chunkSize - 1f;
        return end - start >= 4f ? new FadeBand(start, end, true) : Disabled;
    }
}
