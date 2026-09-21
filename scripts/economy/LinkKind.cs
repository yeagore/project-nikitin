namespace ProjectNikitin.Economy;

public enum LinkKind
{
	/// <summary>A good into a recipe's input slot.</summary>
	Input,

	/// <summary>A recipe's output into the good it makes.</summary>
	Output,

	/// <summary>A good into a consumer.</summary>
	Consumed,
}
