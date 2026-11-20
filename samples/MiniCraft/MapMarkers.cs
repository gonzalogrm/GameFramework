using GF.World;
using GF.World.Entities;
using GF.World.Map;
using GF.World.Tiles;
using Microsoft.Xna.Framework;

namespace MiniCraft;

/// <summary>Los marcadores del mapamundi: entidades, ciudades y ruinas conocidas, y campos de estatuas.</summary>
public static class MapMarkers
{
    public static IEnumerable<MapMarker> Build(EntityStore entities, CityPlanner cities, WorldScale scale, int seed)
    {
        foreach (var e in entities.All)
        {
            var def = EntityTypes.Registry.Get(e.TypeId);
            yield return new MapMarker((float)e.Position.X, (float)e.Position.Z, new Color(def.R, def.G, def.B), 5f);
        }

        foreach (var c in cities.Known)
        {
            Color mc = !c.Abandoned ? Color.White
                : c.Site == SiteKind.Temple ? new Color(180, 90, 220)      // templos: violeta
                : c.Site == SiteKind.Statues ? new Color(80, 200, 220)     // estatuas: cian
                : c.Site == SiteKind.Tech ? new Color(150, 170, 255)       // instalaciones tecnológicas: azul claro
                : new Color(220, 150, 50);                                 // ciudades abandonadas: naranja
            yield return new MapMarker(c.CenterX, c.CenterZ, mc, c.Abandoned ? 7f : 9f);
        }

        foreach (var f in StatueFields.All(scale, seed))
            yield return new MapMarker(f.X, f.Z,
                f.Kind == 1 ? new Color(200, 170, 120) : f.Kind == 0 ? new Color(80, 200, 220) : new Color(160, 200, 150), 14f);   // estatuas cian, urnas arena, mixto verde
    }
}
