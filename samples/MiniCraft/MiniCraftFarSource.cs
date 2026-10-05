using GF.World.Map;
using GF.World.Voxel;
using Microsoft.Xna.Framework;

namespace MiniCraft;

/// <summary>
/// Terreno lejano de MiniCraft: sale directamente del modelo climático (la misma función que genera los chunks), sin necesidad de
/// tener chunks cargados. Las alturas llegan filtradas según la separación entre muestras (SampleLod), así que montañas y valles
/// se conservan a cualquier distancia y las colinas finas aparecen al acercarte. El color es el del bioma, como en el mapamundi.
/// </summary>
public sealed class MiniCraftFarSource : IFarTerrainSource
{
    private readonly MiniCraftClimate _climate;
    private readonly int _zMax;
    private readonly float _landRange;

    public MiniCraftFarSource(MiniCraftClimate climate, WorldScale scale)
    {
        _climate = climate;
        _zMax = scale.HeightBlocks - 1;
        _landRange = Math.Max(1f, climate.Settings.WorldHeight - climate.SeaLevel);
    }

    public void Sample(int wx, int wz, int spacing, out float height, out Color color)
    {
        wz = Math.Clamp(wz, 0, _zMax);   // más allá de los polos, el borde se repite
        var s = _climate.SampleLod(wx, wz, spacing);
        int sea = _climate.SeaLevel;

        if (MiniCraftClimate.ToBlockHeight(s.Height) <= sea)
        {
            // Agua: superficie plana, más oscura cuanto más profundo es el fondo.
            height = sea + 0.5f;
            float depth = Math.Clamp((sea - s.Height) / (sea * 0.7f + 1f), 0f, 1f);
            color = Color.Lerp(new Color(58, 110, 195), new Color(18, 48, 130), depth);
            return;
        }

        height = s.Height;
        var biome = Biomes.Registry.Get(_climate.Classify(s));
        float k = 0.88f + 0.22f * Math.Clamp((s.Height - sea) / _landRange, 0f, 1f);   // más claro cuanto más alto
        color = new Color((int)Math.Min(255f, biome.R * k), (int)Math.Min(255f, biome.G * k), (int)Math.Min(255f, biome.B * k), 255);
    }
}
