using GF.World.Cosmos;
using Xunit;

namespace GF.World.Tests;

public class UniverseTests
{
    private const ulong Seed = 123456789UL;

    [Fact]
    public void TheUniverse_HasTheConfiguredShape()
    {
        var universe = new Universe(Seed);
        Assert.Equal(10, universe.Galaxies.Count);
        Assert.All(universe.Galaxies, g => Assert.Equal(100, g.Systems.Count));
        foreach (var system in universe.Galaxies.SelectMany(g => g.Systems))
            Assert.InRange(system.Planets.Count, 1, 5);
    }

    [Fact]
    public void TheSameSeed_GivesExactlyTheSameIds_InAnyOrder()
    {
        var a = new Universe(Seed);
        var b = new Universe(Seed);

        Assert.Equal(a.Galaxies.Select(g => g.Id), b.Galaxies.Select(g => g.Id));
        // Recorrido en orden distinto: se piden primero los últimos.
        var lastA = a.Galaxies[9].Systems[99].Planets;
        var firstB = b.Galaxies[0].Systems[0].Planets;
        var lastB = b.Galaxies[9].Systems[99].Planets;
        Assert.Equal(lastA.Select(p => p.Id), lastB.Select(p => p.Id));
        Assert.Equal(firstB.Select(p => p.Address), a.Galaxies[0].Systems[0].Planets.Select(p => p.Address));
        Assert.Equal(a.Galaxies[3].Systems[42].Id, b.Galaxies[3].Systems[42].Id);
    }

    [Fact]
    public void DifferentSeeds_GiveDifferentUniverses()
    {
        var a = new Universe(1);
        var b = new Universe(2);
        Assert.NotEqual(a.Galaxies[0].Id, b.Galaxies[0].Id);
        Assert.Empty(a.Galaxies.Select(g => g.Id).Intersect(b.Galaxies.Select(g => g.Id)));
    }

    [Fact]
    public void IdsAreUniqueWithinEachLevel()
    {
        var universe = new Universe(Seed);
        Assert.Equal(10, universe.Galaxies.Select(g => g.Id).Distinct().Count());
        var galaxy = universe.Galaxies[0];
        Assert.Equal(100, galaxy.Systems.Select(s => s.Id).Distinct().Count());
        var planets = galaxy.Systems.SelectMany(s => s.Planets).ToList();
        Assert.Equal(planets.Count, planets.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void ThePlanetAddress_RegeneratesTheSamePlanet_WithoutWalkingTheUniverse()
    {
        var universe = new Universe(Seed);
        var planet = universe.Galaxies[4].Systems[77].Planets[0];

        var again = UniverseGenerator.CreatePlanet(planet.Address);   // solo con los cuatro IDs
        Assert.Equal(planet.Name, again.Name);
        Assert.Equal(planet.Kind, again.Kind);
        Assert.Equal(planet.Habitable, again.Habitable);
        Assert.Equal(planet.TemperatureC, again.TemperatureC);
        Assert.Equal(planet.Climate, again.Climate);
        Assert.Equal(planet.Address.WorldSeed, again.Address.WorldSeed);

        Assert.Equal(planet.Address, universe.Find(planet.Address)!.Address);
        Assert.Null(universe.Find(planet.Address with { PlanetId = planet.Id + 1 }));   // IDs que no existen
        Assert.Null(universe.Find(planet.Address with { UniverseId = 5 }));
    }

    [Fact]
    public void TheWorldSeed_DependsOnAllFourIds_AndIsStable()
    {
        var a = new PlanetAddress(1, 2, 3, 4);
        Assert.Equal(a.WorldSeed, new PlanetAddress(1, 2, 3, 4).WorldSeed);
        Assert.NotEqual(a.WorldSeed, (a with { UniverseId = 9 }).WorldSeed);
        Assert.NotEqual(a.WorldSeed, (a with { GalaxyId = 9 }).WorldSeed);
        Assert.NotEqual(a.WorldSeed, (a with { SystemId = 9 }).WorldSeed);
        Assert.NotEqual(a.WorldSeed, (a with { PlanetId = 9 }).WorldSeed);
        Assert.NotEqual(UniverseGenerator.Mix(1, 2), UniverseGenerator.Mix(2, 1));   // el orden importa
    }

    [Fact]
    public void AddressesRoundTripAsText()
    {
        var a = new PlanetAddress(0xDEADBEEFUL, 1, ulong.MaxValue, 42);
        Assert.True(PlanetAddress.TryParse(a.ToString(), out var b));
        Assert.Equal(a, b);
        Assert.False(PlanetAddress.TryParse("no es una dirección", out _));
    }

    [Fact]
    public void HabitablePlanetsExist_AndHomeIsAlwaysTheSame()
    {
        var universe = new Universe(Seed);
        var home = universe.FindHome();
        Assert.NotNull(home);
        Assert.True(home!.Habitable);
        Assert.NotNull(home.Climate);
        Assert.Equal(home.Address, new Universe(Seed).FindHome()!.Address);

        var all = universe.Galaxies.SelectMany(g => g.Systems).SelectMany(s => s.Planets).ToList();
        double habitable = all.Count(p => p.Habitable) / (double)all.Count;
        Assert.InRange(habitable, 0.30, 0.50);                          // alrededor del 40 %
        Assert.All(all.Where(p => !p.Habitable), p => Assert.Null(p.Climate));
        Assert.All(all.Where(p => p.Kind == PlanetKind.GasGiant), p => Assert.False(p.Habitable));
    }

    [Fact]
    public void TheOptionsChangeTheShape_AndAreTheUniversesIdentity()
    {
        var small = new Universe(Seed, new UniverseOptions { GalaxyCount = 3, SystemsPerGalaxy = 10, MinPlanets = 2, MaxPlanets = 2 });
        Assert.Equal(3, small.Galaxies.Count);
        Assert.Equal(10, small.Galaxies[0].Systems.Count);
        Assert.All(small.Galaxies[0].Systems, s => Assert.Equal(2, s.Planets.Count));
        // Las 3 primeras galaxias son las mismas que con las opciones por defecto: crecer el universo no cambia lo ya generado.
        Assert.Equal(new Universe(Seed).Galaxies.Take(3).Select(g => g.Id), small.Galaxies.Select(g => g.Id));
    }
}
