using OtterLogic.Unsupervised.Clustering;
using OtterLogic.StructuralEngine;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// Reads the levels and structural grid a model's lines imply, from the lines
/// alone — for a model that arrived without them.
/// <para>
/// Every stage is the same move: take one measurement across the whole model and
/// let <see cref="ValueBands"/> find where it gathers. Nothing here knows a floor
/// height, a bay size or a tolerance for "on the grid"; each is read from the
/// model's own population, and only the names come from <see cref="GridNaming"/>.
/// </para>
/// <list type="number">
/// <item><b>Orientation.</b> Band every line's inclination and name each band by the
/// nearest prototype — level, pitched, plumb.</item>
/// <item><b>Levels.</b> Band the heights of every level and plumb line's ends. Pitched
/// lines are left out: a brace's ends mostly land on levels anyway, and a rafter's
/// ridge would add a height that is not a floor.</item>
/// <item><b>Directions.</b> Band the plan direction of every level line, on a circle of
/// 180 degrees, so 179.8 and 0.2 are one direction.</item>
/// <item><b>Gridlines.</b> For each direction, band the positions across it of the
/// primary framing — level lines with an end on a column end, and the columns they
/// frame. Primary only because joists at close centres would otherwise fill every
/// gap between gridlines and no gridline would stand out; and only the columns a
/// direction's own framing reaches, so a rotated wing's columns do not scatter
/// across the main grid's positions.</item>
/// <item><b>Placement.</b> Within every level and gridline, flag the elements further
/// from its median than its own scatter explains — the column 12 mm off, the beam
/// 25 mm high.</item>
/// </list>
/// </summary>
public static class GridLevelInference
{
    /// <summary>Infers levels and grid from line elements.</summary>
    /// <param name="starts">n x 3 start points.</param>
    /// <param name="ends">n x 3 end points.</param>
    /// <param name="options">Settings; null for the defaults.</param>
    public static GridLevelResult Infer(double[,] starts, double[,] ends, GridLevelOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(starts);
        ArgumentNullException.ThrowIfNull(ends);
        options ??= new GridLevelOptions();
        options.Validate();

        int n = starts.GetLength(0);
        if (starts.GetLength(1) != 3 || ends.GetLength(1) != 3 || ends.GetLength(0) != n)
            throw new ArgumentException("Starts and ends must both be n x 3, one row per line.", nameof(ends));
        if (n == 0)
            throw new ArgumentException("Need at least one line.", nameof(starts));

        for (int i = 0; i < n; i++)
            for (int a = 0; a < 3; a++)
                if (!double.IsFinite(starts[i, a]) || !double.IsFinite(ends[i, a]))
                    throw new ArgumentException($"Line {i} has a coordinate that is not finite.", nameof(starts));

        double tolerance = options.Tolerance;
        var lengths = options.Banding with { Resolution = tolerance };
        var angles = options.Banding with { Resolution = 0.0 };
        var notes = new List<string>();

        var a0 = Enumerable.Range(0, n).Select(i => Vec.Row(starts, i)).ToArray();
        var a1 = Enumerable.Range(0, n).Select(i => Vec.Row(ends, i)).ToArray();

        var orientation = Orient(a0, a1, tolerance, angles, notes);

        var issues = new List<PlacementIssue>();
        var (levels, startLevel, endLevel) = FindLevels(a0, a1, orientation, lengths, issues, notes);

        double baseHeight = levels.Count > 0
            ? levels[0].Elevation
            : Enumerable.Range(0, n).Min(i => Math.Min(a0[i].Z, a1[i].Z));

        var (families, gridlines, gridLabel) = FindGrid(a0, a1, orientation, tolerance, baseHeight, options, lengths, angles, issues, notes);

        var ordered = issues
            .OrderByDescending(issue => double.IsNaN(issue.Deviation) ? -1.0 : Math.Abs(issue.Deviation))
            .ThenBy(issue => issue.Element)
            .ToList();

        return new GridLevelResult(orientation, levels, families, gridlines, startLevel, endLevel, gridLabel, ordered, notes);
    }

    private static LineOrientation[] Orient(Vec[] a0, Vec[] a1, double tolerance, ValueBandsOptions banding, List<string> notes)
    {
        var orientation = LineOrientations.Classify(a0, a1, tolerance, banding, notes);

        int pitched = orientation.Count(o => o == LineOrientation.Pitched);
        if (pitched > 0)
            notes.Add($"{pitched} pitched line(s) — braces, rafters — were left out of finding levels and grid.");

        return orientation;
    }

