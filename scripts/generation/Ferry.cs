using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// A <b>ferry berth</b>: one land cell and one water cell beside it — a domino —
/// which is all the room a ferry station needs.
///
/// <para>It is the answer to a question the reach analysis used to get wrong.
/// Water is not walkable, so a lake read as a gap; a gap inside the bridge span
/// read as bridgeable; and a chain of one-cell islets across a lagoon therefore
/// counted as one place, joined by bridges that landed on a single slab of
/// ground. Water is <i>crossable</i>, but by boat, and a boat has to be able to
/// tie up: a quay on the shore and open water in front of it.</para>
///
/// <para><b>Two berths are linked when their water is the same water</b> — the
/// two shores of one lake, or two reaches of one navigable river with no fall
/// between them. That is what <see cref="Body"/> records: waterfalls cut a body
/// in two, because nothing is sailing up one.</para>
///
/// <para>Streams are not ferried. A stream is one slab of water in a bed you
/// step down into and out of, so it is forded for nothing — a ferry across it
/// would be infrastructure bought to replace a free step. Lakes and navigable
/// rivers are the water that actually divides a Domain.</para>
/// </summary>
/// <param name="Land">The quay: dry, walkable ground on the shore.</param>
/// <param name="Water">The water cell it faces, and puts a hull on.</param>
/// <param name="Level">Slab level of the water surface here.</param>
/// <param name="Body">
/// Which body of water it reaches — an index into the Domain's water bodies. Two
/// berths on one body are linked; two on different bodies are not, however close
/// they stand.
/// </param>
public readonly record struct FerryBerth(Vector2I Land, Vector2I Water, short Level, int Body);
