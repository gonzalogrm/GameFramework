using System.Text.Json;

namespace GF.Engine;

/// <summary>
/// sprites.json (opcional) junto a los .png. Solo sirve para indicar tamaños; si un sprite no aparece, mide
/// (píxeles / PixelsPerBlock) bloques. Ejemplo:
///     { "pixelsPerBlock": 16, "sprites": { "rock": { "width": 0.9, "height": 0.7 } } }
/// Admite comentarios // y comas finales.
/// </summary>
public sealed class SpriteManifest
{
    public sealed class SpriteSpec
    {
        public float? Width { get; set; }
        public float? Height { get; set; }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Cuántos píxeles de un sprite equivalen a un bloque de ancho/alto.</summary>
    public int PixelsPerBlock { get; set; } = 16;
    public Dictionary<string, SpriteSpec> Sprites { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static SpriteManifest Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize<SpriteManifest>(json, Options) ?? new SpriteManifest();
        manifest.PixelsPerBlock = Math.Max(1, manifest.PixelsPerBlock);
        manifest.Sprites = new Dictionary<string, SpriteSpec>(manifest.Sprites ?? new(), StringComparer.OrdinalIgnoreCase);
        return manifest;
    }

    /// <summary>Tamaño en bloques (ancho, alto) de un sprite de pixelWidth x pixelHeight píxeles.</summary>
    public (float Width, float Height) SizeOf(string name, int pixelWidth, int pixelHeight)
    {
        float w = pixelWidth / (float)PixelsPerBlock, h = pixelHeight / (float)PixelsPerBlock;
        if (Sprites.TryGetValue(name, out var spec))
        {
            if (spec.Width is > 0) w = spec.Width.Value;
            if (spec.Height is > 0) h = spec.Height.Value;
        }
        return (w, h);
    }
}
