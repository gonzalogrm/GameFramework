using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GF.Engine;

public static class SpriteLoader
{
    /// <summary>
    /// Carga todos los .png de una carpeta: cada archivo es un sprite y su nombre es el del archivo sin extensión. Pueden tener
    /// cualquier tamaño. Un sprites.json opcional ajusta tamaños en bloques. Los errores no son fatales: se devuelven en
    /// 'messages' y el sprite afectado se verá como el de reemplazo. Llamar desde el hilo principal (necesita el GraphicsDevice).
    /// </summary>
    public static SpriteAtlas LoadFolder(GraphicsDevice device, string folder, out List<string> messages)
    {
        messages = new List<string>();
        var builder = new SpriteAtlasBuilder();

        if (!Directory.Exists(folder))
        {
            messages.Add($"No existe la carpeta de sprites '{folder}'.");
            return builder.Build(device);
        }

        var manifest = new SpriteManifest();
        var manifestPath = Path.Combine(folder, "sprites.json");
        if (File.Exists(manifestPath))
        {
            try { manifest = SpriteManifest.Parse(File.ReadAllText(manifestPath)); }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                messages.Add("sprites.json no es valido (" + e.Message + "); se usan los tamaños por defecto.");
            }
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.png").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            try
            {
                using var stream = File.OpenRead(file);
                using var texture = Texture2D.FromStream(device, stream);
                var pixels = new Color[texture.Width * texture.Height];
                texture.GetData(pixels);

                var (w, h) = manifest.SizeOf(name, texture.Width, texture.Height);
                builder.Add(name, texture.Width, texture.Height, pixels, new Vector2(w, h));
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException
                                          or NotSupportedException or UnauthorizedAccessException)
            {
                messages.Add($"No se pudo cargar '{Path.GetFileName(file)}': {e.Message}");
            }
        }

        foreach (var name in manifest.Sprites.Keys.Where(n => !builder.Contains(n)))
            messages.Add($"sprites.json menciona '{name}' pero no hay {name}.png");

        return builder.Build(device);
    }
}
