using System.Numerics;

namespace SupremeCommanderEditor.Rendering;

public class Camera
{
    public Vector3 Target { get; set; }
    public float Distance { get; set; } = 300f;
    public float Yaw { get; set; } = -45f;   // degrees
    public float Pitch { get; set; } = 45f;   // degrees
    public float Fov { get; set; } = 60f;
    public float NearPlane { get; set; } = 1f;
    public float FarPlane { get; set; } = 5000f;

    /// <summary>When true, GetProjectionMatrix returns an orthographic projection sized by OrthoHalfHeight.</summary>
    public bool Orthographic { get; set; }
    /// <summary>Half-height of the ortho frustum in world units. Width is derived from aspect ratio.</summary>
    public float OrthoHalfHeight { get; set; } = 256f;

    public Vector3 Position
    {
        get
        {
            float yawRad = MathF.PI / 180f * Yaw;
            float pitchRad = MathF.PI / 180f * Pitch;
            float cosPitch = MathF.Cos(pitchRad);

            return Target + new Vector3(
                Distance * cosPitch * MathF.Cos(yawRad),
                Distance * MathF.Sin(pitchRad),
                Distance * cosPitch * MathF.Sin(yawRad));
        }
    }

    public Matrix4x4 GetViewMatrix()
    {
        // For top-down ortho (Pitch ~= 90), Vector3.UnitY is parallel to the view direction
        // and CreateLookAt degenerates. Use world -Z as up, so map +Z is screen-down (north up).
        var up = (Orthographic && Pitch > 85f) ? new Vector3(0, 0, -1) : Vector3.UnitY;
        return Matrix4x4.CreateLookAt(Position, Target, up);
    }

    public Matrix4x4 GetProjectionMatrix(float aspectRatio)
    {
        if (Orthographic)
        {
            float halfH = OrthoHalfHeight;
            float halfW = halfH * aspectRatio;
            return Matrix4x4.CreateOrthographic(halfW * 2f, halfH * 2f, NearPlane, FarPlane);
        }
        return Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 180f * Fov, aspectRatio, NearPlane, FarPlane);
    }

    /// <summary>Build a top-down ortho camera that frames the given map dimensions exactly.</summary>
    public static Camera CreateTopDown(int mapWidth, int mapHeight, float maxHeight = 256f)
    {
        return new Camera
        {
            Target = new Vector3(mapWidth / 2f, 0f, mapHeight / 2f),
            Distance = maxHeight + 500f,
            Yaw = 90f,
            Pitch = 90f,
            Orthographic = true,
            OrthoHalfHeight = mapHeight / 2f,
            NearPlane = 1f,
            FarPlane = maxHeight + 2000f,
        };
    }

    public void Orbit(float deltaYaw, float deltaPitch)
    {
        Yaw += deltaYaw;
        Pitch = Math.Clamp(Pitch + deltaPitch, 5f, 89f);
    }

    public void Zoom(float delta)
    {
        Distance = Math.Clamp(Distance * (1f - delta * 0.1f), 10f, 4000f);
    }

    public void Pan(float deltaX, float deltaZ)
    {
        float yawRad = MathF.PI / 180f * Yaw;
        var right = new Vector3(MathF.Sin(yawRad), 0, -MathF.Cos(yawRad));
        var forward = new Vector3(MathF.Cos(yawRad), 0, MathF.Sin(yawRad));

        float speed = Distance * 0.002f;
        Target += right * deltaX * speed + forward * deltaZ * speed;
    }

    public void FitToMap(int mapWidth, int mapHeight)
    {
        Target = new Vector3(mapWidth / 2f, 0, mapHeight / 2f);
        Distance = Math.Max(mapWidth, mapHeight) * 0.8f;
        // 90° looks north (toward -Z), matching the in-game default camera. The compass arrow then
        // points straight up (compass formula: angle = 90 - yaw).
        Yaw = 90f;
        Pitch = 45f;
    }
}
