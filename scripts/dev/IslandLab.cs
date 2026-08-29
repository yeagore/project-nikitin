using System;
using System.Collections.Generic;
using Godot;
using ProjectNikitin;
using ProjectNikitin.Generation;

namespace ProjectNikitin.Dev;

/// <summary>
/// Dev harness for island generation. Open <c>scenes/dev/island_lab.tscn</c> and
/// run it (F6). Edit the <see cref="Params"/> resource (or <see cref="Seed"/>)
/// in the running scene's remote inspector — the island rebuilds automatically
/// when a value changes.
///
/// Camera: WASD move, Q/E or middle-mouse-drag rotate, wheel zoom, Shift for
/// faster pan (see <see cref="CameraRig"/>). <b>F</b> re-frames the island,
/// <b>R</b> forces a regenerate.
///
/// Renders one scaled <c>MultiMesh</c> box per span (keel → surface), in slab
/// units — no mesher, no per-face culling; that comes later. NOT a <c>[Tool]</c>
/// script: generating in-editor bakes the MultiMesh buffer into the scene file.
/// </summary>
public partial class IslandLab : Node3D
{
    [Export] public int Seed { get; set; } = 1337;
    [Export] public IslandParams Params { get; set; } = null!;

    private MultiMeshInstance3D _terrain = null!;
    private CameraRig _rig = null!;
    private BoxMesh _unitBox = null!;
    private int _lastSignature;

    private Vector3 _islandCenter = Vector3.Zero;
    private float _islandRadius = 10f;
    private bool _framedOnce;

    public override void _Ready()
    {
        _terrain = GetNode<MultiMeshInstance3D>("Terrain");
        _rig = GetNode<CameraRig>("CameraRig");
        _unitBox = new BoxMesh { Size = Vector3.One };
        _unitBox.Material = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 1f,
        };
        AddControlsHint();
        Rebuild();
    }

    public override void _Process(double delta)
    {
        if (Signature() != _lastSignature)
            Rebuild();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.Keycode)
        {
            case Key.R: Rebuild(); break;
            case Key.F: _rig.Frame(_islandCenter, _islandRadius); break;
        }
    }

    private int Signature()
    {
        var h = new HashCode();
        h.Add(Seed);
        if (Params != null)
        {
            h.Add(Params.Size);
            h.Add(Params.Radius);
            h.Add(Params.Coverage);
            h.Add(Params.Fragmentation);
            h.Add(Params.Relief);
            h.Add(Params.Roughness);
            h.Add(Params.HeightScale);
            h.Add(Params.TerraceCount);
            h.Add(Params.TerraceGrip);
            h.Add(Params.RimDepth);
            h.Add(Params.RimFalloff);
        }
        return h.ToHashCode();
    }

    private void Rebuild()
    {
        if (_terrain == null || _unitBox == null) return;
        Params ??= new IslandParams();
        _lastSignature = Signature();

        ulong t0 = Time.GetTicksUsec();
        IslandData data = new IslandGenerator().Generate(Seed, Params);
        int spans = RenderSpans(data);
        GD.Print($"[IslandLab] seed {Seed}, {Params.Size}² -> {spans} spans "
            + $"in {(Time.GetTicksUsec() - t0) / 1000f:0.0} ms");

        if (!_framedOnce)
        {
            _rig.Frame(_islandCenter, _islandRadius);
            _framedOnce = true;
        }
    }

    private int RenderSpans(IslandData d)
    {
        int n = d.Size;
        float half = n * 0.5f;
        const float sh = Terrain.SlabHeight;
        const float cs = Terrain.CellSize;

        var xf = new List<Transform3D>();
        var col = new List<Color>();

        int topMax = 1, topMin = 0;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            Span[] spans = d.Spans[x, z];
            if (spans == null) continue;
            foreach (Span s in spans)
            {
                topMax = Math.Max(topMax, s.Top);
                topMin = Math.Min(topMin, s.Top);
            }
        }
        float tintSpan = Math.Max(1, topMax - topMin);

        var low = new Color(0.24f, 0.20f, 0.13f);   // deep / dirt
        var mid = new Color(0.30f, 0.42f, 0.18f);   // grass
        var high = new Color(0.66f, 0.72f, 0.52f);  // highlands

        var bbMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var bbMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            Span[] spans = d.Spans[x, z];
            if (spans == null) continue;

            foreach (Span s in spans)
            {
                float hWorld = s.Height * sh;
                float yCenter = (s.Bottom + s.Top + 1) * 0.5f * sh;
                var origin = new Vector3((x - half) * cs, yCenter, (z - half) * cs);

                xf.Add(new Transform3D(
                    Basis.Identity.Scaled(new Vector3(cs, hWorld, cs)), origin));

                float t = Mathf.Clamp((s.Top - topMin) / tintSpan, 0f, 1f);
                col.Add(t < 0.5f ? low.Lerp(mid, t * 2f) : mid.Lerp(high, (t - 0.5f) * 2f));

                var ext = new Vector3(cs * 0.5f, hWorld * 0.5f, cs * 0.5f);
                bbMin = bbMin.Min(origin - ext);
                bbMax = bbMax.Max(origin + ext);
            }
        }

        if (xf.Count > 0)
        {
            _islandCenter = (bbMin + bbMax) * 0.5f;
            _islandRadius = Mathf.Max(1f, (bbMax - bbMin).Length() * 0.5f);
        }

        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = _unitBox,
            InstanceCount = xf.Count,
        };
        for (int i = 0; i < xf.Count; i++)
        {
            mm.SetInstanceTransform(i, xf[i]);
            mm.SetInstanceColor(i, col[i]);
        }
        _terrain.Multimesh = mm;
        return xf.Count;
    }

    private void AddControlsHint()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        var label = new Label
        {
            Text = "WASD move   Q/E rotate   MMB-drag rotate   wheel zoom   Shift faster"
                 + "\nF frame island   R regenerate",
            Position = new Vector2(12, 8),
        };
        label.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.85f));
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.6f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        layer.AddChild(label);
    }
}
