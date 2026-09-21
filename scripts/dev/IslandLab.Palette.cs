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
		Navigable,
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

	/// <summary>
	/// A view's legend in the parts the plate lays out: what the view asks, its colours
	/// one to a line (so the plate can be a narrow column at the edge of the screen and
	/// scroll), and the small print under them.
	/// </summary>
	private readonly record struct Legend(string Title, string Intro, List<string> Items, string Note = "");

	/// <summary>What each view is answering, with its actual colours, next to the picture.</summary>
	private static Legend ViewLegend(View view)
	{
		switch (view)
		{
			case View.Height:
				return new Legend("height", "", new List<string>
				{
					$"{Ramp(DevPalette.HeightLow, DevPalette.HeightMid)}{Ramp(DevPalette.HeightMid, DevPalette.HeightHigh)}",
					"low ground dark, high ground pale",
				});

			case View.Landform:
			{
				var bits = new List<string>();
				foreach (LandformType t in Enum.GetValues<LandformType>())
					bits.Add(Keyed(LandformColor(t), t.ToString().ToLowerInvariant()));
				bits.Add(Keyed(LandformColor(LandformType.Plain).Lerp(PassTint, 0.55f), "pass (tinted)"));
				return new Legend("landform", "", bits);
			}

			case View.Region:
				return new Legend("region", "one hue per patch, borders darkened", new List<string>
				{
					Keyed(RegionColor(3), "a patch"),
					Keyed(RegionColor(3).Darkened(0.55f), "its border"),
				});

			case View.Walk:
				return new Legend("walk", "what you can cross on foot",
					new List<string>
					{
						Keyed(MainlandTint, "mainland"),
						$"{Swatch(DevPalette.District(1))}{Swatch(DevPalette.District(2))}{Swatch(DevPalette.District(3))} a hue per other district",
						Keyed(Unremarkable, "broken ground"),
						Keyed(WaterTint, "water"),
					},
					"Corners are cut unless both sides are scarps or cliffs; a district "
					+ $"({Traversal.MinDistrictArea}+ cells) is somewhere to build.");

			case View.Reach:
				return new Legend("reach", "what you can cross once built", new List<string>
				{
					Keyed(MainlandTint, "heartland"),
					Ramp(ReachColor(0f), ReachColor(1f)) + " out of reach whatever you build, warmer the smaller",
					Keyed(WaterTint, "water"),
				});

			case View.Navigable:
				return new Legend("navigable", "the bodies of sailable water; [b]a fall cuts a body[/b]",
					new List<string>
					{
						$"{Swatch(DevPalette.Body(0))}{Swatch(DevPalette.Body(1))}{Swatch(DevPalette.Body(2))} a hue per body",
						Keyed(DevPalette.Anchor(DevPalette.FallLip), "the lip a body ends at"),
						Keyed(DevPalette.Unsailable, "water no hull uses"),
						Keyed(DevPalette.Goo, "goo"),
						Keyed(Unremarkable, "land"),
					},
					"Standing water and navigable reaches, never goo and never a stream, which is forded "
					+ "and not sailed. A hull goes anywhere within one hue and nowhere between two, since "
					+ "nothing sails up a fall. The bed under a body carries its colour dimmed, so the "
					+ "regions still read with the liquid off (I); the island readout (F3) names every "
					+ "body and counts its cells, and the cell readout names the one pointed at.");

			case View.Surface:
			{
				var bits = new List<string>();
				foreach (SurfaceMaterial m in Enum.GetValues<SurfaceMaterial>())
					bits.Add(Keyed(MaterialColor(m), m.ToString().ToLowerInvariant()));
				return new Legend("surface", "what the ground is made of", bits, "An overhang's lip is drawn as stone.");
			}

			case View.Anchors:
			{
				var bits = new List<string>();
				foreach (int kind in DevPalette.LegendOrder)
					bits.Add(Keyed(DevPalette.Anchor(kind), DevPalette.AnchorName(kind)));
				bits.Add(Keyed(DevPalette.Anchor(0), "unremarkable ground"));
				return new Legend("anchors", "what the content layer attaches to",
					bits,
					$"A cliff is a face of {Traversal.CliffFace}+ slabs, a scarp one of 2–{Traversal.CliffFace - 1}. "
					+ "The lists overlap; here the built and rarer kinds win, a cliff anchor over a scarp "
					+ "one, and a cell that is both brink and foot of one kind of face is its ledge. The "
					+ "cell readout names every list a cell is on. "
					+ "Only the lip of an overhang is magenta: the ground under it is its own kind. "
					+ "Beds show with liquid off (I). A sea stack is a dark column in the aether, in every view.");
			}

			case View.Moisture:
				return new Legend("moisture", "", new List<string> { $"{Ramp(DevPalette.MoistureRamp)}  parched … waterside" },
					"The Domain's background moisture in patches; the lee in the wind's rain shadow, and "
					+ "sheltered broken ground (a gorge floor) damper, both by the wind knob; rock and its "
					+ "fringe with patches of drought; plus what fresh water adds along a walk from it "
					+ "(two cells more per slab climbed, so a river waters the plain it crosses and "
					+ "not the mountain it passes).");

			case View.Warmth:
			{
				var stops = new List<string>();
				foreach (byte w in new byte[] { 0, 64, 110, 150, 190, 205, 220, 235, 255 })
					stops.Add(Swatch(DevPalette.WarmthTint(w)));
				return new Legend("warmth", "", new List<string>
					{
						string.Join("", stops),
						"frozen … cold (blue) … temperate (yellow) … hot (orange)",
					},
					"One climate over the whole island, then the lapse over a mountain's upper part; a "
					+ "slope facing the sun (compass overlay, X) a touch warmer and one facing away colder; "
					+ "basins and sinkhole pits frost hollows; the lee milder by the wind knob, the rim "
					+ "colder, wet ground tempered; on a cold Domain a bloom round each hot spring or "
					+ "pool (orange water).");
			}

			case View.Rugged:
				return new Legend("rugged", "", new List<string> { $"{Ramp(DevPalette.RuggedRamp)}  flat … broken" },
					"Local relief within two cells. Water is read as its bank, a slab over its surface, "
					+ "so a stream through a plain is flat country and a gorge is still its walls.");

			case View.Exposure:
				return new Legend("exposure", "", new List<string> { $"{Ramp(DevPalette.ExposureRamp)}  lee … windswept" },
					"Openness to the Domain's one wind (compass overlay, X, shows it), dunes or not.");

			case View.Rim:
				return new Legend("rim", "", new List<string> { $"{Ramp(DevPalette.RimRamp)}  rim … interior" },
					"Cells of land between here and the aether. Essencecoral country is the violet end.");

			case View.Water:
				return new Legend("water distance", "", new List<string> { $"{Ramp(DevPalette.WaterRamp)}  bank … out of reach" },
					"The walk cost to fresh water the moisture strip reads (a cell per cell along or "
					+ "down, two more per slab up), kept as a byte for the settlement and biome layers; shown to 60.");

			default:
				return new Legend("magick", "", new List<string> { $"{Ramp(DevPalette.MagickRamp)}  inert … saturated" },
					"The magickal density layer, grown by a Turing reaction between the magick and the "
					+ "inhibitor it feeds on: spots, worms, mazes or lace by the pattern, thickened by "
					+ "the density. Read by nothing.");
		}
	}

	private static Color LandformColor(LandformType type) => DevPalette.Landform(type);

	/// <summary>A distinct hue per region; the golden-ratio step keeps adjacent ids apart.</summary>
	private static Color RegionColor(int id)
	{
		if (id < 0) return new Color(0.5f, 0.5f, 0.5f);
		float hue = id * 0.61803399f % 1f;
		float sat = 0.45f + (id * 7 % 3) * 0.12f;
		float val = 0.62f + (id * 5 % 4) * 0.09f;
		return Color.FromHsv(hue, sat, val);
	}

	private static readonly Color Unremarkable = DevPalette.Broken;
	private static readonly Color WaterTint = DevPalette.WalkWater;
	private static readonly Color PassTint = new(0.92f, 0.85f, 0.42f);
	private static readonly Color MainlandTint = DevPalette.Mainland;

	private static readonly Color DeckTint = new(0.95f, 0.72f, 0.30f);
	private static readonly Color BankTint = new(0.99f, 0.94f, 0.55f);
	private static readonly Color StripUsedTint = new(1f, 0.55f, 0.85f);

	private static readonly Color RoadTint = new(0.98f, 0.95f, 0.62f, 0.8f);
	private static readonly Color StairTint = new(1f, 0.45f, 0.25f);
	private static readonly Color LadderTint = new(0.72f, 0.52f, 1f);
	private static readonly Color SpanTint = new(1f, 0.80f, 0.20f);

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
		foreach (Vector2I c in d.ShallowBedCells) grid[c.X, c.Y] = DevPalette.ShallowBed;
		foreach (Vector2I c in d.MidBedCells) grid[c.X, c.Y] = DevPalette.MidBed;
		foreach (Vector2I c in d.DeepBedCells) grid[c.X, c.Y] = DevPalette.DeepBed;
		foreach (Vector2I c in d.Deeps) grid[c.X, c.Y] = DevPalette.Deep;
		foreach (Vector2I c in d.CoastCells) grid[c.X, c.Y] = DevPalette.Coast;
		// Scarps first, so a cell that is a cliff anchor one way and a scarp anchor
		// another reads as the cliff. Within one kind of face, a bench is a brink over
		// one neighbour and a foot under another: a ledge.
		foreach (Vector2I c in d.ScarpFootCells) grid[c.X, c.Y] = DevPalette.ScarpFoot;
		foreach (Vector2I c in d.ScarpCells)
			grid[c.X, c.Y] = (byte)(grid[c.X, c.Y] == DevPalette.ScarpFoot ? DevPalette.ScarpLedge : DevPalette.ScarpBrink);
		foreach (Vector2I c in d.CliffFootCells) grid[c.X, c.Y] = DevPalette.CliffFoot;
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
			if (d.Hot[x, z]) grid[x, z] = DevPalette.HotSpring;
			if (d.Ford[x, z]) grid[x, z] = DevPalette.Ford;
			if (d.Landings[x, z]) grid[x, z] = DevPalette.Landing;
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
		return DevPalette.District(id);
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

	/// <summary>Ford, navigable reach, stream and standing water are four colours; the navigable view asks a different question of the same cells.</summary>
	private Color WaterColor(IslandData d, int x, int z)
		=> _view == View.Navigable ? BodyColor(d, x, z) : DevPalette.Water(d, x, z);

	/// <summary>
	/// A water cell by the body it belongs to: a hue each, white at the lip of a fall
	/// that ends one, and the slate of water no hull uses (a stream, a goo puddle —
	/// though goo has its own material and never reads this).
	/// </summary>
	private Color BodyColor(IslandData d, int x, int z)
	{
		int id = d.WaterBody[x, z];
		if (id < 0) return DevPalette.Unsailable;
		if (_fallLips.Contains(new Vector2I(x, z))) return DevPalette.Anchor(DevPalette.FallLip);
		return DevPalette.Body(id);
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
