using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// One connected set of ground: free to walk (<see cref="IslandData.Areas"/>) or
/// connected once built (<see cref="IslandData.Reaches"/>).
/// </summary>
/// <param name="Id">Index into the owning list; also the value in <see cref="IslandData.Walk"/> / <see cref="IslandData.Reach"/>.</param>
/// <param name="Area">Cells.</param>
/// <param name="Low">Lowest crossing level in the set, slabs.</param>
/// <param name="High">Highest crossing level in the set, slabs.</param>
/// <param name="Min">Bounding box corner, cells.</param>
/// <param name="Max">Bounding box corner, cells.</param>
public readonly record struct WalkArea(int Id, int Area, short Low, short High,
                                       Vector2I Min, Vector2I Max)
{
    /// <summary>Big enough to be a place; under <see cref="Traversal.MinDistrictArea"/> it is broken ground (benches, ledges).</summary>
    public bool IsDistrict => Area >= Traversal.MinDistrictArea;
}
