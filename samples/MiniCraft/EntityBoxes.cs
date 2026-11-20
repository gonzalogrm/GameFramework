using GF.Core;
using GF.World;
using GF.World.Entities;
using GF.World.Voxel;
using GF.World.Map;
using Microsoft.Xna.Framework;

namespace MiniCraft;

/// <summary>Las entidades activas listas para dibujar, con el color de su tipo atenuado por la luz de su celda.</summary>
public static class EntityBoxes
{
    private static float Brightness(float level) => MathF.Max(0.08f, MathF.Pow(0.82f, 15f - level));

    public static IEnumerable<EntityBox> Build(EntityStore entities, WorldScale scale, Vec3d playerPos, VoxelWorldRenderer renderer,
        World<ushort> world, Vector3 skyLight)
    {
        foreach (var e in entities.Active)
        {
            var def = EntityTypes.Registry.Get(e.TypeId);
            var color = new Color(def.R, def.G, def.B);
            // La luz de su celda (las entidades son canónicas: X relativa al jugador): de noche y en cuevas se oscurecen.
            double x = playerPos.X + scale.DeltaX(playerPos.X, e.Position.X);
            var cell = new CellCoord(IntMath.FloorToInt(x), IntMath.FloorToInt(e.Position.Y + 0.5), IntMath.FloorToInt(e.Position.Z));
            // Sin el terreno de su chunk construido y ya visible, la entidad flotaría sobre el vacío (aparecía y desaparecía a lo lejos).
            if (!renderer.IsFullyVisible(world.Shape.ToChunk(cell))) continue;
            var lv = renderer.Light.Sample(cell);
            // Sin dato de luz todavía se supone cielo abierto (de noche, oscuro): antes se dibujaba a pleno brillo.
            var t = lv.Known ? lv.Total : Vector3.Max(lv.Ambient, skyLight * LightField.Max);
            color = new Color(color.ToVector3() * new Vector3(Brightness(t.X), Brightness(t.Y), Brightness(t.Z)));
            yield return new EntityBox(e.Position, new Vector3(def.Width, def.Height, def.Width), color);
        }
    }
}
