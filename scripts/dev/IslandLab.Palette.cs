using System;
using Godot;
using ProjectNikitin.Generation;

namespace ProjectNikitin.Dev;

/// <summary>The views and their colours.</summary>
public partial class IslandLab
{
	private enum View
	{
		Height,
		Landform,
		Region,
		Walk,
		Reach,
		Shelves,
		Surface,
		Anchors,
		Moisture,
		Warmth,
		Rugged,
		Exposure,
		Rim,
	}

	private static readonly int ViewCount = Enum.GetValues<View>().Length;

	/// <summary>What each view is answering, in one line, next to the picture.</summary>
	private static string ViewLegend(View view) => view switch
	{
		View.Height => "height    low ground dark, high ground pale",
		View.Landform => "landform  green plain / dark hills / grey mountain / "
					   + "brown mesa / blue basin / tan badlands / pale karst / "
					   + "mauve massif / sand dunes / olive sinkholes; yellow = pass",
		View.Region => "region    one hue per patch, borders darkened",
		View.Walk => "walk      what you can cross on foot: green mainland, "
				   + "a hue per other district, grey = broken ground",
		View.Reach => "reach     what you can cross once built: green heartland, "
					+ "red = out of reach whatever you build",
		View.Shelves => "shelves   ground you could settle on; dim brown = level but "
					  + "too small or too narrow",
		View.Surface => "surface   what the ground is made of: grey stone / pale scree / "
					  + "white snow / sand / brown silt / dark green grass (by water) / "
					  + "light green meadow / olive heath / tan dust",
		View.Anchors => "anchors   what the content layer attaches to: cyan coast / "
		   + "red cliff brink / orange cliff foot / teal bank / magenta overhang / "
		   + "sand beach / green ford / yellow gate landing / blue ferry quay / "
		   + "white summit. Unmarked ground is dimmed",
		View.Moisture => "moisture  nearness to fresh water (goo waters nothing): "
		   + "blue waterside, tan parched. What the biome layer will grow things by",
		View.Warmth => "warmth    the altitude lapse, absolute: orange warm lowland, "
		   + "pale frozen. Anchored to the tallest a mountain can be at this size — "
		   + "a flat island is warm all over",
		View.Rugged => "rugged    local relief within two cells: dark flat, pale broken. "
		   + "Measured on the visible surface, so a river is its water, not its bed",
		View.Exposure => "exposure  openness to the wind (from "
		   + "the dune-field direction): pale windswept, dark green lee",
		_ => "rim       cells of land between here and the aether: violet rim, "
		   + "dark interior. Essencecoral country is the violet end",
	};

	private static Color LandformColor(LandformType type) => type switch
	{
		LandformType.Plain => new Color(0.45f, 0.60f, 0.28f),
		LandformType.Hills => new Color(0.30f, 0.44f, 0.20f),
		LandformType.Mountain => new Color(0.52f, 0.50f, 0.55f),
		LandformType.Mesa => new Color(0.68f, 0.45f, 0.26f),
		LandformType.Basin => new Color(0.28f, 0.40f, 0.52f),
		LandformType.Badlands => new Color(0.72f, 0.56f, 0.34f),
		LandformType.Karst => new Color(0.58f, 0.66f, 0.62f),
		LandformType.Massif => new Color(0.62f, 0.42f, 0.48f),
		LandformType.Dunes => new Color(0.80f, 0.74f, 0.46f),
		LandformType.Sinkholes => new Color(0.50f, 0.58f, 0.44f),
		_ => new Color(0.5f, 0.5f, 0.5f),
	};

	/// <summary>A distinct hue per region; the golden-ratio step keeps adjacent ids apart.</summary>
	private static Color RegionColor(int id)
	{
		if (id < 0) return new Color(0.5f, 0.5f, 0.5f);
		float hue = id * 0.61803399f % 1f;
		float sat = 0.45f + (id * 7 % 3) * 0.12f;
		float val = 0.62f + (id * 5 % 4) * 0.09f;
		return Color.FromHsv(hue, sat, val);
	}

	private static readonly Color Unremarkable = new(0.34f, 0.34f, 0.36f);
	private static readonly Color WaterTint = new(0.16f, 0.34f, 0.52f);
	private static readonly Color PassTint = new(0.92f, 0.85f, 0.42f);

	private static readonly Color DeckTint = new(0.95f, 0.72f, 0.30f);
	private static readonly Color BankTint = new(0.99f, 0.94f, 0.55f);
	private static readonly Color StripUsedTint = new(1f, 0.55f, 0.85f);

	private static readonly Color QuayTint = new(0.98f, 0.45f, 0.30f);
	private static readonly Color HullTint = new(0.55f, 0.85f, 0.98f, 0.9f);

	private static readonly Color RoadTint = new(0.98f, 0.95f, 0.62f, 0.8f);
	private static readonly Color StairTint = new(1f, 0.45f, 0.25f);
	private static readonly Color SpanTint = new(1f, 0.80f, 0.20f);
	private static readonly Color CrossingTint = new(0.30f, 0.95f, 0.85f);

	private static readonly Color FordTint = new(0.85f, 0.95f, 0.60f);

	/// <summary>
	/// The feature anchors flattened onto the footprint. Later kinds win where a cell
	/// is several things at once, so a landing on a beach reads as a landing.
	/// </summary>
	private static byte[,] AnchorGrid(IslandData d)
	{
		int n = d.Size;
		var grid = new byte[n, n];

		foreach (Vector2I c in d.CoastCells) grid[c.X, c.Y] = 1;
		foreach (Vector2I c in d.CliffFootCells) grid[c.X, c.Y] = 8;
		foreach (Vector2I c in d.CliffCells) grid[c.X, c.Y] = 2;
		foreach (Vector2I c in d.BankCells) grid[c.X, c.Y] = 9;
		foreach (Vector2I c in d.Overhangs) grid[c.X, c.Y] = 3;

		for (int x = 0; x < n; x++)
		for (int z = 0; z < n; z++)
		{
			if (d.Beach[x, z]) grid[x, z] = 4;
			if (d.Ford[x, z]) grid[x, z] = 5;
			if (d.Landings[x, z]) grid[x, z] = 6;
			if (d.Ferry[x, z]) grid[x, z] = 7;
		}

		foreach (Vector2I c in d.Summits) grid[c.X, c.Y] = 10;
		return grid;
	}

