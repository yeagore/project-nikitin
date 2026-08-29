using Godot;

namespace ProjectNikitin;

/// <summary>
/// Strategy-camera rig. Attached to the CameraRig node. The Camera3D child holds
/// a fixed downward pitch; this script only yaws/translates the rig and slides
/// the camera along its fixed offset direction to zoom, so the angle against the
/// horizontal plane stays locked.
///
/// Controls: WASD pan (relative to facing), Q/E yaw, middle-mouse drag yaw,
/// mouse wheel zoom, hold Shift to accelerate panning.
///
/// Input is polled directly by physical keycode for now; move to InputMap
/// actions once key bindings need to be user-configurable.
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

    /// <summary>
    /// Closest the camera may sit from the rig pivot, in units. ~4x the
    /// hand-framed starting distance (~3), per design.
    /// </summary>
    [Export] public float MinZoomDistance { get; set; } = 12.0f;

    /// <summary>
    /// Farthest the camera may sit from the rig pivot, in units.
    /// ~30x <see cref="MinZoomDistance"/>.
    /// </summary>
    [Export] public float MaxZoomDistance { get; set; } = 360.0f;

    /// <summary>Distance multiplier applied per mouse-wheel notch.</summary>
    [Export] public float ZoomStep { get; set; } = 1.15f;

    private Camera3D _camera = null!;
    private Vector3 _offsetDir;
    private float _distance;

    public override void _Ready()
    {
        _camera = GetNode<Camera3D>("Camera3D");
        Vector3 offset = _camera.Position;
        // The child camera's authored basis is ignored: the offset *direction*
        // sets the pitch, and ApplyZoom() re-aims it at the pivot every time.
        _offsetDir = offset.LengthSquared() > 0.0001f ? offset.Normalized() : new Vector3(0f, 0.7071f, 0.7071f);
        _distance = Mathf.Clamp(offset.Length(), MinZoomDistance, MaxZoomDistance);
        ApplyZoom();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion
            && Input.IsMouseButtonPressed(MouseButton.Middle))
        {
            // Drag right turns the view right (grab-the-world feel).
            RotateY(-motion.Relative.X * MouseYawSpeed);
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

        // Project onto the ground plane so a pitched/tilted rig would still pan flat.
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

    private void ApplyZoom()
    {
        _camera.Position = _offsetDir * _distance;
        // Aim at the rig pivot (this node's origin) regardless of authored basis.
        _camera.LookAt(GlobalPosition, Vector3.Up);
    }

    /// <summary>
    /// Recentre the rig on <paramref name="center"/> (world space) and set the
    /// zoom so a sphere of <paramref name="radius"/> around it fits the view.
    /// Overrides the current pan/zoom — call on load or an explicit "frame" key,
    /// not on every rebuild.
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
