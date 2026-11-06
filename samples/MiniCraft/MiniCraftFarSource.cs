using GF.World.Map;
using GF.World.Voxel;
using Microsoft.Xna.Framework;

namespace MiniCraft;

/// <summary>
/// Terreno lejano de MiniCraft: sale directamente del modelo climático (la misma función que genera los chunks), sin necesidad de
/// tener chunks cargados. Las alturas llegan filtradas según la separación entre muestras (SampleLod), así que montañas y valles
/// se conservan a cualquier distancia y las colinas finas aparecen al acercarte.
/// El COLOR es el color medio de la textura (ya con la paleta del planeta) del bloque de superficie del bioma (Biomes.Surface), así que coincide con
/// lo que se ve al acercarse. Las ciudades aparecen niveladas y en gris (sin edificios) para que no se vea el terreno original por debajo.
/// </summary>
public sealed class MiniCraftFarSource : IFarTerrainSource
{
    private readonly MiniCraftClimate _climate;
    private readonly WorldScale _scale;
    private readonly CityPlanner? _cities;
    private readonly int _zMax;
    private readonly float _landRange;

    public MiniCraftFarSource(MiniCraftClimate climate, WorldScale scale, CityPlanner? cities = null)
    {
        _climate = climate;
        _scale = scale;
        _cities = cities;
        _zMax = scale.HeightBlocks - 1;
        _landRange = Math.Max(1f, climate.Settings.WorldHeight - climate.SeaLevel);
    }

    private static Color Shade(Color c, float f) =>
        new((int)Math.Min(255f, c.R * f), (int)Math.Min(255f, c.G * f), (int)Math.Min(255f, c.B * f), 255);

    public void Sample(int wx, int wz, int spacing, out float height, out Color color)
    {
        wz = Math.Clamp(wz, 0, _zMax);   // más allá de los polos, el borde se repite

        foreach (var city in _cities?.CitiesAt(wx, wz) ?? Array.Empty<City>())
        {
            if (city.Abandoned) continue;   // las ruinas se ven con el terreno natural
            int dx = (int)Math.Round(_scale.DeltaX(city.CenterX, wx)), dz = wz - city.CenterZ;
            if (city.Contains(dx, dz))
            {
                height = city.Target(dx, dz);
                color = new Color(150, 150, 140, 255);
                return;
            }
        }

        var s = _climate.SampleLod(wx, wz, spacing);
        int sea = _climate.SeaLevel;

        if (MiniCraftClimate.ToBlockHeight(s.Height) <= sea)
        {
            // Agua: superficie plana, más oscura cuanto más profundo es el fondo; el color base es el del tile de agua.
            height = sea + 0.5f;
            float depth = Math.Clamp((sea - s.Height) / (sea * 0.7f + 1f), 0f, 1f);
            var water = AtlasFactory.TileColor(Blocks.TileWater);
            color = Color.Lerp(water, Shade(water, 0.5f), depth);
            return;
        }

        height = s.Height;
        ushort top = Biomes.Surface[_climate.Classify(s)];
        var tile = Blocks.Registry.Get(top).FaceTiles[2];   // cara superior
        float k = 0.95f + 0.1f * Math.Clamp((s.Height - sea) / _landRange, 0f, 1f);   // un poco más claro cuanto más alto
        color = Shade(AtlasFactory.TileColor(tile), k);
    }
}
