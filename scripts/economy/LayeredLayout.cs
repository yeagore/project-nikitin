using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectNikitin.Economy;

/// <summary>
/// Arranges a directed graph into left-to-right layers, Sugiyama-style. Nodes drop into
/// columns by longest path from the sources; each column is then ordered by a handful of
/// barycentre sweeps so that related nodes line up with one another; finally each column
/// is stacked top to bottom and relaxed towards its neighbours' centres, without ever
/// letting two nodes of the same column touch. A cycle is broken by discarding the back
/// edges an ordinary depth-first walk finds before any of that runs — they are still
/// edges as far as the caller is concerned, they simply take no part in the layout maths.
/// An edge naming an unknown key, a self-loop or a repeat of an earlier edge is dropped
/// silently. A node with no edge at all, and no pin, is "loose": rather than join column
/// zero it is packed into a small grid under the drawing. The whole thing is deterministic:
/// no randomness, no hashed iteration order and only stable, ordinal sorts, so the same
/// nodes and edges in the same order always come out the same way, on any machine.
/// </summary>
public static class LayeredLayout
{
	/// <summary>Column value that pins a node one column past the last free one.</summary>
	public const int PinLast = int.MaxValue;

	/// <summary>One box to place.</summary>
	/// <param name="Key">Identifier the caller uses to look the node's position back up.</param>
	/// <param name="Width">Width in the caller's own units; used only to size columns and to check for overlap.</param>
	/// <param name="Height">Height in the caller's own units; used only to stack rows and to check for overlap.</param>
	/// <param name="SortHint">Tie-breaker for the order a column starts in, before any barycentre sweep moves it.</param>
	/// <param name="PinColumn">A fixed column index, or <see cref="PinLast"/> for one column past the last free one; a negative value (the default) leaves the column to the longest path.</param>
	public readonly record struct Node(string Key, float Width, float Height, string SortHint = "", int PinColumn = -1);

	/// <summary>A directed connection from one node key to another.</summary>
	/// <param name="From">The source node's <see cref="Node.Key"/>.</param>
	/// <param name="To">The target node's <see cref="Node.Key"/>.</param>
	public readonly record struct Edge(string From, string To);

	/// <summary>Tuning knobs for <see cref="Arrange"/>. Every field carries a sensible default.</summary>
	public sealed class Options
	{
		/// <summary>Horizontal air between columns: added to the widest node of the column on the left.</summary>
		public float ColumnGap = 90f;

		/// <summary>Vertical air kept between neighbouring nodes of a column, and between rows of the loose block.</summary>
		public float RowGap = 14f;

		/// <summary>How many barycentre sweeps, alternating left-to-right and right-to-left, refine the order within columns.</summary>
		public int OrderSweeps = 8;

		/// <summary>How many vertical relaxation passes pull nodes towards their neighbours' centres once the order is settled.</summary>
		public int RelaxPasses = 12;

		/// <summary>Row width, in nodes, of the grid that holds edge-less, unpinned nodes under the main drawing.</summary>
		public int LooseColumns = 6;
	}

	/// <summary>
	/// Lays the graph out and returns the top-left corner of every node named in <paramref name="nodes"/>,
	/// keyed by <see cref="Node.Key"/>. <paramref name="edges"/> may freely name an unknown key, repeat
	/// itself, loop a node back on itself or close a cycle: all four are handled quietly rather than thrown.
	/// </summary>
	public static Dictionary<string, (float X, float Y)> Arrange(IReadOnlyList<Node> nodes, IReadOnlyList<Edge> edges, Options? options = null)
	{
		return new Builder(nodes, edges, options ?? new Options()).Run();
	}

	// The static class above is just the public face; everything it does needs working
	// state (arrays sized to the node count, columns being refined, and so on), so the
	// actual algorithm lives on this private, single-use instance instead.
	private sealed class Builder
	{
		private readonly Options options;
		private readonly int n;
		private readonly int[] pin;
		private readonly float[] width;
		private readonly float[] height;
		private readonly string[] sortHint;
		private readonly string[] key;

		// Edges naming an unknown key, a self-loop or a repeat of an earlier edge are left
		// out of validEdges entirely. layeringEdges is validEdges minus the back edges a
		// depth-first walk finds; it is what columns, ordering and relaxation all see.
		private readonly List<(int From, int To)> validEdges = new();
		private readonly List<(int From, int To)> layeringEdges = new();

