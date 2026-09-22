namespace OtterLogic.StructuralDesign;

/// <summary>
/// The only hard-coded engineering in grid and level inference: what things are
/// called. Everything that measures — which heights are levels, which directions
/// the grid runs, where each gridline sits, what is off it — is read from the
/// model's own population by <see cref="OtterLogic.Unsupervised.Clustering.ValueBands"/>.
/// <para>
/// Kept in one file so an office with other conventions changes one file. How a
/// line stands — level, pitched, plumb — is not a convention but a reading of the
/// model, and it moved down to <see cref="OtterLogic.StructuralEngine.LineOrientations"/>
/// the day a third toolkit needed it.
/// </para>
/// </summary>
public static class GridNaming
{
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
