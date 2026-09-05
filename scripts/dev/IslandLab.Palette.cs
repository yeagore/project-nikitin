using System;
using System.Collections.Generic;
using Godot;
using ProjectNikitin.Generation;

namespace ProjectNikitin.Dev;

/// <summary>The views, their colours, and the legend that shows those colours.</summary>
public partial class IslandLab
{
	private enum View
	{
		Height,
		Landform,
		Region,
		Walk,
		Reach,
		Surface,
		Anchors,
		Moisture,
		Warmth,
		Rugged,
		Exposure,
		Rim,
		Water,
		Magick,
	}

	private static readonly int ViewCount = Enum.GetValues<View>().Length;

	/// <summary>A colour swatch marker, laid out by <see cref="ShowLegend"/>: the legend shows the colour, not a word for it.</summary>
	private static string Swatch(Color c) => $"{{#{c.ToHtml(false)}}}";

	/// <summary>A swatch followed by what it means.</summary>
	private static string Keyed(Color c, string meaning) => $"{Swatch(c)} {meaning}";

	/// <summary>Five swatches from one end of a ramp to the other.</summary>
	private static string Ramp(Color lo, Color hi)
	{
		var bits = new List<string>();
		for (int i = 0; i <= 4; i++) bits.Add(Swatch(lo.Lerp(hi, i / 4f)));
		return string.Join("", bits);
	}

	private static string Ramp((Color Lo, Color Hi) ramp) => Ramp(ramp.Lo, ramp.Hi);