	private static Color AnchorColor(IslandData d, int x, int z, byte[,]? grid)
	{
		if (grid == null) return Unremarkable;
		if (d.WaterLevel[x, z] != IslandData.NoLand) return new Color(0.16f, 0.24f, 0.38f);

		return grid[x, z] switch
		{
			1 => new Color(0.30f, 0.82f, 0.88f),      // coast
			2 => new Color(0.88f, 0.28f, 0.24f),      // cliff brink
			3 => new Color(0.88f, 0.35f, 0.85f),      // overhang / arch
			4 => new Color(0.90f, 0.82f, 0.55f),      // beach
			5 => new Color(0.55f, 0.92f, 0.45f),      // ford
			6 => new Color(0.98f, 0.86f, 0.25f),      // gate landing
			7 => new Color(0.35f, 0.55f, 0.95f),      // ferry quay
			8 => new Color(0.90f, 0.55f, 0.20f),      // cliff foot
			9 => new Color(0.30f, 0.75f, 0.55f),      // bank
			10 => new Color(1f, 1f, 1f),              // summit
			_ => new Color(0.26f, 0.26f, 0.27f),      // unremarkable ground
		};
	}

	/// <summary>A habitat axis as a two-colour ramp.</summary>
	private static Color FieldColor(byte v, Color lo, Color hi) => lo.Lerp(hi, v / 255f);

	private static Color MaterialColor(SurfaceMaterial m) => m switch
	{
		SurfaceMaterial.Stone => new Color(0.46f, 0.46f, 0.48f),
		SurfaceMaterial.Scree => new Color(0.62f, 0.60f, 0.55f),
		SurfaceMaterial.Snow => new Color(0.92f, 0.94f, 0.96f),
		SurfaceMaterial.Sand => new Color(0.85f, 0.78f, 0.55f),
		SurfaceMaterial.Silt => new Color(0.52f, 0.44f, 0.32f),
		SurfaceMaterial.Grass => new Color(0.36f, 0.56f, 0.26f),
		SurfaceMaterial.Meadow => new Color(0.50f, 0.64f, 0.30f),
		SurfaceMaterial.Heath => new Color(0.52f, 0.52f, 0.32f),
		_ => new Color(0.68f, 0.58f, 0.42f),          // Dust
	};

	/// <summary>Walk areas: only districts get a hue; everything smaller is one grey, the broken mass it is.</summary>
	private static Color WalkColor(IslandData d, int id)
	{
		if (id == Traversal.Water) return WaterTint;
		if (id < 0 || id >= d.Areas.Count) return Unremarkable;
		if (!d.Areas[id].IsDistrict) return Unremarkable;
		if (id == d.Mainland) return new Color(0.42f, 0.62f, 0.28f);

		float hue = (0.08f + id * 0.61803399f) % 1f;
		return Color.FromHsv(hue, 0.62f, 0.88f);
	}

	/// <summary>Reach areas: green heartland; red for what stays out of reach whatever you build, warmer the smaller.</summary>
	private static Color ReachColor(IslandData d, int id)
	{
		if (id == Traversal.Water) return WaterTint;
		if (id < 0 || id >= d.Reaches.Count) return Unremarkable;
		if (id == d.Heartland) return new Color(0.42f, 0.62f, 0.28f);

		float t = Mathf.Clamp(d.Reaches[id].Area / 120f, 0f, 1f);
		return new Color(0.86f, 0.22f + 0.26f * t, 0.18f);
	}

	/// <summary>Shelves: a buildable one gets a hue; level-but-too-small ground is dimmed.</summary>
	private static Color ShelfColor(IslandData d, int x, int z)
	{
		if (d.WaterLevel[x, z] != IslandData.NoLand) return new Color(0.16f, 0.34f, 0.52f);

		int id = d.ShelfId[x, z];
		if (id < 0 || id >= d.Shelves.Count) return Unremarkable;

		Shelf shelf = d.Shelves[id];
		if (!shelf.Buildable) return new Color(0.40f, 0.36f, 0.30f);

		float hue = (0.30f + id * 0.61803399f) % 1f;
		return Color.FromHsv(hue, 0.55f, 0.95f);
	}

	/// <summary>Ford, navigable reach, stream and standing water are four colours.</summary>
	private static Color WaterColor(IslandData d, int x, int z)
	{
		if (d.Ford[x, z]) return new Color(0.55f, 0.80f, 0.72f, 0.55f);      // pale, shallow
		if (d.Navigable[x, z]) return new Color(0.10f, 0.45f, 0.60f, 0.85f); // deep, workable
		if (d.River[x, z]) return new Color(0.35f, 0.66f, 0.80f, 0.70f);     // a stream
		return new Color(0.13f, 0.30f, 0.55f, 0.80f);                        // standing water
	}

	private static bool OnRegionBorder(IslandData d, int x, int z)
	{
		int n = d.Size;
		int r = d.Region[x, z];
		if (x == 0 || z == 0 || x == n - 1 || z == n - 1) return true;
		return d.Region[x - 1, z] != r || d.Region[x + 1, z] != r
			|| d.Region[x, z - 1] != r || d.Region[x, z + 1] != r;
	}
}
