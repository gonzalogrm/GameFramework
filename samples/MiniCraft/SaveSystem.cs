using System.Text.Json;
using System.Text.Json.Serialization;
using GF.World.Cosmos;
using GF.World.Entities;
using GF.World.Items;

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
    /// <summary>Hora del día (0..1) del ciclo día/noche.</summary>
    public double TimeOfDay { get; set; } = 0.38;
    /// <summary>Vida, inventario y equipamiento del jugador (null en guardados antiguos).</summary>
    public PlayerSave? Player { get; set; }
    /// <summary>Configuración con la que se generó este mundo (no cambia aunque edites worldsettings.json).</summary>
    public WorldSettings Settings { get; set; } = new();
    /// <summary>Bloques en orden de id. El guardado es válido si es un prefijo del registro actual.</summary>
    public string[] Blocks { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Qué hay guardado del UNIVERSO (universe.json): su semilla, sus opciones y el planeta donde se jugaba. Cada planeta habitable visitado es un mundo
/// aparte, con su carpeta (world.json + regions/ + entities.json).
/// </summary>
public sealed class UniverseSave
{
    public ulong Seed { get; set; }
    public UniverseOptions Options { get; set; } = new();
    public PlanetAddress? Current { get; set; }
}

/// <summary>Guardado en %AppData%/MiniCraft/universe: universe.json y una carpeta por planeta (planets/galaxia-sistema-planeta/).</summary>
public static class SaveSystem
{
    public const int CurrentVersion = 4;   // 4: configuración del mundo guardada con la partida

    public static string UniverseRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniCraft", "universe");

    // Carpeta del mundo (planeta) con el que se trabaja. La fija quien abre un planeta (UseDirectory); cada PlayScene la vuelve a fijar antes de guardar,
    // para que un cambio de planeta no escriba en la carpeta equivocada.
    private static string _root = Path.Combine(UniverseRoot, "planets", "none");
    public static string Root => _root;
    public static void UseDirectory(string path) => _root = path;

    /// <summary>Carpeta de guardado de un planeta. El universo es único por partida, así que bastan los IDs de galaxia, sistema y planeta.</summary>
    public static string PlanetDir(PlanetAddress a) => Path.Combine(UniverseRoot, "planets", $"{a.GalaxyId:x16}-{a.SystemId:x16}-{a.PlanetId:x16}");

    private static string UniversePath => Path.Combine(UniverseRoot, "universe.json");

    public static UniverseSave? TryLoadUniverse()
    {
        try
        {
            return File.Exists(UniversePath) ? JsonSerializer.Deserialize<UniverseSave>(File.ReadAllText(UniversePath)) : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void WriteUniverse(UniverseSave data)
    {
        Directory.CreateDirectory(UniverseRoot);
        var tmp = UniversePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, UniversePath, overwrite: true);
    }

    /// <summary>Borra el universo entero con todos sus planetas guardados.</summary>
    public static void DeleteUniverse()
    {
        if (Directory.Exists(UniverseRoot)) Directory.Delete(UniverseRoot, recursive: true);
    }
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
        // Sin campos nulos: una entidad sin cambios ocupa solo su tipo, posición e id.
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }));
        File.Move(tmp, EntitiesPath, overwrite: true);
    }
}

public sealed class EntitySave
{
    public double Time { get; set; }
    public List<EntityRecord> Entities { get; set; } = new();
    /// <summary>Columnas de chunks que ya han generado sus enemigos, como tramos [z, x0, x1] (para no repetirlos al volver).</summary>
    public List<int[]> Columns { get; set; } = new();
}

public sealed class PlayerSave
{
    public float Hp { get; set; }
    public List<StackRecord>? Inventory { get; set; }
    public List<EquipRecord>? Gear { get; set; }
}