	/// <summary>What each view is answering, with its actual colours, next to the picture.</summary>
	private static string ViewLegend(View view)
	{
		switch (view)
		{
			case View.Height:
				return $"[b]height[/b]   {Ramp(DevPalette.HeightLow, DevPalette.HeightMid)}{Ramp(DevPalette.HeightMid, DevPalette.HeightHigh)}"
					+ "  low ground dark, high ground pale";

			case View.Landform:
			{
				var bits = new List<string>();
				foreach (LandformType t in Enum.GetValues<LandformType>())
					bits.Add(Keyed(LandformColor(t), t.ToString().ToLowerInvariant()));
				bits.Add(Keyed(LandformColor(LandformType.Plain).Lerp(PassTint, 0.55f), "pass (tinted)"));
				return "[b]landform[/b]   " + string.Join("   ", bits);
			}

			case View.Region:
				return "[b]region[/b]   one hue per patch, borders darkened   "
					+ Keyed(RegionColor(3), "a patch") + "   " + Keyed(RegionColor(3).Darkened(0.55f), "its border");

			case View.Walk:
				return "[b]walk[/b]   what you can cross on foot, corners cut unless both sides are cliffs; "
					+ $"a district ({Traversal.MinDistrictArea}+ cells) is somewhere to build   "
					+ Keyed(MainlandTint, "mainland") + "   a hue per other district   "
					+ Keyed(Unremarkable, "broken ground") + "   " + Keyed(WaterTint, "water");

			case View.Reach:
				return "[b]reach[/b]   what you can cross once built   "
					+ Keyed(MainlandTint, "heartland") + "   "
					+ Ramp(ReachColor(0f), ReachColor(1f)) + " out of reach whatever you build, "
					+ "warmer the smaller   " + Keyed(WaterTint, "water");

			case View.Surface:
			{
				var bits = new List<string>();
				foreach (SurfaceMaterial m in Enum.GetValues<SurfaceMaterial>())
					bits.Add(Keyed(MaterialColor(m), m.ToString().ToLowerInvariant()));
				return "[b]surface[/b]   what the ground is made of   " + string.Join("   ", bits)
					+ "   (an overhang's lip is drawn as stone)";
			}

			case View.Anchors:
			{
				var bits = new List<string>();
				foreach (int kind in DevPalette.LegendOrder)
					bits.Add(Keyed(DevPalette.Anchor(kind), DevPalette.AnchorName(kind)));
				bits.Add(Keyed(DevPalette.Anchor(0), "unremarkable ground"));
				return "[b]anchors[/b]   what the content layer attaches to. The lists overlap; "
					+ "here the built and rarer kinds win, and a cell that is both brink and foot "
					+ "is a ledge   " + string.Join("   ", bits)
					+ "   Only the lip of an overhang is magenta: the ground under it is its own kind. "
					+ "Beds show with liquid off (I). A sea stack is a dark column in the aether, in every view.";
			}

			case View.Moisture:
				return $"[b]moisture[/b]   {Ramp(DevPalette.MoistureRamp)}  parched … waterside: the "
					+ "Domain's background moisture in patches; the lee in the wind's rain shadow, and "
					+ "sheltered broken ground (a gorge floor) damper, both by the wind knob; rock and its "
					+ "fringe with patches of drought; plus what fresh water adds along a walk from it "
					+ "(two cells more per slab climbed, so a river waters the plain it crosses and "
					+ "not the mountain it passes)";

			case View.Warmth:
			{
				var stops = new List<string>();
				foreach (byte w in new byte[] { 0, 64, 110, 150, 190, 205, 220, 235, 255 })
					stops.Add(Swatch(DevPalette.WarmthTint(w)));
				return $"[b]warmth[/b]   {string.Join("", stops)}  frozen … cold (blue) … temperate "
					+ "(yellow) … hot (orange): one climate over the whole island, then the lapse over a "
					+ "mountain's upper part; a slope facing the sun (compass overlay, X) a touch warmer and "
					+ "one facing away colder; basins and sinkhole pits frost hollows; the lee milder by the "
					+ "wind knob, the rim colder, wet ground tempered";
			}

			case View.Rugged:
				return $"[b]rugged[/b]   {Ramp(DevPalette.RuggedRamp)}  flat … broken: local relief within "
					+ "two cells. Water is read as its bank, a slab over its surface, so a stream through "
					+ "a plain is flat country and a gorge is still its walls";

			case View.Exposure:
				return $"[b]exposure[/b]   {Ramp(DevPalette.ExposureRamp)}  lee … windswept: openness to "
					+ "the Domain's one wind (compass overlay, X, shows it), dunes or not";

			case View.Rim:
				return $"[b]rim[/b]   {Ramp(DevPalette.RimRamp)}  rim … interior: cells of land between "
					+ "here and the aether. Essencecoral country is the violet end";

			case View.Water:
				return $"[b]water distance[/b]   {Ramp(DevPalette.WaterRamp)}  bank … out of reach: the walk "
					+ "cost to fresh water the moisture strip reads (a cell per cell along or down, two more "
					+ "per slab up), kept as a byte for the settlement and biome layers; shown to 60";

			default:
				return $"[b]magick[/b]   {Ramp(DevPalette.MagickRamp)}  inert … saturated: the magickal "
					+ "density layer. For now pure noise in soft waves, read by nothing";
		}
	}

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
	private static readonly Color MainlandTint = new(0.42f, 0.62f, 0.28f);

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
	private static readonly Color WindTint = new(0.98f, 0.62f, 0.30f);
	private static readonly Color SunTint = new(1.00f, 0.90f, 0.35f);

