namespace ProjectNikitin.Economy;

/// <summary>Something the analysis found wrong or worth a look, pinned to the node it is about.</summary>
public readonly record struct WebIssue(IssueLevel Level, string Node, string Text);