    private static (List<Level> Levels, int[] StartLevel, int[] EndLevel) FindLevels(
        Vec[] a0, Vec[] a1, LineOrientation[] orientation, ValueBandsOptions banding,
        List<PlacementIssue> issues, List<string> notes)
    {
        int n = a0.Length;
        var startLevel = Enumerable.Repeat(-1, n).ToArray();
        var endLevel = Enumerable.Repeat(-1, n).ToArray();

        var owners = new List<(int Line, bool IsEnd)>();
        var heights = new List<double>();

        for (int i = 0; i < n; i++)
        {
            if (orientation[i] is not (LineOrientation.Level or LineOrientation.Plumb))
                continue;

            owners.Add((i, false));
            heights.Add(a0[i].Z);
            owners.Add((i, true));
            heights.Add(a1[i].Z);
        }

        var levels = new List<Level>();
        if (heights.Count == 0)
        {
            notes.Add("No level or plumb lines, so no levels could be read.");
            return (levels, startLevel, endLevel);
        }

        var bands = ValueBands.Fit(heights, banding);
        if (!bands.Learned)
            notes.Add($"Only {heights.Count} line ends to read levels from — too few to trust the gaps between "
                + "them, so only heights that coincide were grouped.");

        for (int b = 0; b < bands.Bands.Count; b++)
        {
            var band = bands.Bands[b];
            var elements = band.Members.Select(m => owners[m].Line).Distinct().OrderBy(i => i).ToArray();
            levels.Add(new Level(GridNaming.Level(b), band.Median, band.Spread, elements));
        }

        for (int m = 0; m < owners.Count; m++)
        {
            var (line, isEnd) = owners[m];
            if (isEnd)
                endLevel[line] = bands.Band[m];
            else
                startLevel[line] = bands.Band[m];
        }

        // Pitched ends are not part of the population, but a brace landing on a
        // floor is on that floor: within the band's own outlier limit of its median.
        for (int i = 0; i < n; i++)
        {
            if (orientation[i] != LineOrientation.Pitched)
                continue;

            startLevel[i] = Nearest(levels, a0[i].Z, banding);
            endLevel[i] = Nearest(levels, a1[i].Z, banding);
        }

        // One issue per line and level, at the end furthest off.
        foreach (var group in bands.Outliers.GroupBy(m => (owners[m].Line, bands.Band[m])))
        {
            int worst = group.OrderByDescending(m => Math.Abs(bands.Deviation[m])).First();
            double deviation = bands.Deviation[worst];
            var level = levels[bands.Band[worst]];
            string side = deviation > 0.0 ? "above" : "below";

            issues.Add(new PlacementIssue(group.Key.Line, level.Name, deviation,
                $"Line {group.Key.Line} is {Math.Abs(deviation):G4} {side} {level.Name}."));
        }

        return (levels, startLevel, endLevel);
    }

    private static int Nearest(List<Level> levels, double z, ValueBandsOptions banding)
    {
        for (int b = 0; b < levels.Count; b++)
        {
            double limit = Math.Max(banding.OutlierMultiple * levels[b].Spread, banding.Resolution);
            if (Math.Abs(z - levels[b].Elevation) <= limit)
                return b;
        }

        return -1;
    }

