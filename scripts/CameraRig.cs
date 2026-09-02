using Godot;

namespace ProjectNikitin;

/// <summary>
/// Strategy-camera rig: the rig yaws and translates, the child Camera3D orbits it on
/// a spherical offset whose pitch and radius this script owns, re-aimed with LookAt.
/// The child's authored position seeds the start; its authored basis is ignored.
/// Pitch runs below the horizon so the keel can be inspected. Keys are polled by
/// physical keycode; move to InputMap once bindings need to be configurable.
/// </summary>
public partial class CameraRig : Node3D
{
    /// <summary>Ground-plane pan speed at the closest zoom, units per second.</summary>
    [Export] public float PanSpeed { get; set; } = 5.0f;

    /// <summary>Multiplier applied to pan speed while Shift is held.</summary>
    [Export] public float FastMultiplier { get; set; } = 3.0f;

    /// <summary>Yaw speed for Q / E, radians per second.</summary>
    [Export] public float KeyYawSpeed { get; set; } = 1.8f;

    /// <summary>Yaw applied per pixel of middle-mouse drag, radians.</summary>
    [Export] public float MouseYawSpeed { get; set; } = 0.006f;

    /// <summary>Pitch applied per pixel of middle-mouse drag, radians.</summary>
    [Export] public float MousePitchSpeed { get; set; } = 0.005f;

    /// <summary>Pitch speed for the up/down arrows, radians per second.</summary>
    [Export] public float KeyPitchSpeed { get; set; } = 1.2f;

    /// <summary>Lowest tilt, degrees; negative looks up at the keel. Off ±90 so the LookAt up-vector never degenerates.</summary>
    [Export(PropertyHint.Range, "-89,0,1")] public float MinPitchDegrees { get; set; } = -80.0f;

    /// <summary>Highest the camera may tilt, in degrees (near top-down).</summary>
    [Export(PropertyHint.Range, "0,89,1")] public float MaxPitchDegrees { get; set; } = 85.0f;

    /// <summary>Closest the camera may sit from the pivot, units.</summary>
    [Export] public float MinZoomDistance { get; set; } = 12.0f;

    /// <summary>Farthest the camera may sit from the pivot, units.</summary>
    [Export] public float MaxZoomDistance { get; set; } = 360.0f;

    /// <summary>Distance multiplier applied per mouse-wheel notch.</summary>
    [Export] public float ZoomStep { get; set; } = 1.15f;

    private Camera3D _camera = null!;
    private Vector3 _flatDir = Vector3.Back;
    private float _pitch;
    private float _distance;

    public override void _Ready()
    {
        _camera = GetNode<Camera3D>("Camera3D");
        Vector3 offset = _camera.Position;
        if (offset.LengthSquared() < 0.0001f)
            offset = new Vector3(0f, 0.7071f, 0.7071f);

        var flat = new Vector3(offset.X, 0f, offset.Z);
        _flatDir = flat.LengthSquared() > 0.0001f ? flat.Normalized() : Vector3.Back;
        _pitch = Mathf.Atan2(offset.Y, flat.Length());
        _distance = Mathf.Clamp(offset.Length(), MinZoomDistance, MaxZoomDistance);
        SetPitch(_pitch);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion
            && Input.IsMouseButtonPressed(MouseButton.Middle))
        {
            // Drag right turns the view right; drag down lifts towards top-down.
            RotateY(-motion.Relative.X * MouseYawSpeed);
            SetPitch(_pitch + motion.Relative.Y * MousePitchSpeed);
        }
        else if (@event is InputEventMouseButton button && button.Pressed)
        {
            if (button.ButtonIndex == MouseButton.WheelUp)
                Zoom(1.0f / ZoomStep);
            else if (button.ButtonIndex == MouseButton.WheelDown)
                Zoom(ZoomStep);
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        float yaw = 0.0f;
        if (Input.IsPhysicalKeyPressed(Key.E))
            yaw += 1.0f;
        if (Input.IsPhysicalKeyPressed(Key.Q))
            yaw -= 1.0f;
        if (yaw != 0.0f)
            RotateY(yaw * KeyYawSpeed * dt);

        float pitch = 0.0f;
        if (Input.IsPhysicalKeyPressed(Key.Down))
            pitch += 1.0f;
        if (Input.IsPhysicalKeyPressed(Key.Up))
            pitch -= 1.0f;
        if (pitch != 0.0f)
            SetPitch(_pitch + pitch * KeyPitchSpeed * dt);

        var localDir = Vector3.Zero;
        if (Input.IsPhysicalKeyPressed(Key.W))
            localDir.Z -= 1.0f;
        if (Input.IsPhysicalKeyPressed(Key.S))
            localDir.Z += 1.0f;
        if (Input.IsPhysicalKeyPressed(Key.A))
            localDir.X -= 1.0f;
        if (Input.IsPhysicalKeyPressed(Key.D))
            localDir.X += 1.0f;
        if (localDir == Vector3.Zero)
            return;

        float speed = PanSpeed;
        if (Input.IsPhysicalKeyPressed(Key.Shift))
            speed *= FastMultiplier;
        // Scale with zoom so panning stays usable when zoomed far out.
        speed *= _distance / MinZoomDistance;

        // Project onto the ground plane so a pitched rig still pans flat.
        Vector3 worldDir = GlobalBasis * localDir;
        worldDir.Y = 0.0f;
        if (worldDir.Length() > 0.0001f)
            GlobalPosition += worldDir.Normalized() * speed * dt;
    }

    private void Zoom(float factor)
    {
        _distance = Mathf.Clamp(_distance * factor, MinZoomDistance, MaxZoomDistance);
        ApplyZoom();
    }

    private void SetPitch(float radians)
    {
        _pitch = Mathf.Clamp(radians,
            Mathf.DegToRad(MinPitchDegrees), Mathf.DegToRad(MaxPitchDegrees));
        ApplyZoom();
    }

    // The single placement routine: offset the camera and aim it at the pivot.
    private void ApplyZoom()
    {
        if (_camera == null) return;
        Vector3 dir = (_flatDir * Mathf.Cos(_pitch) + Vector3.Up * Mathf.Sin(_pitch)).Normalized();
        _camera.Position = dir * _distance;
        _camera.LookAt(GlobalPosition, Vector3.Up);
    }

    /// <summary>
    /// Recentre on <paramref name="center"/> and zoom so a sphere of <paramref name="radius"/>
    /// fits the view. Overrides the current pan/zoom — call on load or an explicit frame key.
    /// </summary>
    public void Frame(Vector3 center, float radius)
    {
        if (_camera == null) return;
        GlobalPosition = center;
        float halfFov = Mathf.DegToRad(_camera.Fov) * 0.5f;
        float fit = radius / Mathf.Tan(halfFov) * 1.4f;
        _distance = Mathf.Clamp(fit, MinZoomDistance, MaxZoomDistance);
        ApplyZoom();
    }
}