		private bool[] loose = Array.Empty<bool>();
		private int[] column = Array.Empty<int>();
		private List<int>[] preds = Array.Empty<List<int>>();
		private List<int>[] succs = Array.Empty<List<int>>();
		private List<int>[] columnNodes = Array.Empty<List<int>>();
		private int[] posInColumn = Array.Empty<int>();
		private int maxColumn = -1;
		private float[] nodeX = Array.Empty<float>();
		private float[] nodeTop = Array.Empty<float>();

		public Builder(IReadOnlyList<Node> nodes, IReadOnlyList<Edge> edges, Options options)
		{
			this.options = options;
			n = nodes.Count;
			pin = new int[n];
			width = new float[n];
			height = new float[n];
			sortHint = new string[n];
			key = new string[n];

			var keyToIndex = new Dictionary<string, int>(n, StringComparer.Ordinal);
			for (int i = 0; i < n; i++)
			{
				var node = nodes[i];
				key[i] = node.Key;
				width[i] = node.Width;
				height[i] = node.Height;
				sortHint[i] = node.SortHint;
				pin[i] = node.PinColumn;
				keyToIndex[node.Key] = i;
			}

			FilterEdges(edges, keyToIndex);
		}

		public Dictionary<string, (float X, float Y)> Run()
		{
			var topoOrder = FindBackEdgesAndTopoOrder();
			BuildLayeringAdjacency();
			MarkLoose();
			AssignColumns(topoOrder);
			GroupAndSortColumns();
			OrderWithinColumns();
			AssignX();
			AssignY();
			PlaceLooseNodes();
			return BuildResult();
		}

		// Step 0 (edge hygiene): an edge survives only if both ends are known keys, it is
		// not a self-loop, and it has not already been seen. Order is preserved, which is
		// what later steps rely on for "successors in edge order".
		private void FilterEdges(IReadOnlyList<Edge> edges, Dictionary<string, int> keyToIndex)
		{
			var seen = new HashSet<(int, int)>();
			for (int e = 0; e < edges.Count; e++)
			{
				var edge = edges[e];
				if (!keyToIndex.TryGetValue(edge.From, out int from)) continue;
				if (!keyToIndex.TryGetValue(edge.To, out int to)) continue;
				if (from == to) continue;
				if (!seen.Add((from, to))) continue;
				validEdges.Add((from, to));
			}
		}

		// Step 1 (cycles): an iterative depth-first walk — visiting nodes in input order and
		// each node's successors in edge order, so it never depends on hashing — finds every
		// back edge: one that lands on a node still on the walk's own stack. Discarding just
		// those edges always leaves a DAG (a standard fact about depth-first search), and its
		// post-order, reversed, is a valid topological order for that DAG.
		private int[] FindBackEdgesAndTopoOrder()
		{
			var adjacency = new List<(int EdgeIndex, int Target)>[n];
			for (int i = 0; i < n; i++) adjacency[i] = new List<(int, int)>();
			for (int e = 0; e < validEdges.Count; e++)
			{
				var (from, to) = validEdges[e];
				adjacency[from].Add((e, to));
			}

			const byte White = 0, Gray = 1, Black = 2;
			var state = new byte[n];
			var isBack = new bool[validEdges.Count];
			var finishOrder = new List<int>(n);

			// An explicit stack of (node, next child to try) frames, so the walk never
			// recurses; RemoveAt on the last element is the pop.
			var stackNode = new List<int>();
			var stackNext = new List<int>();

			for (int start = 0; start < n; start++)
			{
				if (state[start] != White) continue;

				state[start] = Gray;
				stackNode.Add(start);
				stackNext.Add(0);

				while (stackNode.Count > 0)
				{
					int frame = stackNode.Count - 1;
					int u = stackNode[frame];
					int nextChild = stackNext[frame];
					var adj = adjacency[u];

					if (nextChild < adj.Count)
					{
						stackNext[frame] = nextChild + 1;
						var (edgeIndex, v) = adj[nextChild];
						if (state[v] == White)
						{
							state[v] = Gray;
							stackNode.Add(v);
							stackNext.Add(0);
						}
						else if (state[v] == Gray)
						{
							isBack[edgeIndex] = true;
						}
						// state[v] == Black: a cross or forward edge, which needs no special handling.
					}
					else
					{
						state[u] = Black;
						finishOrder.Add(u);
						stackNode.RemoveAt(frame);
						stackNext.RemoveAt(frame);
					}
				}
			}

			for (int e = 0; e < validEdges.Count; e++)
				if (!isBack[e]) layeringEdges.Add(validEdges[e]);

			finishOrder.Reverse();
			return finishOrder.ToArray();
		}

