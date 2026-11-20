using GF.Core;
using GF.Engine;
using GF.World;
using GF.World.Entities;
using GF.World.Voxel;
using GF.World.Map;
using Microsoft.Xna.Framework;

namespace MiniCraft;

/// <summary>El cuerpo del jugador: posición (doble precisión), velocidad, andar / volar / saltar y colisión con bloques y entidades.</summary>
public sealed class PlayerBody
{
    public static readonly Vector3 Size = new(0.6f, 1.8f, 0.6f);

    private readonly WorldScale _scale;
    private readonly int _verticalChunks;
    private readonly List<Entity> _scratch = new();
    private bool _needsUnstuck;

    public PlayerBody(WorldScale scale, int verticalChunks)
    {
        _scale = scale; _verticalChunks = verticalChunks;
    }

    /// <summary>Centro de los pies. Doble precisión: a 100.000 bloques del origen un float pierde 1/128 de bloque.</summary>
    public Vec3d Pos = new(0, 64, 0);
    public Vector3 Vel;
    public bool OnGround;
    /// <summary>Modos de movimiento (F = volar, G = escalón automático).</summary>
    public bool Flying, AutoStep;

    /// <summary>Coloca al jugador: al cargar la columna, si hay algo justo ahí, sube hasta quedar libre.</summary>
    public void Teleport(Vec3d pos)
    {
        Pos = pos;
        Vel = Vector3.Zero;
        OnGround = false;
        _needsUnstuck = true;
    }

    /// <summary>¿Solapa la caja del jugador con esta celda? (para no colocar bloques dentro de él)</summary>
    public bool Intersects(CellCoord c)
    {
        float hx = Size.X * 0.5f, hz = Size.Z * 0.5f;
        return Pos.X - hx < c.X + 1 && Pos.X + hx > c.X &&
               Pos.Y < c.Y + 1 && Pos.Y + Size.Y > c.Y &&
               Pos.Z - hz < c.Z + 1 && Pos.Z + hz > c.Z;
    }

    private bool ColumnLoaded(World<ushort> world)
    {
        var cc = world.Shape.ToChunk(new CellCoord(IntMath.FloorToInt(Pos.X), 0, IntMath.FloorToInt(Pos.Z)));
        for (int cy = 0; cy < _verticalChunks; cy++)
            if (world.GetChunk(new ChunkCoord(cc.X, cy, cc.Z)) == null) return false;
        return true;
    }

    public void Update(InputService input, float dt, float yaw, World<ushort> world, EntityStore entities)
    {
        var fwd = new Vector3(MathF.Sin(yaw), 0, -MathF.Cos(yaw));
        var right = new Vector3(MathF.Cos(yaw), 0, MathF.Sin(yaw));
        var wish = Vector3.Zero;
        if (input.IsDown("Forward")) wish += fwd;
        if (input.IsDown("Back")) wish -= fwd;
        if (input.IsDown("Right")) wish += right;
        if (input.IsDown("Left")) wish -= right;
        if (wish != Vector3.Zero) wish.Normalize();
        bool sprint = input.IsDown("Sprint");

        float speed = Flying ? (sprint ? 40f : 10f) : (sprint ? 40f : 5.5f);
        speed *= StatusEffects.PlayerSpeedFactor;
        Vel.X = wish.X * speed;
        Vel.Z = wish.Z * speed;
        if (Flying)
        {
            // En vuelo no hay gravedad: Espacio sube, Ctrl/C baja.
            float vertical = (input.IsDown("Jump") ? 1f : 0f) - (input.IsDown("Descend") ? 1f : 0f);
            Vel.Y = vertical * (sprint ? 16f : 8f);
        }

        // Física: solo cuando la columna de chunks bajo el jugador ya está cargada
        if (ColumnLoaded(world))
        {
            if (_needsUnstuck)
            {
                for (int guard = 0; guard < 64 && VoxelPhysics.Overlaps(world, Blocks.Registry, Pos, Size); guard++)
                    Pos = Pos with { Y = Pos.Y + 1.0 };
                _needsUnstuck = false;
            }

            if (!Flying)
            {
                Vel.Y -= 28f * dt;
                if (OnGround && input.IsDown("Jump")) Vel.Y = 9f;
            }
            // Escalón automático: solo andando y apoyado en el suelo (en el aire o volando no se sube nada solo).
            double stepHeight = AutoStep && !Flying && OnGround ? 1.0 : 0.0;
            Pos = VoxelPhysics.MoveAndCollide(world, Blocks.Registry, Pos, Size,
                new Vec3d(Vel.X * dt, Vel.Y * dt, Vel.Z * dt), out var flags, stepHeight);
            OnGround = (flags & CollisionFlags.Ground) != 0;
            if ((flags & CollisionFlags.Y) != 0) Vel.Y = 0;

            // Las entidades son sólidas: no se pueden atravesar. Se te saca de su caja; si caes sobre una, te quedas encima.
            var pushed = EntityCollision.PushOut(entities, _scale, Pos, Size.X, Size.Y, null, _scratch, out bool onEntity);
            if (pushed != Pos)
            {
                Pos = VoxelPhysics.MoveAndCollide(world, Blocks.Registry, Pos, Size, pushed - Pos, out _);   // sin meterte en una pared
                if (onEntity) { OnGround = true; if (Vel.Y < 0) Vel.Y = 0; }
            }
        }

        // Mundo cilíndrico: X se envuelve; Z termina en los polos.
        Pos = Pos with { X = _scale.WrapX(Pos.X), Z = Math.Clamp(Pos.Z, 0.5, _scale.HeightBlocks - 0.5) };
    }
}
