namespace ProjectNikitin.Economy;

/// <summary>
/// What a slot accepts is a string: a good's id as it stands (<c>icu</c>), or a tag behind
/// a hash (<c>#kind:golem-heart</c>). A good's id never starts with a hash, so the two cannot collide.
/// </summary>
public static class Acceptor
{
	public const char TagMark = '#';

	public static bool IsTag(string acceptor) => acceptor.Length > 0 && acceptor[0] == TagMark;

	/// <summary>The tag of a tag acceptor, without its hash.</summary>
	public static string TagOf(string acceptor) => acceptor[1..];

	public static string ForTag(string tag) => TagMark + tag;

	public static bool Admits(string acceptor, Good good) =>
		IsTag(acceptor) ? good.Tags.Contains(TagOf(acceptor)) : acceptor == good.Id;
}
