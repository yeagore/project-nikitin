using System;
using System.Collections.Generic;
using Godot;
using ProjectNikitin.Generation;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Dev;

/// <summary>The MultiMesh layers, the compass and the materials.</summary>
public partial class IslandLab
{
	private static StandardMaterial3D WaterMaterial(float alpha) => new()
	{
		AlbedoColor = new Color(0.16f, 0.42f, 0.62f, alpha),
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		// Visible from underneath too, since the lab can tilt below the island.
		CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		Roughness = 0.12f,
		Metallic = 0.1f,
	};

	private static StandardMaterial3D GooMaterial() => new()
	{
		AlbedoColor = new Color(0.52f, 0.14f, 0.72f, 0.9f),
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		Roughness = 0.05f,
		Metallic = 0.2f,
	};

	private static StandardMaterial3D GateMaterial() => new()
	{
		VertexColorUseAsAlbedo = true,
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		NoDepthTest = true,
	};

	private static StandardMaterial3D MarkMaterial() => new()
	{
		VertexColorUseAsAlbedo = true,
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
	};

	/// <summary>One MultiMesh from parallel transform and (optional) colour lists, in list order.</summary>
	private static MultiMesh BuildMultiMesh(Mesh mesh, List<Transform3D> xf, List<Color>? col)
	{
		var mm = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			UseColors = col != null,
			Mesh = mesh,
			InstanceCount = xf.Count,
		};
		for (int i = 0; i < xf.Count; i++)
		{
			mm.SetInstanceTransform(i, xf[i]);
			if (col != null) mm.SetInstanceColor(i, col[i]);
		}
		return mm;
	}

	/// <summary>Overlay markers in the making: a scaled box per call, in call order.</summary>
	private sealed class MarkList
	{
		public readonly List<Transform3D> Xf = new();
		public readonly List<Color> Col = new();
		private readonly float _half;

		public MarkList(float half) => _half = half;

		public void Add(float x, float y, float z, Vector3 size, Color tint)
		{
			const float cs = Terrain.CellSize;
			Xf.Add(new Transform3D(Basis.Identity.Scaled(size),
								   new Vector3((x - _half) * cs, y, (z - _half) * cs)));
			Col.Add(tint);
		}
	}

	/// <summary>One box per span, coloured by the current view; returns the instance count.</summary>
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

		// Built once, not searched per column.
		byte[,]? anchor = _view == View.Anchors ? AnchorGrid(d) : null;

		var bbMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
		var bbMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

		for (int x = 0; x < n; x++)
		for (int z = 0; z < n; z++)
		{
			Span[] spans = d.Spans[x, z];
			if (spans == null) continue;

			// Span 0 is the ground; anything above it is a lip, coloured as one where the view cares.
			for (int i = 0; i < spans.Length; i++)
			{
				Span s = spans[i];
				float hWorld = s.Height * sh;
				float yCenter = (s.Bottom + s.Top + 1) * 0.5f * sh;
				var origin = new Vector3((x - half) * cs, yCenter, (z - half) * cs);

				xf.Add(new Transform3D(
					Basis.Identity.Scaled(new Vector3(cs, hWorld, cs)), origin));

				switch (_view)
				{
					case View.Landform:
						col.Add(d.Pass[x, z]
							? LandformColor((LandformType)d.Landform[x, z]).Lerp(PassTint, 0.55f)
							: LandformColor((LandformType)d.Landform[x, z]));
						break;
					case View.Region:
						col.Add(OnRegionBorder(d, x, z)
							? RegionColor(d.Region[x, z]).Darkened(0.55f)
							: RegionColor(d.Region[x, z]));
						break;
					case View.Walk:
						col.Add(WalkColor(d, d.Walk[x, z]));
						break;
					case View.Reach:
						col.Add(ReachColor(d, d.Reach[x, z]));
						break;
					case View.Shelves:
						col.Add(ShelfColor(d, x, z));
						break;
					case View.Surface:
						// Material is the ground's; a lip is a rock roof.
						col.Add(MaterialColor(i > 0 ? SurfaceMaterial.Stone : (SurfaceMaterial)d.Material[x, z]));
						break;
					case View.Anchors:
						col.Add(AnchorColor(x, z, i, anchor));
						break;
					case View.Moisture:
						col.Add(FieldColor(d.Moisture[x, z], DevPalette.MoistureRamp));
						break;
					case View.Warmth:
						col.Add(DevPalette.WarmthTint(d.Warmth[x, z]));
						break;
					case View.Rugged:
						col.Add(FieldColor(d.Ruggedness[x, z], DevPalette.RuggedRamp));
						break;
					case View.Exposure:
						col.Add(FieldColor(d.Exposure[x, z], DevPalette.ExposureRamp));
						break;
					case View.Rim:
						col.Add(FieldColor((byte)Math.Min(255, d.RimDistance[x, z] * 6), DevPalette.RimRamp));
						break;
					default:
						float t = Mathf.Clamp((s.Top - topMin) / tintSpan, 0f, 1f);
						col.Add(t < 0.5f
							? HeightLow.Lerp(HeightMid, t * 2f)
							: HeightMid.Lerp(HeightHigh, (t - 0.5f) * 2f));
						break;
				}

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

		_terrain.Multimesh = BuildMultiMesh(_unitBox, xf, col);
		return xf.Count;
	}

	/// <summary>One quad per flooded column at the surface; returns the distinct lake count.</summary>
	private int RenderWater(IslandData d)
	{
		int n = d.Size;
		float half = n * 0.5f;
		const float sh = Terrain.SlabHeight;
		const float cs = Terrain.CellSize;

		var xf = new List<Transform3D>();
		var col = new List<Color>();
		var goo = new List<Transform3D>();
		var lakes = new HashSet<int>();

		for (int x = 0; x < n; x++)
		for (int z = 0; z < n; z++)
		{
			short level = d.WaterLevel[x, z];
			if (level == IslandData.NoLand) continue;

			short top = d.SurfaceLevel(x, z);
			if (top == IslandData.NoLand || level <= top) continue;

			// Slab L fills [L*sh, (L+1)*sh), so the surface sits at (level + 1) * sh.
			var at = new Transform3D(
				Basis.Identity,
				new Vector3((x - half) * cs, (level + 1) * sh, (z - half) * cs));

			if (d.Fluid[x, z] == (byte)FluidKind.Goo) { goo.Add(at); continue; }

			xf.Add(at);
			col.Add(WaterColor(d, x, z));
			if (!d.River[x, z]) lakes.Add(d.Region[x, z]);
		}

		_goo.Multimesh = BuildMultiMesh(_gooQuad, goo, null);
		_water.Multimesh = BuildMultiMesh(_waterQuad, xf, col);
		return lakes.Count;
	}

	/// <summary>
	/// Vertical sheets: one per named Fall, one per sub-FallDepth step between
	/// flooded cells (cataracts, renderer-only), two crossed per Geyser.
	/// </summary>
	private void RenderFalls(IslandData d)
	{
		int n = d.Size;
		float half = n * 0.5f;
		const float sh = Terrain.SlabHeight;
		const float cs = Terrain.CellSize;

		var xf = new List<Transform3D>();
		float floor = 0f, ceiling = 0f;

		foreach (Fall f in d.Falls)
		{
			float top = (f.Top + 1) * sh;
			float bottom = f.Bottom * sh;
			float height = Mathf.Max(sh, top - bottom);

			// Pushed 0.53 cell downstream: 0.5 is the cliff face's own plane and z-fights.
			float angle = Mathf.Atan2(f.Flow.X, f.Flow.Y);
			var basis = new Basis(Vector3.Up, angle)
				.Scaled(new Vector3(cs, height, 1f));

			var origin = new Vector3(
				(f.Cell.X - half + f.Flow.X * 0.53f) * cs,
				bottom + height * 0.5f,
				(f.Cell.Y - half + f.Flow.Y * 0.53f) * cs);

			xf.Add(new Transform3D(basis, origin));
			floor = Mathf.Min(floor, bottom);
			ceiling = Mathf.Max(ceiling, top);
		}

		for (int x = 0; x < n; x++)
		for (int z = 0; z < n; z++)
		{
			short a = d.WaterLevel[x, z];
			if (a == IslandData.NoLand) continue;
			if (d.Fluid[x, z] != (byte)FluidKind.Water) continue;   // goo does not pour

			for (int k = 0; k < 2; k++)
			{
				int nx = x + (k == 0 ? 1 : 0), nz = z + (k == 0 ? 0 : 1);
				if (nx >= n || nz >= n) continue;
				short b = d.WaterLevel[nx, nz];
				if (b == IslandData.NoLand || a == b) continue;
				if (d.Fluid[nx, nz] != (byte)FluidKind.Water) continue;
				if (Math.Abs(a - b) >= Rivers.FallDepth) continue;  // a fall, drawn above

				bool aHigh = a > b;
				int hx = aHigh ? x : nx, hz = aHigh ? z : nz;
				int fx = aHigh ? nx - x : x - nx, fz = aHigh ? nz - z : z - nz;
				short hi = aHigh ? a : b;
				short lo = aHigh ? b : a;

				float top = (hi + 1) * sh;
				float bottom = lo * sh;
				float height = top - bottom;
				var basis = new Basis(Vector3.Up, Mathf.Atan2(fx, fz))
					.Scaled(new Vector3(cs, height, 1f));
				xf.Add(new Transform3D(basis, new Vector3(
					(hx - half + fx * 0.53f) * cs,
					bottom + height * 0.5f,
					(hz - half + fz * 0.53f) * cs)));
				floor = Mathf.Min(floor, bottom);
				ceiling = Mathf.Max(ceiling, top);
			}
		}

		foreach (Geyser g in d.Geysers)
		{
			float bottom = (g.Base + 1) * sh;
			float top = (g.Top + 1) * sh;
			float height = Mathf.Max(sh, top - bottom);
			var at = new Vector3((g.Cell.X - half) * cs, bottom + height * 0.5f,
								 (g.Cell.Y - half) * cs);
			for (int arm = 0; arm < 2; arm++)
			{
				var basis = new Basis(Vector3.Up, arm * Mathf.Pi / 2f)
					.Scaled(new Vector3(0.34f * cs, height, 1f));
				xf.Add(new Transform3D(basis, at));
			}
			floor = Mathf.Min(floor, bottom);
			ceiling = Mathf.Max(ceiling, top);
		}

		_falls.Multimesh = BuildMultiMesh(_fallQuad, xf, null);
		// CustomAabb: flat quads get paper-thin automatic bounds and the culler blinks them out.
		_falls.CustomAabb = new Aabb(
			new Vector3(-half * cs - cs, floor - cs, -half * cs - cs),
			new Vector3(n * cs + 2 * cs, ceiling - floor + 2 * cs, n * cs + 2 * cs));
	}

	/// <summary>One box per Gate: entry gold, exits cyan, hanging Gates paler.</summary>
	private void RenderGates(IslandData d)
	{
		int n = d.Size;
		float half = n * 0.5f;
		const float sh = Terrain.SlabHeight;
		const float cs = Terrain.CellSize;

		var xf = new List<Transform3D>();
		var col = new List<Color>();

		foreach (Gate g in d.Gates)
		{
			// Center.Y is the sill slab; the portal rises Gate.Height from its top.
			float baseY = (g.Center.Y + 1) * sh;
			var origin = new Vector3((g.Center.X - half) * cs,
									 baseY + Gate.Height * sh * 0.5f,
									 (g.Center.Z - half) * cs);

			var size = new Vector3(
				Mathf.Abs(g.Across.X) * Gate.Width * cs + Mathf.Abs(g.Outward.X) * cs,
				Gate.Height * sh,
				Mathf.Abs(g.Across.Y) * Gate.Width * cs + Mathf.Abs(g.Outward.Y) * cs);

			xf.Add(new Transform3D(Basis.Identity.Scaled(size), origin));

			Color tint = g.Role == GateRole.Entry
				? new Color(1f, 0.82f, 0.25f, 0.85f)
				: new Color(0.35f, 0.85f, 0.95f, 0.75f);
			col.Add(g.Kind == GateKind.Hanging ? tint.Lightened(0.35f) : tint);
		}

		_gates.Multimesh = BuildMultiMesh(_gateBox, xf, col);
	}

	/// <summary>The toggled overlays, drawn into one multimesh in a fixed sequence.</summary>
	private void RenderOverlays(IslandData d)
	{
		var m = new MarkList(d.Size * 0.5f);

		if (_showBridges) DrawBridges(d, m);
		if (_showLandings) DrawLandings(d, m);
		if (_showFords) DrawFords(d, m);
		if (_showFerries) DrawFerries(d, m);
		if (_showRoutes) DrawRoads(d, m);
		if (_showCompass)
		{
			DrawGateVectors(d, m);
			DrawWind(d, m);
			DrawDuneGrain(d, m.Add);
			DrawBounds(d, m);
		}

		_marks.Multimesh = BuildMultiMesh(_markBox, m.Xf, m.Col);
		PlaceCompass(d);
	}

	/// <summary>
	/// The Domain's one wind, whether or not it has dunes to show it on: a tapering
	/// run of arrows standing off the upwind edge, pointing the way it blows, above
	/// the tallest ground so a mountain cannot hide it.
	/// </summary>
	private static void DrawWind(IslandData d, MarkList m)
	{
		const float sh = Terrain.SlabHeight;
		const float cs = Terrain.CellSize;
		int n = d.Size;

		short top = 0;
		for (int x = 0; x < n; x++)
		for (int z = 0; z < n; z++)
			if (d.HasLand(x, z)) top = Math.Max(top, d.Spans[x, z][^1].Top);
		float y = (top + 1) * sh + 2f * cs;

		Vector2 dir = d.DuneVector;
		float half = n * 0.5f;
		const int run = 8;
		for (int step = 0; step <= run; step++)
		{
			// From nine cells off the rim to one: widest upwind, a point downwind.
			float off = half + 9f - step;
			float px = half - dir.X * off, pz = half - dir.Y * off;
			float w = Mathf.Lerp(0.9f, 0.2f, step / (float)run);
			m.Add(px, y, pz, new Vector3(cs * w, sh * 0.8f, cs * w), WindTint);
		}
	}

	/// <summary>Each bridge deck, bank to bank, and its two banks.</summary>
	private static void DrawBridges(IslandData d, MarkList m)
	{
		const float sh = Terrain.SlabHeight;
		const float cs = Terrain.CellSize;
		foreach (Crossing c in d.Bridges)
		{
			for (int i = 0; i < c.Span; i++)
			{
				Vector2I cell = c.Cell(i);
				m.Add(cell.X, (c.Deck + 1) * sh, cell.Y,
					  new Vector3(cs * 0.8f, sh * 0.7f, cs * 0.8f), DeckTint);
			}
			foreach (Vector2I bank in new[] { c.A, c.B })
				m.Add(bank.X, (Traversal.CrossLevel(d, bank.X, bank.Y) + 1) * sh + sh * 0.2f,
					  bank.Y, new Vector3(cs * 0.9f, sh * 0.5f, cs * 0.9f), BankTint);
		}
	}

	/// <summary>The 1 x 3 landing strip each Gate is served by.</summary>
	private static void DrawLandings(IslandData d, MarkList m)
	{
		const float sh = Terrain.SlabHeight;
		const float cs = Terrain.CellSize;
		int n = d.Size;
		for (int x = 0; x < n; x++)
		for (int z = 0; z < n; z++)
		{
			if (!d.Landings[x, z]) continue;
			m.Add(x, (d.SurfaceLevel(x, z) + 1) * sh + sh * 0.2f, z,
				  new Vector3(cs * 0.8f, sh * 0.3f, cs * 0.8f), StripUsedTint);
		}
	}

	private static void DrawFords(IslandData d, MarkList m)
	{
		const float sh = Terrain.SlabHeight;
		const float cs = Terrain.CellSize;
		int n = d.Size;
		for (int x = 0; x < n; x++)
		for (int z = 0; z < n; z++)
		{
			if (!d.Ford[x, z]) continue;
			m.Add(x, (d.WaterLevel[x, z] + 1) * sh + sh * 0.2f, z,
				  new Vector3(cs * 0.7f, sh * 0.4f, cs * 0.7f), FordTint);
		}
	}

	/// <summary>Each ferry berth as a domino: the quay and the hull on the water before it.</summary>
	private static void DrawFerries(IslandData d, MarkList m)
	{
		const float sh = Terrain.SlabHeight;
		const float cs = Terrain.CellSize;
		foreach (FerryBerth berth in d.Berths)
		{
			m.Add(berth.Land.X,
				  (Traversal.CrossLevel(d, berth.Land.X, berth.Land.Y) + 1) * sh + sh * 0.25f,
				  berth.Land.Y, new Vector3(cs * 0.55f, sh * 0.5f, cs * 0.55f), QuayTint);
			m.Add(berth.Water.X, (berth.Level + 1) * sh + sh * 0.1f, berth.Water.Y,
				  new Vector3(cs * 0.4f, sh * 0.3f, cs * 0.4f), HullTint);
		}
	}

	/// <summary>The roads between the Gates: the walk, and every work on it in its kind's colour.</summary>
	private static void DrawRoads(IslandData d, MarkList m)
	{
		const float sh = Terrain.SlabHeight;
		const float cs = Terrain.CellSize;
		foreach (Passage road in d.Passages)
		{
			foreach (Vector2I cell in road.Path)
				m.Add(cell.X, (Traversal.CrossLevel(d, cell.X, cell.Y) + 1) * sh + sh * 0.15f,
					  cell.Y, new Vector3(cs * 0.35f, sh * 0.3f, cs * 0.35f), RoadTint);

			foreach (Works works in road.Built)
			{
				Color tint = works.Kind switch
				{
					WorksKind.Stair => StairTint,
					WorksKind.Bridge => SpanTint,
					_ => CrossingTint,
				};
				foreach (Vector2I cell in new[] { works.From, works.To })
					m.Add(cell.X,
						  (Traversal.CrossLevel(d, cell.X, cell.Y) + 1) * sh + sh * 0.55f,
						  cell.Y, new Vector3(cs * 0.7f, sh * 0.8f, cs * 0.7f), tint);
			}
		}
	}

	/// <summary>A tapering arrow inward from each Gate: the way in is the opposite of Outward.</summary>
	private static void DrawGateVectors(IslandData d, MarkList m)
	{
		const float sh = Terrain.SlabHeight;
		const float cs = Terrain.CellSize;
		int n = d.Size;
		foreach (Gate g in d.Gates)
		{
			Vector2I inward = -g.Outward;
			Color tint = g.Role == GateRole.Entry
				? new Color(1f, 0.82f, 0.25f)
				: new Color(0.35f, 0.85f, 0.95f);

			for (int step = 1; step <= 6; step++)
			{
				int cx = g.Center.X + inward.X * step;
				int cz = g.Center.Z + inward.Y * step;
				if (!InBounds(n, cx, cz)) continue;

				short ground = d.SurfaceLevel(cx, cz);
				float y = ground == IslandData.NoLand
					? (g.Center.Y + 1) * sh
					: (ground + 1) * sh + sh * 0.6f;
				float w = Mathf.Lerp(0.7f, 0.2f, (step - 1) / 5f);
				m.Add(cx, y, cz, new Vector3(cs * w, sh * 0.5f, cs * w), tint);
			}
		}
	}

	/// <summary>
	/// Two boxes: gold tight round the land itself, and the Domain's cube — Size
	/// cells across, Size slabs tall, standing on the keel's lowest point, the
	/// shape the audit's altitude check measures against. The cube never changes
	/// shape between seeds; what it contains does.
	/// </summary>
	private static void DrawBounds(IslandData d, MarkList m)
	{
		const float sh = Terrain.SlabHeight;
		const float cs = Terrain.CellSize;
		int n = d.Size;

		float yLo = 0f, yHi = 0f;
		float xLo = n, xHi = -1f, zLo = n, zHi = -1f;
		bool anyCol = false;
		for (int x = 0; x < n; x++)
		for (int z = 0; z < n; z++)
		{
			if (!d.HasLand(x, z)) continue;
			float keel = d.KeelLevel(x, z) * sh;
			float top = d.Spans[x, z][^1].Top * sh;
			yLo = anyCol ? Mathf.Min(yLo, keel) : keel;
			yHi = anyCol ? Mathf.Max(yHi, top) : top;
			anyCol = true;
			xLo = Mathf.Min(xLo, x);
			xHi = Mathf.Max(xHi, x);
			zLo = Mathf.Min(zLo, z);
			zHi = Mathf.Max(zHi, z);
		}

		void Box(float ax, float bx, float ay, float by, float az, float bz,
				 float girth, Color tint)
		{
			float mx = (ax + bx) * 0.5f, mz = (az + bz) * 0.5f;
			float my = (ay + by) * 0.5f;
			float lx = (bx - ax) * cs + girth, lz = (bz - az) * cs + girth;
			foreach (float y in new[] { ay, by })
			{
				m.Add(mx, y, az, new Vector3(lx, girth, girth), tint);
				m.Add(mx, y, bz, new Vector3(lx, girth, girth), tint);
				m.Add(ax, y, mz, new Vector3(girth, girth, lz), tint);
				m.Add(bx, y, mz, new Vector3(girth, girth, lz), tint);
			}
			foreach (float px in new[] { ax, bx })
			foreach (float pz in new[] { az, bz })
				m.Add(px, my, pz, new Vector3(girth, by - ay, girth), tint);
		}

		if (anyCol)
		{
			Box(xLo - 0.5f, xHi + 0.5f, yLo - sh, yHi + sh, zLo - 0.5f,
				zHi + 0.5f, cs * 0.14f, new Color(1f, 0.78f, 0.25f, 0.6f));

			Box(-0.5f, n - 0.5f, yLo, yLo + n * sh, -0.5f,
				n - 0.5f, cs * 0.1f, new Color(0.92f, 0.96f, 1f, 0.3f));
		}
	}

	/// <summary>The wind as a tapering run of arrows from the centre of each dune field (12 cells or more).</summary>
	private static void DrawDuneGrain(IslandData d, Action<float, float, float, Vector3, Color> mark)
	{
		int n = d.Size;
		const float sh = Terrain.SlabHeight;
		const float cs = Terrain.CellSize;
		Color wind = WindTint;

		var sumX = new Dictionary<int, long>();
		var sumZ = new Dictionary<int, long>();
		var count = new Dictionary<int, int>();

		for (int x = 0; x < n; x++)
		for (int z = 0; z < n; z++)
		{
			if (!d.HasLand(x, z)) continue;
			if ((LandformType)d.Landform[x, z] != LandformType.Dunes) continue;
			int r = d.Region[x, z];
			sumX[r] = sumX.GetValueOrDefault(r) + x;
			sumZ[r] = sumZ.GetValueOrDefault(r) + z;
			count[r] = count.GetValueOrDefault(r) + 1;
		}

		Vector2 dir = d.DuneVector;
		foreach ((int r, int cells) in count)
		{
			if (cells < 12) continue;                     // a sliver is not a field
			float cx = sumX[r] / (float)cells;
			float cz = sumZ[r] / (float)cells;

			int run = Math.Clamp((int)MathF.Sqrt(cells) - 1, 4, 14);
			for (int step = -run; step <= run; step++)
			{
				float px = cx + dir.X * step;
				float pz = cz + dir.Y * step;
				int ix = Mathf.RoundToInt(px), iz = Mathf.RoundToInt(pz);
				if (!InBounds(n, ix, iz) || !d.HasLand(ix, iz)) continue;

				short ground = d.SurfaceLevel(ix, iz);
				if (ground == IslandData.NoLand) continue;

				float t = (step + run) / (float)(2 * run);
				float w = Mathf.Lerp(0.75f, 0.18f, t);     // widest upwind, a point downwind
				mark(px, (ground + 1) * sh + sh * 0.9f,
					 pz, new Vector3(cs * w, sh * 0.6f, cs * w), wind);
			}
		}
	}

	/// <summary>N / E / S / W billboards, standing off the four edges of the footprint, and the wind's label.</summary>
	private void BuildCompass()
	{
		var letters = new[] { "N", "E", "S", "W" };
		foreach (string text in letters)
			_compass.Add(Billboard(text, 128, new Color(1f, 0.95f, 0.8f, 0.85f)));
		_windLabel = Billboard("", 72, new Color(WindTint, 0.95f));
	}

	private Label3D Billboard(string text, int fontSize, Color tint)
	{
		var label = new Label3D
		{
			Text = text,
			FontSize = fontSize,
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			NoDepthTest = true,
			Modulate = tint,
			PixelSize = 0.006f,
		};
		AddChild(label);
		return label;
	}

	private void PlaceCompass(IslandData d)
	{
		float half = d.Size * 0.5f * Terrain.CellSize;
		float out_ = half + 6f;
		var at = new[]
		{
			new Vector3(0f, 0f, -out_),   // N is -Z
			new Vector3(out_, 0f, 0f),    // E is +X
			new Vector3(0f, 0f, out_),    // S is +Z
			new Vector3(-out_, 0f, 0f),   // W is -X
		};

		for (int i = 0; i < _compass.Count; i++)
		{
			_compass[i].Position = at[i] + Vector3.Up * 3f;
			_compass[i].Visible = _showCompass;
		}

		// The wind's name at the upwind end of its arrows, over the arrows' own height.
		short top = 0;
		for (int x = 0; x < d.Size; x++)
		for (int z = 0; z < d.Size; z++)
			if (d.HasLand(x, z)) top = Math.Max(top, d.Spans[x, z][^1].Top);
		Vector2 dir = d.DuneVector;
		float off = half + 11f;
		_windLabel.Text = $"wind from {d.WindFrom}";
		_windLabel.Position = new Vector3(-dir.X * off,
			(top + 1) * Terrain.SlabHeight + 2f * Terrain.CellSize + 1f, -dir.Y * off);
		_windLabel.Visible = _showCompass;
	}
}
