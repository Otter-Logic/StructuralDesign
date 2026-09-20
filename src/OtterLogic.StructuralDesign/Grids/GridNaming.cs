namespace OtterLogic.StructuralDesign;

/// <summary>How a line stands, named from the prototypes in <see cref="GridNaming"/>.</summary>
public enum LineOrientation
{
    /// <summary>Lies level — a beam, a joist, a tie.</summary>
    Level,

    /// <summary>Half-way between — a brace, a rafter, a raking member.</summary>
    Pitched,

    /// <summary>Stands up — a column, a post, a hanger.</summary>
    Plumb,

    /// <summary>No length at the document's tolerance, so no direction to read.</summary>
    Degenerate,
}

/// <summary>
/// The only hard-coded engineering in grid and level inference: what things are
/// called. Everything that measures — which heights are levels, which directions
/// the grid runs, where each gridline sits, what is off it — is read from the
/// model's own population by <see cref="OtterLogic.Unsupervised.Clustering.ValueBands"/>.
/// <para>
/// Kept in one file so an office with other conventions changes one file. The
/// orientation prototypes match Structural-Analysis's <c>Vocabulary</c>; the two
/// cannot share a copy while neither domain may reference the other, and both
/// belong in Core once a third needs them.
/// </para>
/// </summary>
public static class GridNaming
{
    /// <summary>
    /// Orientation prototypes, as the sine of a line's inclination: level lies at
    /// 0, plumb stands at 1, pitched sits at 45 degrees between. A band of lines is
    /// named by the prototype nearest its median, so a roof whose beams all sit at a
    /// few degrees reads as level, and a leaning column as plumb — prototypes, not
    /// cut-offs.
    /// </summary>
    public static readonly (LineOrientation Orientation, double Sine)[] Prototypes =
    {
        (LineOrientation.Level, 0.0),
        (LineOrientation.Pitched, Math.Sqrt(0.5)),
        (LineOrientation.Plumb, 1.0),
    };

    /// <summary>The prototype nearest a line's inclination sine.</summary>
    public static LineOrientation Nearest(double sine)
        => Prototypes.OrderBy(p => Math.Abs(p.Sine - sine)).First().Orientation;

    /// <summary>Levels by height, lowest first: Level 00, Level 01, ...</summary>
    public static string Level(int index) => $"Level {index:00}";

    /// <summary>
    /// Gridline names for a family, in order of position. The family with the most
    /// gridlines is numbered, the next lettered, and any further families — a
    /// rotated wing — lettered with a prime per family beyond the second.
    /// </summary>
    /// <param name="family">The family's rank by gridline count, 0 first.</param>
    /// <param name="count">How many gridlines it has.</param>
    public static string[] Gridlines(int family, int count)
    {
        if (family == 0)
            return Enumerable.Range(1, count).Select(i => i.ToString()).ToArray();

        string primes = new('\'', Math.Max(0, family - 1));
        return Letters(count).Select(letter => letter + primes).ToArray();
    }

    /// <summary>
    /// A, B, C ... skipping I and O, which read as 1 and 0 on a drawing; past Z,
    /// AA, AB and so on.
    /// </summary>
    public static string[] Letters(int count)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        var names = new string[count];

        for (int i = 0; i < count; i++)
        {
            int k = i;
            string name = string.Empty;
            do
            {
                name = alphabet[k % alphabet.Length] + name;
                k = k / alphabet.Length - 1;
            }
            while (k >= 0);

            names[i] = name;
        }

        return names;
    }

    /// <summary>A column's grid position, lettered family first: "B/3".</summary>
    public static string Intersection(IEnumerable<string> gridlines)
        => string.Join("/", gridlines.OrderBy(name => char.IsDigit(name[0]) ? 1 : 0).ThenBy(name => name));
}