		private void BuildLayeringAdjacency()
		{
			preds = new List<int>[n];
			succs = new List<int>[n];
			for (int i = 0; i < n; i++)
			{
				preds[i] = new List<int>();
				succs[i] = new List<int>();
			}
			foreach (var (from, to) in layeringEdges)
			{
				succs[from].Add(to);
				preds[to].Add(from);
			}
		}

		// A node with no edge at all — not even a back edge — and no pin takes no part in
		// layering; PlaceLooseNodes gives it a spot in the grid under the drawing instead.
		private void MarkLoose()
		{
			var hasEdge = new bool[n];
			foreach (var (from, to) in validEdges)
			{
				hasEdge[from] = true;
				hasEdge[to] = true;
			}
			loose = new bool[n];
			for (int i = 0; i < n; i++) loose[i] = !hasEdge[i] && pin[i] < 0;
		}

		// Step 2 (columns): longest path from the sources, read off in topological order so
		// that every predecessor's column is already settled by the time a node needs it. A
		// pin wins outright over the longest path; PinLast is deferred until every other
		// column is known, then placed one past the largest of them.
		private void AssignColumns(int[] topoOrder)
		{
			column = new int[n];
			for (int i = 0; i < n; i++) column[i] = -1;

			var deferredPinLast = new List<int>();

			foreach (int u in topoOrder)
			{
				if (loose[u]) continue;
				if (pin[u] == PinLast) { deferredPinLast.Add(u); continue; }
				if (pin[u] >= 0) { column[u] = pin[u]; continue; }

				int maxPred = -1;
				foreach (int p in preds[u])
					if (column[p] > maxPred) maxPred = column[p];
				column[u] = maxPred + 1;
			}

			int maxSoFar = -1;
			for (int i = 0; i < n; i++)
				if (!loose[i] && column[i] > maxSoFar) maxSoFar = column[i];

			foreach (int u in deferredPinLast) column[u] = maxSoFar + 1;

			maxColumn = -1;
			for (int i = 0; i < n; i++)
				if (!loose[i] && column[i] > maxColumn) maxColumn = column[i];
		}

		// Step 3a: every column starts out ordered by SortHint then Key, ordinal both times,
		// so two callers building the same graph in a different order still agree.
		private void GroupAndSortColumns()
		{
			columnNodes = new List<int>[Math.Max(0, maxColumn + 1)];
			for (int c = 0; c < columnNodes.Length; c++) columnNodes[c] = new List<int>();
			for (int i = 0; i < n; i++)
				if (!loose[i]) columnNodes[column[i]].Add(i);

			for (int c = 0; c < columnNodes.Length; c++)
				columnNodes[c] = columnNodes[c]
					.OrderBy(i => sortHint[i], StringComparer.Ordinal)
					.ThenBy(i => key[i], StringComparer.Ordinal)
					.ToList();

			posInColumn = new int[n];
			for (int c = 0; c < columnNodes.Length; c++) ReindexColumn(c);
		}

		private void ReindexColumn(int c)
		{
			var col = columnNodes[c];
			for (int i = 0; i < col.Count; i++) posInColumn[col[i]] = i;
		}

		// A node's place in its own column, normalised to 0..1 so it can be averaged with
		// positions from columns of a different size. A column of one reads 0.5, a neutral
		// value to land on when there is nothing else to go by.
		private float Normalised(int node)
		{
			var col = columnNodes[column[node]];
			return col.Count <= 1 ? 0.5f : (float)posInColumn[node] / (col.Count - 1);
		}

		// Step 3b: barycentre sweeps. Left-to-right sorts each column by the mean normalised
		// position of its predecessors (columns to the left, already refreshed earlier in
		// this same sweep); right-to-left does the same with successors. A node with nothing
		// on the relevant side sorts by its own current position instead, so a column where
		// nothing has a signal reproduces its current order exactly. Crossings between
		// directly adjacent columns are cheap to count at this size, so every sweep is kept
		// only if it does not make things worse; the best ordering seen wins in the end.
		private void OrderWithinColumns()
		{
			var best = CloneColumns();
			long bestCrossings = CountCrossings();

			for (int sweep = 0; sweep < options.OrderSweeps; sweep++)
			{
				bool leftToRight = sweep % 2 == 0;
				if (leftToRight)
					for (int c = 0; c <= maxColumn; c++) SortColumnBy(c, preds);
				else
					for (int c = maxColumn; c >= 0; c--) SortColumnBy(c, succs);

				long crossings = CountCrossings();
				if (crossings < bestCrossings)
				{
					bestCrossings = crossings;
					best = CloneColumns();
				}
			}

			columnNodes = best;
			for (int c = 0; c < columnNodes.Length; c++) ReindexColumn(c);
		}

