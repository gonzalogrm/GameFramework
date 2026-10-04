using GF.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GF.Engine;

public interface ICamera
{
    Matrix View { get; }
    Matrix Projection { get; }
    void Update(GameTime gameTime);
}

/// <summary>
/// Cámara 3D con posición en doble precisión. Para renderizar con origen flotante, el renderer pide la vista
/// RELATIVA a un origen (normalmente la propia posición de la cámara) y coloca todo lo demás respecto a él.
/// </summary>
public interface ICamera3D : ICamera
{
    Vec3d Position { get; }
    Matrix ViewRelativeTo(Vec3d origin);
}

public static class Vec3dExtensions
{
    /// <summary>Vector en float desde 'origin' hasta p. Preciso mientras estén cerca entre sí.</summary>
    public static Vector3 RelativeTo(this Vec3d p, Vec3d origin) =>
        new((float)(p.X - origin.X), (float)(p.Y - origin.Y), (float)(p.Z - origin.Z));

    public static Vec3d Add(this Vec3d p, Vector3 v) => new(p.X + v.X, p.Y + v.Y, p.Z + v.Z);
    public static Vector3 ToVector3(this Vec3d p) => new((float)p.X, (float)p.Y, (float)p.Z);
    public static Vec3d ToVec3d(this Vector3 v) => new(v.X, v.Y, v.Z);
}

/// <summary>Yaw 0 mira hacia -Z. Yaw positivo gira a la derecha.</summary>
public sealed class FirstPersonCamera : ICamera3D
{
    private const float PitchLimit = MathHelper.PiOver2 - 0.02f;

    public Vec3d Position { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public float FieldOfView { get; set; } = MathHelper.ToRadians(70f);
    public float AspectRatio { get; set; } = 16f / 9f;
    public float NearPlane { get; set; } = 0.05f;
    public float FarPlane { get; set; } = 500f;

    public Vector3 Forward
    {
        get
        {
            float cp = MathF.Cos(Pitch);
            return new Vector3(MathF.Sin(Yaw) * cp, MathF.Sin(Pitch), -MathF.Cos(Yaw) * cp);
        }
    }

    public Vector3 Right => new(MathF.Cos(Yaw), 0, MathF.Sin(Yaw));

    public Matrix ViewRelativeTo(Vec3d origin)
    {
        var eye = Position.RelativeTo(origin);
        return Matrix.CreateLookAt(eye, eye + Forward, Vector3.Up);
    }

    public Matrix View => ViewRelativeTo(Position);
    public Matrix Projection => Matrix.CreatePerspectiveFieldOfView(FieldOfView, AspectRatio, NearPlane, FarPlane);

    public void Rotate(float deltaX, float deltaY, float sensitivity = 0.0025f)
    {
        Yaw += deltaX * sensitivity;
        Pitch = MathHelper.Clamp(Pitch - deltaY * sensitivity, -PitchLimit, PitchLimit);
    }

    public void SetViewport(Viewport vp) => AspectRatio = vp.AspectRatio;
    public void Update(GameTime gameTime) { }
}

public sealed class OrbitCamera : ICamera3D
{
    public Vec3d Target { get; set; }
    public float Distance { get; set; } = 10f;
    public float Yaw { get; set; }
    public float Pitch { get; set; } = 0.5f;
    public float FieldOfView { get; set; } = MathHelper.ToRadians(60f);
    public float AspectRatio { get; set; } = 16f / 9f;
    public float NearPlane { get; set; } = 0.1f;
    public float FarPlane { get; set; } = 500f;

    public Vec3d Position => Target.Add(Distance * new Vector3(
        MathF.Sin(Yaw) * MathF.Cos(Pitch), MathF.Sin(Pitch), MathF.Cos(Yaw) * MathF.Cos(Pitch)));

    public Matrix ViewRelativeTo(Vec3d origin) =>
        Matrix.CreateLookAt(Position.RelativeTo(origin), Target.RelativeTo(origin), Vector3.Up);

    public Matrix View => ViewRelativeTo(Position);
    public Matrix Projection => Matrix.CreatePerspectiveFieldOfView(FieldOfView, AspectRatio, NearPlane, FarPlane);
    public void Update(GameTime gameTime) { }
}

/// <summary>Cámara 2D en unidades de píxel de mundo. View sirve como transformMatrix de SpriteBatch.</summary>
public sealed class Camera2D : ICamera
{
    public Vector2 Position { get; set; }
    public float Zoom { get; set; } = 1f;
    public float Rotation { get; set; }
    public Point ViewportSize { get; set; } = new(1280, 720);

    /// <summary>Si se asigna, la cámara lo sigue suavemente.</summary>
    public Vector2? FollowTarget { get; set; }
    /// <summary>0 = seguimiento instantáneo.</summary>
    public float FollowSpeed { get; set; } = 8f;

    public Matrix View =>
        Matrix.CreateTranslation(-Position.X, -Position.Y, 0) *
        Matrix.CreateRotationZ(-Rotation) *
        Matrix.CreateScale(Zoom, Zoom, 1) *
        Matrix.CreateTranslation(ViewportSize.X * 0.5f, ViewportSize.Y * 0.5f, 0);

    public Matrix Projection => Matrix.CreateOrthographicOffCenter(0, ViewportSize.X, ViewportSize.Y, 0, 0, 1);

    public Vector2 ScreenToWorld(Vector2 screen) => Vector2.Transform(screen, Matrix.Invert(View));
    public Vector2 WorldToScreen(Vector2 world) => Vector2.Transform(world, View);

    /// <summary>Rectángulo del mundo visible (AABB de la pantalla transformada).</summary>
    public Rectangle VisibleBounds
    {
        get
        {
            var inv = Matrix.Invert(View);
            var a = Vector2.Transform(Vector2.Zero, inv);
            var b = Vector2.Transform(new Vector2(ViewportSize.X, 0), inv);
            var c = Vector2.Transform(new Vector2(ViewportSize.X, ViewportSize.Y), inv);
            var d = Vector2.Transform(new Vector2(0, ViewportSize.Y), inv);
            float minX = MathF.Min(MathF.Min(a.X, b.X), MathF.Min(c.X, d.X));
            float minY = MathF.Min(MathF.Min(a.Y, b.Y), MathF.Min(c.Y, d.Y));
            float maxX = MathF.Max(MathF.Max(a.X, b.X), MathF.Max(c.X, d.X));
            float maxY = MathF.Max(MathF.Max(a.Y, b.Y), MathF.Max(c.Y, d.Y));
            return new Rectangle((int)MathF.Floor(minX), (int)MathF.Floor(minY),
                (int)MathF.Ceiling(maxX - minX), (int)MathF.Ceiling(maxY - minY));
        }
    }

    public void SetViewport(Viewport vp) => ViewportSize = new Point(vp.Width, vp.Height);

    public void Update(GameTime gameTime)
    {
        if (FollowTarget is { } t)
        {
            float k = FollowSpeed <= 0 ? 1f : 1f - MathF.Exp(-FollowSpeed * (float)gameTime.ElapsedGameTime.TotalSeconds);
            Position = Vector2.Lerp(Position, t, k);
        }
    }
}
