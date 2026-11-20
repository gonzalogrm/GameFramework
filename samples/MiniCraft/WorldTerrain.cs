using GF.World;
using GF.World.Map;

namespace MiniCraft;

/// <summary>
/// El terreno de un mundo: modelo climático (fuente única de verdad de chunks y mapamundi), caché de alturas y biomas por columna, planificador de
/// ciudades y ruinas, y el generador de chunks con todas sus etapas en orden. Sin GPU ni entidades.
/// </summary>
internal sealed class WorldTerrain
{
    public MiniCraftClimate Climate { get; }
    public TerrainColumnCache Columns { get; }
    public CityPlanner Cities { get; }
    public WorldGenerator<ushort> Generator { get; }

    public WorldTerrain(int seed, WorldSettings settings, WorldScale scale)
    {
        // El mismo modelo climático alimenta el terreno de los chunks y el mapamundi.
        Climate = new MiniCraftClimate(seed, settings, scale);
        var climate = Climate;
        StatueFields.Setup((x, z) => MiniCraftClimate.ToBlockHeight(climate.Sample(x, z).Height), climate.SeaLevel,
                           settings.Ruins.FieldChance, settings.Ruins.FieldMinRadius, settings.Ruins.FieldMaxRadius);

        // Altura y bioma de cada columna se calculan una sola vez y los comparten terreno, árboles y decoración.
        Columns = new TerrainColumnCache(Climate, scale);
        var cityOptions = settings.Cities.ToOptions();
        settings.Ruins.Apply(cityOptions);
        Cities = new CityPlanner(seed, Climate, scale, Columns, cityOptions);
        var cities = Cities;
        Task.Run(cities.PlanAll);
        Task.Run(() => { foreach (var f in StatueFields.All(scale, seed)) Console.WriteLine($"[campo] centro x {f.X} z {f.Z}, radio {f.Radius}, {StatueFields.KindName(f.Kind)}"); });

        Generator = new WorldGenerator<ushort>(seed, Climate.Noise)
            .AddStage(new TerrainStage(Climate, Columns))
            .AddStage(new CityStage(Cities, scale, Columns))
            .AddStage(new StatueFieldStage(seed, Cities, scale, Columns, Climate.SeaLevel, settings.Ruins.FieldDensity))
            .AddStage(new TreeStage(Climate, scale, Columns, Cities))
            .AddStage(new DecorationStage(Climate, scale, Columns, Cities))
            .AddStage(new SupportStage(Columns));
    }
}
