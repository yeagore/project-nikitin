using System;
using Godot;

namespace ProjectNikitin.Dev;

/// <summary>
/// The lab's canvas: a <see cref="GraphEdit"/> that also takes goods dragged in from the
/// palette, and knows where on the canvas a point of itself is.
/// </summary>
public partial class WebGraph : GraphEdit
{
	/// <summary>The key a drag's data carries its good ids under.</summary>
	public const string GoodsKey = "goods";

	/// <summary>Goods dropped from the palette, with the canvas position they were dropped at.</summary>
	public event Action<string[], Vector2>? GoodsDropped;

	/// <summary>The zoom has changed. GraphEdit has no signal of its own for it, so it is watched.</summary>
	public event Action<float>? ZoomChanged;

	private float _watchedZoom = -1f;

	public override void _Process(double delta)
	{
		if (Mathf.IsEqualApprox(Zoom, _watchedZoom)) return;
		_watchedZoom = Zoom;
		ZoomChanged?.Invoke(Zoom);
	}

	/// <summary>Have the zoom reported again next frame: nodes added just now have not been laid out yet.</summary>
	public void RestateZoom() => _watchedZoom = -1f;

	/// <summary>A point of this control, in the coordinates nodes are placed in.</summary>
	public Vector2 ToCanvas(Vector2 local) => (local + ScrollOffset) / Zoom;

	/// <summary>The canvas point in the middle of what is on screen.</summary>
	public Vector2 CanvasCentre => ToCanvas(Size / 2f);

	public override bool _CanDropData(Vector2 atPosition, Variant data) =>
		data.VariantType == Variant.Type.Dictionary && data.AsGodotDictionary().ContainsKey(GoodsKey);

	public override void _DropData(Vector2 atPosition, Variant data)
	{
		string[] ids = data.AsGodotDictionary()[GoodsKey].AsStringArray();
		if (ids.Length > 0) GoodsDropped?.Invoke(ids, ToCanvas(atPosition));
	}

	/// <summary>What a drag from the palette carries.</summary>
	public static Variant DragData(string[] goodIds) =>
		new Godot.Collections.Dictionary { [GoodsKey] = goodIds };

	/// <summary>
	/// Nails a control to the height it has now. A node's ports sit on its children, so a child
	/// that empties itself for the sake of a distant view must not be allowed to shrink with its
	/// contents: that would move the port and every wire on it.
	/// </summary>
	internal static void KeepHeight(Control control)
	{
		if (control.CustomMinimumSize.Y <= 0 && control.Size.Y > 0)
			control.CustomMinimumSize = new Vector2(control.CustomMinimumSize.X, control.Size.Y);
	}
}
