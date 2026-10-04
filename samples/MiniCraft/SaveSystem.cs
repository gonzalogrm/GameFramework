using System.Text.Json;
using GF.World.Entities;

namespace MiniCraft;

public sealed class SaveData
{
    /// <summary>Versión del formato/generación. Si cambia el generador de terreno, súbela: invalida guardados viejos.</summary>
    public int Version { get; set; }
    public int Seed { get; set; }
    // Posición en double: es la precisión con la que se simula (los guardados antiguos con float se leen sin problema).
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public int Selected { get; set; }
    /// <summary>Configuración con la que se generó este mundo (no cambia aunque edites worldsettings.json).</summary>
    public WorldSettings Settings { get; set; } = new();
    /// <summary>Bloques en orden de id. El guardado es válido si es un prefijo del registro actual.</summary>
    public string[] Blocks { get; set; } = Array.Empty<string>();
}

/// <summary>Un único mundo guardado en %AppData%/MiniCraft/world (world.json + chunks/).</summary>
public static class SaveSystem
{
    public const int CurrentVersion = 4;   // 4: configuración del mundo guardada con la partida

    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniCraft", "world");
    public static string ChunksDir => Path.Combine(Root, "chunks");     // formato anterior: un archivo por chunk (solo lectura)
    public static string RegionsDir => Path.Combine(Root, "regions");   // formato actual: un archivo por región
    private static string JsonPath => Path.Combine(Root, "world.json");

    public static SaveData? TryLoad()
    {
        try
        {
            if (!File.Exists(JsonPath)) return null;
            var data = JsonSerializer.Deserialize<SaveData>(File.ReadAllText(JsonPath));
            if (data == null || data.Version != CurrentVersion) return null;
            data.Settings ??= new WorldSettings();
            data.Settings.Validate();
            var current = Blocks.Registry.NamesInOrder().ToArray();
            if (data.Blocks.Length > current.Length || !current.Take(data.Blocks.Length).SequenceEqual(data.Blocks)) return null;
            return data;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Write(SaveData data)
    {
        Directory.CreateDirectory(Root);
        data.Version = CurrentVersion;
        data.Blocks = Blocks.Registry.NamesInOrder().ToArray();
        var tmp = JsonPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, JsonPath, overwrite: true);
    }

    public static void Delete()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }

    private static string EntitiesPath => Path.Combine(Root, "entities.json");

    public static EntitySave? TryLoadEntities()
    {
        try
        {
            return File.Exists(EntitiesPath) ? JsonSerializer.Deserialize<EntitySave>(File.ReadAllText(EntitiesPath)) : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void WriteEntities(EntitySave data)
    {
        Directory.CreateDirectory(Root);
        var tmp = EntitiesPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data));
        File.Move(tmp, EntitiesPath, overwrite: true);
    }
}

public sealed class EntitySave
{
    public double Time { get; set; }
    public List<EntityRecord> Entities { get; set; } = new();
}
