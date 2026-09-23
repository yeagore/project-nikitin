namespace ProjectNikitin.Economy;

/// <summary>What a good is within one web, read off its links and never stored.</summary>
public enum GoodRole
{
	/// <summary>Nothing here makes it, uses it or consumes it.</summary>
	Loose,

	/// <summary>Nothing here makes it but the ground, through an extraction: a raw input of this web, whatever it is elsewhere.</summary>
	Source,

	/// <summary>Made here and used here.</summary>
	Intermediate,

	/// <summary>Made here and used by no recipe: it is consumed, or kept.</summary>
	Final,
}
