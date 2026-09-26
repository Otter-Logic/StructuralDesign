using OtterLogic.StructuralEngine;

namespace OtterLogic.StructuralDesign;

/// <summary>A level: a height the model's columns stop and start at.</summary>
/// <param name="Name">Its name, from <see cref="GridNaming.Level"/>.</param>
/// <param name="Elevation">The median height of the column ends on it.</param>
/// <param name="Spread">How tightly they hold it — a robust standard deviation. Zero when most sit exactly on it.</param>
/// <param name="Elements">Lines with at least one end on it, the columns that make it and everything landing on it, ascending.</param>
public sealed record Level(string Name, double Elevation, double Spread, int[] Elements);

/// <summary>A direction the grid runs in, and the gridlines along it.</summary>
/// <param name="Direction">Plan angle of the gridlines from the X axis, 0 to 180 degrees.</param>
/// <param name="Gridlines">Indices into <see cref="GridLevelResult.Gridlines"/>, in order of position.</param>
public sealed record GridFamily(double Direction, int[] Gridlines);

/// <summary>A gridline: a line in plan where columns stand and primary framing runs.</summary>
/// <param name="Name">Its name, from <see cref="GridNaming.Gridlines"/>.</param>
/// <param name="Family">Index into <see cref="GridLevelResult.Families"/>.</param>
/// <param name="Offset">Its position across its family's direction — the median of the elements on it.</param>
/// <param name="Start">One end, drawn at the lowest level's height, as x, y, z.</param>
/// <param name="End">The other end.</param>
/// <param name="Columns">Plumb lines standing on it.</param>
/// <param name="Beams">Primary level lines running along it.</param>
public sealed record Gridline(string Name, int Family, double Offset, double[] Start, double[] End, int[] Columns, int[] Beams);

/// <summary>An element that belongs to a gridline or level but is not quite on it.</summary>
/// <param name="Element">The line.</param>
/// <param name="Reference">The gridline or level name.</param>
/// <param name="Deviation">How far off, signed, in model units: above a level, or towards the family's positive side of a gridline.</param>
/// <param name="Message">The same in words.</param>
public sealed record PlacementIssue(int Element, string Reference, double Deviation, string Message);

/// <summary>
/// The levels and grid a model's lines imply, where every line sits on them,
/// and which lines are not quite where the rest of their level or gridline is.
/// </summary>
public sealed class GridLevelResult
{
    internal GridLevelResult(
        LineOrientation[] orientation,
        IReadOnlyList<Level> levels,
        IReadOnlyList<GridFamily> families,
        IReadOnlyList<Gridline> gridlines,
        int[] startLevel,
        int[] endLevel,
        string[] gridLabel,
        IReadOnlyList<PlacementIssue> issues,
        IReadOnlyList<string> notes)
    {
        Orientation = orientation;
        Levels = levels;
        Families = families;
        Gridlines = gridlines;
        StartLevel = startLevel;
        EndLevel = endLevel;
        GridLabel = gridLabel;
        Issues = issues;
        Notes = notes;
    }

    /// <summary>How each line stands, named from its band of inclinations.</summary>
    public LineOrientation[] Orientation { get; }

    /// <summary>The levels, lowest first.</summary>
    public IReadOnlyList<Level> Levels { get; }

    /// <summary>The grid's directions, the one with most gridlines first.</summary>
    public IReadOnlyList<GridFamily> Families { get; }

    /// <summary>Every gridline, family by family, each in order of position.</summary>
    public IReadOnlyList<Gridline> Gridlines { get; }

    /// <summary>Per line, the level its start sits on, or -1 when it is on none — a pitched line, say.</summary>
    public int[] StartLevel { get; }

    /// <summary>Per line, the level its end sits on, or -1.</summary>
    public int[] EndLevel { get; }

    /// <summary>
    /// Per line, its grid position: "B/3" for a column on two gridlines, "3" on one,
    /// empty for a line that is not a column on the grid.
    /// </summary>
    public string[] GridLabel { get; }

    /// <summary>Lines not quite on the level or gridline they belong to, furthest off first.</summary>
    public IReadOnlyList<PlacementIssue> Issues { get; }

    /// <summary>Anything that changed what was found, in plain words.</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>Number of lines read.</summary>
    public int LineCount => Orientation.Length;
}
