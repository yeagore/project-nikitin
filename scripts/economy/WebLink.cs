namespace ProjectNikitin.Economy;

/// <summary>
/// One arrow of a web, between node keys. An input runs from a good to a recipe's input slot
/// <see cref="Port"/>, an output from a recipe's output <see cref="Port"/> to a good, and a
/// consumed link from a good to a consumer. <see cref="Via"/> is the acceptor that made the
/// link (the good's own id, or a <c>#tag</c>), or for an output the good made.
/// </summary>
public readonly record struct WebLink(string From, string To, LinkKind Kind, int Port, string Via)
{
	/// <summary>True when no one drew this link: a tag on the slot and on the good implies it.</summary>
	public bool ByTag => Kind != LinkKind.Output && Acceptor.IsTag(Via);
}
