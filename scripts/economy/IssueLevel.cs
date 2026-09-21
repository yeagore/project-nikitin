namespace ProjectNikitin.Economy;

public enum IssueLevel
{
	/// <summary>Worth knowing: a loose good, a loop.</summary>
	Note,

	/// <summary>Probably unfinished: a recipe that takes nothing, a tag nothing carries.</summary>
	Warning,

	/// <summary>Broken: a recipe that makes nothing, a slot naming a good that is not in the web.</summary>
	Error,
}
