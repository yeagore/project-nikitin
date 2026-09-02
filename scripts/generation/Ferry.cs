using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// A ferry berth: one dry walkable quay cell and the water cell beside it — a domino.
/// Two berths are linked when their water is the same <see cref="Body"/>; a waterfall
/// cuts a body in two, and a stream is forded rather than ferried.
/// </summary>
/// <param name="Land">The quay: dry, walkable ground on the shore.</param>
/// <param name="Water">The water cell it faces, and puts a hull on.</param>
/// <param name="Level">Slab level of the water surface here.</param>
/// <param name="Body">Index into the Domain's water bodies; berths on different bodies are never linked, however close.</param>
public readonly record struct FerryBerth(Vector2I Land, Vector2I Water, short Level, int Body);
