using Godot;
using ProjectNikitin.Generation;

namespace ProjectNikitin.Dev;

/// <summary>The cursor's column, read off the mesh's colliders: the first thing to use them.</summary>
public partial class IslandLab
{
	private string _pickText = "";
	private string _fpsText = "";

	/// <summary>
	/// Casts a ray from the camera through the cursor; a hit on a chunk's collider
	/// names the column and what the pipeline said about it. Physics-side, since
	/// that is where the space state is current.
	/// </summary>
	public override void _PhysicsProcess(double delta)
	{
		_pickText = "";
		if (!_showMesh || _data == null || _mesh == null) return;
		Camera3D? cam = GetViewport().GetCamera3D();
		if (cam == null) return;

		Vector2 mouse = GetViewport().GetMousePosition();
		Vector3 from = cam.ProjectRayOrigin(mouse);
		Vector3 to = from + cam.ProjectRayNormal(mouse) * 4000f;
		Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(
			PhysicsRayQueryParameters3D.Create(from, to));
		if (hit.Count == 0) return;

		// A touch inside the solid along the normal, so a side face resolves to its own
		// column and a top to the slab under it rather than the air above.
		Vector3 p = hit["position"].AsVector3() - hit["normal"].AsVector3() * 0.02f;
		Vector3 local = _mesh.ToLocal(p);
		int x = Mathf.RoundToInt(local.X / Terrain.CellSize);
		int z = Mathf.RoundToInt(local.Z / Terrain.CellSize);
		int slab = Mathf.FloorToInt(local.Y / Terrain.SlabHeight);
		if (x < 0 || z < 0 || x >= _data.Size || z >= _data.Size || !_data.HasLand(x, z)) return;
		_pickText = CellSummary(_data, x, z, slab);
	}

	/// <summary>One line on a column: where, what landform and ground, its water, its walk area, and its six habitat bytes.</summary>
	private static string CellSummary(IslandData d, int x, int z, int slab)
	{
		short surface = d.SurfaceLevel(x, z);
		short water = d.WaterLevel[x, z];
		string standing = water != IslandData.NoLand && water > surface
			? (d.Fluid[x, z] == (byte)FluidKind.Goo ? "goo"
				: d.River[x, z] ? (d.Navigable[x, z] ? "navigable river" : "stream") : "lake") + $" to {water}"
			: "dry";

		int walk = d.Walk[x, z];
		string area;
		if (walk == Traversal.Water) area = "water";
		else if (walk < 0) area = "no area";
		else if (walk == d.Mainland) area = "mainland";
		else if (walk < d.Areas.Count && d.Areas[walk].IsDistrict)
			area = "district " + (walk < d.Districts.Count && d.Districts[walk].Length > 0 ? d.Districts[walk] : walk.ToString());
		else area = "broken ground";

		return $"cell {x},{z}  slab {slab}  {((LandformType)d.Landform[x, z]).ToString().ToLowerInvariant()} (patch {d.Region[x, z]})  "
			+ $"surface {surface} {((SurfaceMaterial)d.Material[x, z]).ToString().ToLowerInvariant()}  {standing}  {area}  "
			+ $"moisture {d.Moisture[x, z]}  warmth {d.Warmth[x, z]}  rugged {d.Ruggedness[x, z]}  exposure {d.Exposure[x, z]}  "
			+ $"rim {d.RimDistance[x, z]}  to water {d.WaterDistance[x, z]}  magick {d.Magick[x, z]}"
			+ (d.Spans[x, z].Length > 1 ? "  overhang" : "");
	}
}
