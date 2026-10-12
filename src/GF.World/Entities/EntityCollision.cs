using GF.Core;
using GF.World.Map;

namespace GF.World.Entities;

/// <summary>
/// Solidez entre entidades (y contra el jugador): cada cuerpo es una caja alineada con los ejes (ancho x alto x ancho, centro de los pies).
/// Resolución por mínima penetración: si dos cajas se solapan, el cuerpo que se está moviendo sale por el eje (X o Z) donde menos se
/// solapan; si cae encima de otra (sus pies casi a la altura de su cabeza) se queda ENCIMA. Sin dependencias de MonoGame.
/// Se usa DESPUÉS de la física de bloques; el llamador debe volver a pasar el desplazamiento por la colisión con bloques
/// (VoxelPhysics.MoveAndCollide con delta = resultado - posición) para no empujar a nadie dentro de una pared.
/// </summary>
public static class EntityCollision
{
    private const double StepOnTop = 0.3;   // si los pies están a menos de esto bajo su cabeza, se sube encima
    private const double Epsilon = 0.001;

    /// <param name="pos">Centro de los pies, en las coordenadas locales del llamador (las de los chunks cargados, sin envolver).</param>
    /// <param name="self">La entidad que se mueve (se ignora a sí misma); null para el jugador.</param>
    /// <param name="scratch">Lista reutilizable para la consulta espacial.</param>
    /// <param name="onTop">true si ha quedado de pie encima de otro cuerpo.</param>
    /// <param name="obstacle">Un cuerpo extra, en las mismas coordenadas que pos (el jugador, visto desde un NPC).</param>
    /// <returns>La posición corregida (igual a pos si no había solape).</returns>
    public static Vec3d PushOut(EntityStore store, WorldScale scale, Vec3d pos, double width, double height, Entity? self,
        List<Entity> scratch, out bool onTop, Vec3d? obstacle = null, double obstacleWidth = 0.6, double obstacleHeight = 1.8)
    {
        onTop = false;
        scratch.Clear();
        store.QueryRadius(new Vec3d(scale.WrapX(pos.X), pos.Y, pos.Z), 3.5, scratch);
        double half = width * 0.5;

        for (int pass = 0; pass < 2; pass++)   // el empujón de uno puede meterte en otro: segunda pasada
        {
            bool moved = false;
            foreach (var other in scratch)
            {
                if (ReferenceEquals(other, self) || other.Tier != SimulationTier.Active) continue;
                double ow = other.Def?.Width ?? 0.6, oh = other.Def?.Height ?? 1.8;
                double ox = pos.X + scale.DeltaX(pos.X, other.Position.X);   // su X canónica, llevada al marco local
                moved |= Separate(ref pos, half, height, ox, other.Position.Y, other.Position.Z, ow * 0.5, oh, ref onTop);
            }
            if (obstacle is { } p)
                moved |= Separate(ref pos, half, height, p.X, p.Y, p.Z, obstacleWidth * 0.5, obstacleHeight, ref onTop);
            if (!moved) break;
        }
        scratch.Clear();
        return pos;
    }

    private static bool Separate(ref Vec3d pos, double half, double height, double ox, double oy, double oz, double ohalf, double oheight,
        ref bool onTop)
    {
        double dx = pos.X - ox, dz = pos.Z - oz;
        double px = half + ohalf - Math.Abs(dx), pz = half + ohalf - Math.Abs(dz);
        if (px <= 0 || pz <= 0) return false;

        double top = oy + oheight;
        if (pos.Y >= top || pos.Y + height <= oy) return false;   // no se solapan en vertical

        if (pos.Y >= top - StepOnTop)   // los pies casi a la altura de su cabeza: te quedas encima
        {
            pos = pos with { Y = top + Epsilon };
            onTop = true;
            return true;
        }

        if (px < pz) pos = pos with { X = pos.X + (dx >= 0 ? px + Epsilon : -(px + Epsilon)) };
        else pos = pos with { Z = pos.Z + (dz >= 0 ? pz + Epsilon : -(pz + Epsilon)) };
        return true;
    }
}