    private static (List<GridFamily> Families, List<Gridline> Gridlines, string[] GridLabel) FindGrid(
        Vec[] a0, Vec[] a1, LineOrientation[] orientation, double tolerance, double baseHeight,
        GridLevelOptions options, ValueBandsOptions lengths, ValueBandsOptions angles,
        List<PlacementIssue> issues, List<string> notes)
    {
        int n = a0.Length;
        var gridLabel = Enumerable.Repeat(string.Empty, n).ToArray();
        var families = new List<GridFamily>();
        var gridlines = new List<Gridline>();

        var columns = Enumerable.Range(0, n).Where(i => orientation[i] == LineOrientation.Plumb).ToArray();
        var beams = Enumerable.Range(0, n)
            .Where(i => orientation[i] == LineOrientation.Level && Plan(a1[i] - a0[i]).Length > tolerance)
            .ToArray();

        if (columns.Length == 0 || beams.Length == 0)
        {
            notes.Add(columns.Length == 0
                ? "No plumb lines, so no columns for a grid to run through."
                : "No level lines, so no directions for a grid to run in.");
            return (families, gridlines, gridLabel);
        }

        // Which column each beam end lands on, at the document's tolerance.
        var framedColumns = new List<int>[n];
        foreach (int beam in beams)
        {
            framedColumns[beam] = columns
                .Where(c => Touches(a0[beam], a0[c], a1[c], tolerance) || Touches(a1[beam], a0[c], a1[c], tolerance))
                .ToList();
        }

        var directions = beams.Select(i => Degrees(Plan(a1[i] - a0[i]))).ToArray();
        var directionBands = ValueBands.Fit(directions, angles with { Period = 180.0 });

        var candidates = new List<(double Direction, Vec Normal, Vec Along, List<(double Offset, double Low, double High, int[] Columns, int[] Beams, List<(int Line, double Deviation)> Off)> Lines)>();

        for (int f = 0; f < directionBands.Bands.Count; f++)
        {
            double direction = directionBands.Bands[f].Median;
            double radians = direction * Math.PI / 180.0;
            var along = new Vec(Math.Cos(radians), Math.Sin(radians), 0.0);
            var normal = new Vec(-along.Y, along.X, 0.0);
            if ((Math.Abs(normal.X) >= Math.Abs(normal.Y) ? normal.X : normal.Y) < 0.0)
                normal = -1.0 * normal;

            var primary = beams
                .Where((_, k) => directionBands.Band[k] == f)
                .Where(beam => framedColumns[beam].Count > 0)
                .ToArray();

            var reached = primary.SelectMany(beam => framedColumns[beam]).Distinct().OrderBy(c => c).ToArray();
            if (primary.Length == 0)
                continue;

            // One population: every primary beam and every column it reaches, each
            // by its position across the direction.
            var members = primary.Select(beam => (Line: beam, IsColumn: false))
                .Concat(reached.Select(column => (Line: column, IsColumn: true)))
                .ToArray();
            var offsets = members.Select(member => Midpoint(a0[member.Line], a1[member.Line]).Dot(normal)).ToArray();

            var bands = ValueBands.Fit(offsets, lengths);
            var lines = new List<(double, double, double, int[], int[], List<(int, double)>)>();

            for (int b = 0; b < bands.Bands.Count; b++)
            {
                var inBand = bands.Bands[b].Members;
                var bandColumns = inBand.Where(m => members[m].IsColumn).Select(m => members[m].Line).ToArray();
                if (bandColumns.Length == 0)
                    continue;

                var bandBeams = inBand.Where(m => !members[m].IsColumn).Select(m => members[m].Line).ToArray();

                var along0 = bandColumns.Select(c => Midpoint(a0[c], a1[c]).Dot(along))
                    .Concat(bandBeams.SelectMany(beam => new[] { a0[beam].Dot(along), a1[beam].Dot(along) }))
                    .ToArray();

                var off = bands.Outliers
                    .Where(m => bands.Band[m] == b)
                    .Select(m => (members[m].Line, bands.Deviation[m]))
                    .ToList();

                lines.Add((bands.Bands[b].Median, along0.Min() - options.Extension, along0.Max() + options.Extension,
                    bandColumns, bandBeams, off));
            }

            if (lines.Count > 0)
                candidates.Add((direction, normal, along, lines));
        }

        int unframedDirections = directionBands.Bands.Count - candidates.Count;
        if (unframedDirections > 0)
            notes.Add($"{unframedDirections} direction(s) of level lines frame into no column — joists or ties "
                + "running that way — so no gridlines run in them.");

        // Most gridlines first, so the main grid is numbered; ties by direction.
        var ranked = candidates
            .Select((candidate, index) => (candidate, index))
            .OrderByDescending(pair => pair.candidate.Lines.Count)
            .ThenBy(pair => pair.candidate.Direction)
            .Select(pair => pair.candidate)
            .ToList();

        var onGridlines = new List<string>[n];

        for (int f = 0; f < ranked.Count; f++)
        {
            var (direction, normal, along, lines) = ranked[f];
            var names = GridNaming.Gridlines(f, lines.Count);
            var indices = new List<int>();

            for (int g = 0; g < lines.Count; g++)
            {
                var (offset, low, high, bandColumns, bandBeams, off) = lines[g];
                var origin = offset * normal + new Vec(0.0, 0.0, baseHeight);
                var start = origin + low * along;
                var end = origin + high * along;

                indices.Add(gridlines.Count);
                gridlines.Add(new Gridline(names[g], f, offset,
                    new[] { start.X, start.Y, start.Z }, new[] { end.X, end.Y, end.Z }, bandColumns, bandBeams));

                foreach (int column in bandColumns)
                    (onGridlines[column] ??= new List<string>()).Add(names[g]);

                foreach (var (line, deviation) in off)
                {
                    string what = orientation[line] == LineOrientation.Plumb ? "Column" : "Line";
                    issues.Add(new PlacementIssue(line, names[g], deviation,
                        $"{what} {line} is {Math.Abs(deviation):G4} off gridline {names[g]}."));
                }
            }

            families.Add(new GridFamily(direction, indices.ToArray()));
        }

        foreach (int column in columns)
        {
            if (onGridlines[column] is { } names)
                gridLabel[column] = GridNaming.Intersection(names);
            else if (families.Count > 0)
                issues.Add(new PlacementIssue(column, string.Empty, double.NaN,
                    $"Column {column} stands on no gridline: no primary framing along the grid reaches it."));
        }

        return (families, gridlines, gridLabel);
    }

    private static Vec Plan(Vec v) => new(v.X, v.Y, 0.0);

    private static Vec Midpoint(Vec a, Vec b) => 0.5 * (Plan(a) + Plan(b));

    /// <summary>Plan direction, 0 to 180 degrees — a line has no sense of which way along it runs.</summary>
    private static double Degrees(Vec plan)
    {
        double degrees = Math.Atan2(plan.Y, plan.X) * 180.0 / Math.PI;
        return degrees < 0.0 ? degrees + 180.0 : degrees >= 180.0 ? degrees - 180.0 : degrees;
    }

    private static bool Touches(Vec point, Vec columnStart, Vec columnEnd, double tolerance)
        => point.DistanceTo(columnStart) <= tolerance || point.DistanceTo(columnEnd) <= tolerance;
}