		private void SortColumnBy(int c, List<int>[] side)
		{
			var col = columnNodes[c];
			if (col.Count < 2) return;

			var keyOf = new float[col.Count];
			for (int i = 0; i < col.Count; i++)
			{
				int u = col[i];
				var neighbours = side[u];
				if (neighbours.Count == 0)
				{
					keyOf[i] = Normalised(u);
				}
				else
				{
					float sum = 0f;
					foreach (int v in neighbours) sum += Normalised(v);
					keyOf[i] = sum / neighbours.Count;
				}
			}

			// A stable sort with the original index as tie-break: List.Sort is not stable,
			// so this decorates with position and lets OrderBy (which is) do the work.
			var order = new int[col.Count];
			for (int i = 0; i < col.Count; i++) order[i] = i;
			var sorted = order.OrderBy(i => keyOf[i]).ThenBy(i => i).ToArray();

			var newCol = new List<int>(col.Count);
			foreach (int i in sorted) newCol.Add(col[i]);
			columnNodes[c] = newCol;
			ReindexColumn(c);
		}

		private List<int>[] CloneColumns()
		{
			var clone = new List<int>[columnNodes.Length];
			for (int c = 0; c < columnNodes.Length; c++) clone[c] = new List<int>(columnNodes[c]);
			return clone;
		}

		// Only edges that run between two directly-adjacent columns are counted: a node's
		// column is one past its furthest predecessor, not its nearest, so some edges skip
		// columns altogether and have no single boundary to cross at.
		private long CountCrossings()
		{
			if (maxColumn < 0) return 0;
			var byLeftColumn = new List<(int FromPos, int ToPos)>[maxColumn + 1];
			for (int c = 0; c <= maxColumn; c++) byLeftColumn[c] = new List<(int, int)>();

			foreach (var (from, to) in layeringEdges)
			{
				int cf = column[from];
				if (column[to] != cf + 1) continue;
				byLeftColumn[cf].Add((posInColumn[from], posInColumn[to]));
			}

			long total = 0;
			for (int c = 0; c <= maxColumn; c++)
			{
				var list = byLeftColumn[c];
				if (list.Count < 2) continue;
				var sorted = list.OrderBy(p => p.FromPos).ToList();
				for (int i = 0; i < sorted.Count; i++)
					for (int j = i + 1; j < sorted.Count; j++)
						if (sorted[i].ToPos > sorted[j].ToPos) total++;
			}
			return total;
		}

		// Step 4 (X): a column is as wide as its widest node; columns sit left-aligned,
		// ColumnGap apart, the first at X = 0.
		private void AssignX()
		{
			nodeX = new float[n];
			if (maxColumn < 0) return;

			var columnWidth = new float[maxColumn + 1];
			for (int c = 0; c <= maxColumn; c++)
			{
				float w = 0f;
				foreach (int u in columnNodes[c]) if (width[u] > w) w = width[u];
				columnWidth[c] = w;
			}

			var columnLeft = new float[maxColumn + 1];
			columnLeft[0] = 0f;
			for (int c = 1; c <= maxColumn; c++) columnLeft[c] = columnLeft[c - 1] + columnWidth[c - 1] + options.ColumnGap;

			for (int c = 0; c <= maxColumn; c++)
				foreach (int u in columnNodes[c]) nodeX[u] = columnLeft[c];
		}

		// Step 5 (Y): stack each column top to bottom with RowGap between nodes, then relax.
		// Each pass gives every node a desired centre — the mean centre of all its neighbours,
		// both sides at once — a column at a time, alternating sweep direction so a column
		// sees its already-updated neighbours as often as its stale ones. Within a column, a
		// forward clamp (top to bottom) and a backward clamp (bottom to top) each produce a
		// valid, gap-respecting stack on their own; since both keep at least RowGap between
		// neighbours, so does their average, which is what every pass keeps — no overlap is
		// ever possible, whatever the desired centres say. A final shift brings the top of
		// the whole drawing to Y = 0.
		private void AssignY()
		{
			nodeTop = new float[n];
			if (maxColumn < 0) return;

			var centre = new float[n];
			for (int c = 0; c <= maxColumn; c++)
			{
				float y = 0f;
				foreach (int u in columnNodes[c])
				{
					centre[u] = y + height[u] / 2f;
					y += height[u] + options.RowGap;
				}
			}

			for (int pass = 0; pass < options.RelaxPasses; pass++)
			{
				bool leftToRight = pass % 2 == 0;
				if (leftToRight)
					for (int c = 0; c <= maxColumn; c++) RelaxColumn(c, centre);
				else
					for (int c = maxColumn; c >= 0; c--) RelaxColumn(c, centre);
			}

			float minTop = float.MaxValue;
			for (int i = 0; i < n; i++)
			{
				if (loose[i]) continue;
				nodeTop[i] = centre[i] - height[i] / 2f;
				if (nodeTop[i] < minTop) minTop = nodeTop[i];
			}
			if (minTop == float.MaxValue) minTop = 0f;
			for (int i = 0; i < n; i++)
				if (!loose[i]) nodeTop[i] -= minTop;
		}

