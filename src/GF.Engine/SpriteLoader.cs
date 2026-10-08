using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace GF.Engine;

public static class SpriteLoader
{
    /// <summary>
    /// Sprites desde el PIPELINE DE CONTENIDO de MonoGame (MGCB): cada textura compilada de una carpeta de contenido (Content/sprites/*.xnb)
    /// es un sprite, de cualquier tamaño, y su nombre es el del archivo. Después se leen los .png SUELTOS de 'looseFolder' (si existe,
    /// junto al ejecutable): sustituyen al compilado del mismo nombre o añaden sprites nuevos sin recompilar el contenido (útil para mods
    /// y pruebas rápidas). Un sprites.json opcional (tamaños en bloques) se busca primero en la carpeta suelta y luego en la de contenido.
    /// Los errores no son fatales: se devuelven en 'messages' y el sprite afectado se ve como el de reemplazo.
    /// Llamar desde el hilo principal. El ContentManager puede liberarse después: el atlas se empaqueta en una textura propia.
    /// </summary>
    /// <param name="contentFolder">Carpeta dentro del contenido, p. ej. "sprites".</param>
    public static SpriteAtlas LoadContent(GraphicsDevice device, ContentManager content, string contentFolder,
        string looseFolder, out List<string> messages)
    {
        messages = new List<string>();
        var builder = new SpriteAtlasBuilder();
        string contentDir = Path.Combine(AppContext.BaseDirectory, content.RootDirectory, contentFolder);

        var manifest = LoadManifest(Directory.Exists(looseFolder) && File.Exists(Path.Combine(looseFolder, "sprites.json"))
            ? Path.Combine(looseFolder, "sprites.json")
            : Path.Combine(contentDir, "sprites.json"), messages);

        // 1) Compilados por el pipeline.
        var compiled = ListContentSprites(contentDir);
        if (compiled.Count == 0)
            messages.Add($"No hay sprites compilados en '{contentDir}' (¿se ha compilado el contenido? dotnet build).");
        foreach (var name in compiled)
        {
            try
            {
                var texture = content.Load<Texture2D>(contentFolder + "/" + name);
                AddTexture(builder, manifest, name, texture, replace: false);
            }
            catch (Exception e) when (e is ContentLoadException or InvalidOperationException or ArgumentException)
            {
                messages.Add($"No se pudo cargar el sprite compilado '{name}': {e.Message}");
            }
        }

        // 2) Sueltos: mandan sobre los compilados.
        AddLooseFolder(device, builder, manifest, looseFolder, messages, replace: true);

        ReportUnused(builder, manifest, messages);
        return builder.Build(device);
    }

    /// <summary>
    /// Solo sprites sueltos: cada .png de una carpeta (sin pipeline). Es el cargador sencillo; para el juego se prefiere LoadContent.
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

        var manifest = LoadManifest(Path.Combine(folder, "sprites.json"), messages);
        AddLooseFolder(device, builder, manifest, folder, messages, replace: false);
        ReportUnused(builder, manifest, messages);
        return builder.Build(device);
    }

    /// <summary>Nombres (sin extensión) de las texturas compiladas (.xnb) de una carpeta de contenido, ordenados.</summary>
    public static List<string> ListContentSprites(string folder)
    {
        if (!Directory.Exists(folder)) return new List<string>();
        return Directory.EnumerateFiles(folder, "*.xnb")
                        .Select(f => Path.GetFileNameWithoutExtension(f))
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .ToList();
    }

    // ------------------------------------------------------------------ comunes

    private static SpriteManifest LoadManifest(string path, List<string> messages)
    {
        if (!File.Exists(path)) return new SpriteManifest();
        try { return SpriteManifest.Parse(File.ReadAllText(path)); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            messages.Add("sprites.json no es valido (" + e.Message + "); se usan los tamaños por defecto.");
            return new SpriteManifest();
        }
    }

    private static void AddTexture(SpriteAtlasBuilder builder, SpriteManifest manifest, string name, Texture2D texture, bool replace)
    {
        var pixels = new Color[texture.Width * texture.Height];
        texture.GetData(pixels);
        var (w, h) = manifest.SizeOf(name, texture.Width, texture.Height);
        var size = new Vector2(w, h);
        if (replace) builder.AddOrReplace(name, texture.Width, texture.Height, pixels, size);
        else builder.Add(name, texture.Width, texture.Height, pixels, size);
    }

    private static void AddLooseFolder(GraphicsDevice device, SpriteAtlasBuilder builder, SpriteManifest manifest,
        string folder, List<string> messages, bool replace)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder, "*.png").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            try
            {
                using var stream = File.OpenRead(file);
                using var texture = Texture2D.FromStream(device, stream);
                AddTexture(builder, manifest, name, texture, replace);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException
                                          or NotSupportedException or UnauthorizedAccessException)
            {
                messages.Add($"No se pudo cargar '{Path.GetFileName(file)}': {e.Message}");
            }
        }
    }

    private static void ReportUnused(SpriteAtlasBuilder builder, SpriteManifest manifest, List<string> messages)
    {
        foreach (var name in manifest.Sprites.Keys.Where(n => !builder.Contains(n)))
            messages.Add($"sprites.json menciona '{name}' pero no hay un sprite con ese nombre");
    }
}
