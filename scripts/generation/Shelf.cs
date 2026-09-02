using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// Ground level enough to lay a settlement on: every cell flat or at one lone step,
/// no two neighbours more than a slab apart. <see cref="Width"/> is the largest
/// inscribed square — what tells a yard from a fifty-cell ledge one cell deep.
/// </summary>
/// <param name="Id">Index into <see cref="IslandData.Shelves"/>; also the value in <see cref="IslandData.ShelfId"/>.</param>
/// <param name="Level">Lowest cell, slabs.</param>
/// <param name="Top">Highest cell, slabs.</param>
/// <param name="Area">Cells.</param>
/// <param name="Width">Side of the widest inscribed square, cells.</param>
/// <param name="Min">Bounding box corner, cells.</param>
/// <param name="Max">Bounding box corner, cells.</param>
/// <param name="Center">A cell at the middle of that widest square.</param>
public readonly record struct Shelf(int Id, short Level, short Top, int Area, int Width,
                                    Vector2I Min, Vector2I Max, Vector2I Center)
{
    /// <summary>Slabs from the shelf's lowest cell to its highest.</summary>
    public int Drop => Top - Level;

    /// <summary>Room for a settlement: <see cref="Traversal.MinShelfArea"/> cells and <see cref="Traversal.MinShelfWidth"/> wide.</summary>
    public bool Buildable => Area >= Traversal.MinShelfArea && Width >= Traversal.MinShelfWidth;
}