		private void RelaxColumn(int c, float[] centre)
		{
			var col = columnNodes[c];
			int count = col.Count;
			if (count == 0) return;

			var desired = new float[count];
			for (int i = 0; i < count; i++)
			{
				int u = col[i];
				int total = preds[u].Count + succs[u].Count;
				if (total == 0) { desired[i] = centre[u]; continue; }
				float sum = 0f;
				foreach (int v in preds[u]) sum += centre[v];
				foreach (int v in succs[u]) sum += centre[v];
				desired[i] = sum / total;
			}

			var forward = new float[count];
			forward[0] = desired[0];
			for (int i = 1; i < count; i++)
			{
				float minGap = height[col[i - 1]] / 2f + options.RowGap + height[col[i]] / 2f;
				forward[i] = Math.Max(desired[i], forward[i - 1] + minGap);
			}

			var backward = new float[count];
			backward[count - 1] = desired[count - 1];
			for (int i = count - 2; i >= 0; i--)
			{
				float minGap = height[col[i]] / 2f + options.RowGap + height[col[i + 1]] / 2f;
				backward[i] = Math.Min(desired[i], backward[i + 1] - minGap);
			}

			for (int i = 0; i < count; i++) centre[col[i]] = (forward[i] + backward[i]) / 2f;
		}

		// Loose nodes join no column. Ordered by SortHint then Key, they are packed into a
		// grid of Options.LooseColumns per row — sized the same way the main drawing is,
		// column by widest node and row by tallest — and set below everything else.
		private void PlaceLooseNodes()
		{
			var looseNodes = new List<int>();
			for (int i = 0; i < n; i++) if (loose[i]) looseNodes.Add(i);
			if (looseNodes.Count == 0) return;

			looseNodes = looseNodes
				.OrderBy(i => sortHint[i], StringComparer.Ordinal)
				.ThenBy(i => key[i], StringComparer.Ordinal)
				.ToList();

			int perRow = Math.Max(1, options.LooseColumns);
			int rows = (looseNodes.Count + perRow - 1) / perRow;
			var colWidth = new float[perRow];
			var rowHeight = new float[rows];

			for (int pos = 0; pos < looseNodes.Count; pos++)
			{
				int r = pos / perRow, c = pos % perRow;
				int u = looseNodes[pos];
				if (width[u] > colWidth[c]) colWidth[c] = width[u];
				if (height[u] > rowHeight[r]) rowHeight[r] = height[u];
			}

			var colLeft = new float[perRow];
			for (int c = 1; c < perRow; c++) colLeft[c] = colLeft[c - 1] + colWidth[c - 1] + options.ColumnGap;

			var rowTop = new float[rows];
			for (int r = 1; r < rows; r++) rowTop[r] = rowTop[r - 1] + rowHeight[r - 1] + options.RowGap;

			float drawingBottom = 0f;
			bool anyMain = false;
			for (int i = 0; i < n; i++)
			{
				if (loose[i]) continue;
				anyMain = true;
				float bottom = nodeTop[i] + height[i];
				if (bottom > drawingBottom) drawingBottom = bottom;
			}
			float offset = anyMain ? drawingBottom + options.RowGap : 0f;

			for (int pos = 0; pos < looseNodes.Count; pos++)
			{
				int r = pos / perRow, c = pos % perRow;
				int u = looseNodes[pos];
				nodeX[u] = colLeft[c];
				nodeTop[u] = offset + rowTop[r];
			}
		}

		private Dictionary<string, (float X, float Y)> BuildResult()
		{
			var result = new Dictionary<string, (float X, float Y)>(n, StringComparer.Ordinal);
			for (int i = 0; i < n; i++) result[key[i]] = (nodeX[i], nodeTop[i]);
			return result;
		}
	}
}