	/// <summary>
	/// The feature anchors flattened onto the footprint, ground span only. Later
	/// kinds win where a cell is several things at once, so a landing on a beach
	/// reads as a landing and a ford reads over the bed it crosses. Overhangs are
	/// not here: a lip is coloured per span in <see cref="AnchorColor"/>.
	/// </summary>
	private static byte[,] AnchorGrid(IslandData d)
	{
		int n = d.Size;
		var grid = new byte[n, n];

		foreach (Vector2I c in d.RiverBedCells) grid[c.X, c.Y] = DevPalette.RiverBed;
		foreach (Vector2I c in d.LakeBedCells) grid[c.X, c.Y] = DevPalette.LakeBed;
		foreach (Vector2I c in d.CoastCells) grid[c.X, c.Y] = DevPalette.Coast;
		foreach (Vector2I c in d.CliffFootCells) grid[c.X, c.Y] = DevPalette.CliffFoot;
		// A bench on a mountainside is a brink over one neighbour and a foot under another.
		foreach (Vector2I c in d.CliffCells)
			grid[c.X, c.Y] = (byte)(grid[c.X, c.Y] == DevPalette.CliffFoot ? DevPalette.Ledge : DevPalette.Brink);
		foreach (Vector2I c in d.BankCells) grid[c.X, c.Y] = DevPalette.Bank;
		foreach (Fall f in d.Falls) grid[f.Cell.X, f.Cell.Y] = DevPalette.FallLip;
		foreach (Vector2I c in d.Springs) grid[c.X, c.Y] = DevPalette.Spring;

		for (int x = 0; x < n; x++)
		for (int z = 0; z < n; z++)
		{
			if (d.WaterLevel[x, z] != IslandData.NoLand && d.Fluid[x, z] == (byte)FluidKind.Goo)
				grid[x, z] = DevPalette.GooBed;
			if (d.Beach[x, z]) grid[x, z] = DevPalette.Beach;
			if (d.Ford[x, z]) grid[x, z] = DevPalette.Ford;
			if (d.Landings[x, z]) grid[x, z] = DevPalette.Landing;
			if (d.Ferry[x, z]) grid[x, z] = DevPalette.Quay;
		}

		foreach (Vector2I c in d.Summits) grid[c.X, c.Y] = DevPalette.Summit;
		return grid;
	}

	/// <summary>The ground span by its anchor kind; any span above it is a lip, whatever lies under it.</summary>
	private static Color AnchorColor(int x, int z, int span, byte[,]? grid)
	{
		if (grid == null) return Unremarkable;
		if (span > 0) return DevPalette.Anchor(DevPalette.Overhang);
		return DevPalette.Anchor(grid[x, z]);
	}

	/// <summary>A habitat axis as a two-colour ramp.</summary>
	private static Color FieldColor(byte v, (Color Lo, Color Hi) ramp) => ramp.Lo.Lerp(ramp.Hi, v / 255f);

	private static Color MaterialColor(SurfaceMaterial m) => DevPalette.Material(m);

	/// <summary>Walk areas: only districts get a hue; everything smaller is one grey, the broken mass it is.</summary>
	private static Color WalkColor(IslandData d, int id)
	{
		if (id == Traversal.Water) return WaterTint;
		if (id < 0 || id >= d.Areas.Count) return Unremarkable;
		if (!d.Areas[id].IsDistrict) return Unremarkable;
		if (id == d.Mainland) return MainlandTint;

		float hue = (0.08f + id * 0.61803399f) % 1f;
		return Color.FromHsv(hue, 0.62f, 0.88f);
	}

	/// <summary>Reach areas: green heartland; red for what stays out of reach whatever you build, warmer the smaller.</summary>
	private static Color ReachColor(IslandData d, int id)
	{
		if (id == Traversal.Water) return WaterTint;
		if (id < 0 || id >= d.Reaches.Count) return Unremarkable;
		if (id == d.Heartland) return MainlandTint;
		return ReachColor(Mathf.Clamp(d.Reaches[id].Area / 120f, 0f, 1f));
	}

	/// <summary>The out-of-reach red at a size, 0 the smallest and warmest.</summary>
	private static Color ReachColor(float t) => new(0.86f, 0.22f + 0.26f * t, 0.18f);

	/// <summary>Ford, navigable reach, stream and standing water are four colours.</summary>
	private static Color WaterColor(IslandData d, int x, int z) => DevPalette.Water(d, x, z);

	private static bool OnRegionBorder(IslandData d, int x, int z)
	{
		int n = d.Size;
		int r = d.Region[x, z];
		if (x == 0 || z == 0 || x == n - 1 || z == n - 1) return true;
		return d.Region[x - 1, z] != r || d.Region[x + 1, z] != r
			|| d.Region[x, z - 1] != r || d.Region[x, z + 1] != r;
	}
}
