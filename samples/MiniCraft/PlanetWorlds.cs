using GF.World.Cosmos;

namespace MiniCraft;

/// <summary>Traduce el clima de un planeta habitable a los ajustes del mundo jugable (determinista: mismo planeta, mismos ajustes).</summary>
public static class PlanetWorlds
{
    public static WorldSettings CreateSettings(WorldSettings baseSettings, Planet planet)
    {
        var s = baseSettings.Clone();
        if (planet.Climate is { } c)
        {
            s.ClimateWarmth += c.Warmth;
            s.ClimateWetness += c.Wetness;
            s.SeaLevelFraction += c.SeaLevelOffset;
            s.LandBias += c.LandBias;
            s.MountainAmplitude *= c.MountainScale;
            s.TreeDensityMultiplier *= c.TreeDensity;
            s.ContinentSize *= c.ContinentScale;
        }
        s.Validate();
        return s;
    }
}
