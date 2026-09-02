namespace ProjectNikitin.Generation;

/// <summary>
/// A region's assignment: what it is, the level it is built from (slabs), and
/// the rung group it was unioned into. Neighbours in one group share a rung,
/// which is the statement "no cliff belongs here" — the slope limiter enforces
/// it across the border.
/// </summary>
internal readonly struct RegionPlan
{
    public readonly LandformType Type;
    public readonly int Plateau;
    public readonly int RungGroup;

    public RegionPlan(LandformType type, int plateau, int rungGroup)
    {
        Type = type;
        Plateau = plateau;
        RungGroup = rungGroup;
    }
}
