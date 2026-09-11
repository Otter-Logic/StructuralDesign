namespace OtterLogic.StructuralDesign;

/// <summary>
/// Groups foundations for design from the six reactions at each column base,
/// under one load combination or many.
/// <para>
/// <see cref="DesignGrouping"/> with what a foundation knows about its forces:
/// only the axial force has a direction that changes the design. Compression is
/// a bearing problem and tension is an uplift problem, so Fz keeps its sign.
/// Shear, bending and torsion — Fx, Fy, Mx, My, Mz — are each designed to act
/// either way, so a foundation carrying +80 kN of shear and one carrying −80 kN
/// need the same design, and those five are grouped by size.
/// </para>
/// <para>
/// Grouping by signed value gets this wrong, not merely noisily. Wind or seismic
/// shear across a roughly symmetric building sits around zero, so once each
/// column is standardised, +80 and −80 land at opposite ends of it and look like
/// the two most different foundations in the model. Measured on three families
/// with shear and moments pointing either way, signed grouping found seven groups
/// and 38 one-offs where grouping by size found the three.
/// </para>
/// <para>
/// Over several combinations each foundation is reduced to its own envelope first
/// — see <see cref="FoundationEnvelope"/> — and grouped once, on that. The
/// alternative, grouping under each combination and keeping whichever grouping
/// comes up most often, answers the wrong question: it groups foundations by how
/// they usually behave, and a foundation is designed for how it behaves at worst.
/// A support that lifts under one wind direction in thirty combinations behaves
/// like its neighbours in twenty-nine of them, so a vote files it with them — the
/// very case the uplift rule below exists for. The envelope also stays seven
/// values however many combinations there are, so ten near-identical gravity
/// combinations cannot outvote the one wind combination that governs.
/// </para>
/// <para>
/// Uplift is a rule, not just a feature. Foundations in tension under any
/// combination are grouped separately from those in compression throughout, and
/// no group holds both: a foundation that lifts needs an uplift check that one in
/// compression does not, and grouped together one of them is designed for the
/// wrong thing. Separately rather than hoping the signed Fz separates them,
/// because it does not reliably — see <see cref="DesignGrouping"/>.
/// </para>
/// <para>
/// Saying which foundations are in tension needs to know which sign is
/// compression, and analyses differ. It is read from the forces: the sign of Fz
/// summed over every foundation and every combination. Gravity is in every
/// combination of a real model and loads every foundation the same way, and even
/// where wind lifts a few supports it presses harder on others, so the total has
/// the sign of compression. The report says which sign was taken. Forces with no
/// gravity in them, where the total is near zero, should say which sign is
/// compression through <see cref="FoundationGroupingOptions"/>.
/// </para>
/// </summary>
public static class FoundationGrouping
{
    private const string NeverInUplift = "never in uplift";
    private const string SeesUplift = "sees uplift";

    /// <summary>
    /// Groups n foundations from their six reactions under one load combination:
    /// one list per degree of freedom, one value per foundation, straight out of
    /// the analysis with their signs as they came.
    /// </summary>
    /// <exception cref="ArgumentException">The six lists are not all the same length.</exception>
    public static FoundationGroupingResult Group(
        IReadOnlyList<double> fx, IReadOnlyList<double> fy, IReadOnlyList<double> fz,
        IReadOnlyList<double> mx, IReadOnlyList<double> my, IReadOnlyList<double> mz,
        FoundationGroupingOptions? options = null)
    {
        return Group(SixDof.AsOneCombination(SixDof.Read(fx, fy, fz, mx, my, mz)), options);
    }

    /// <summary>
    /// Groups n foundations from their six reactions under any number of load
    /// combinations: one list per degree of freedom, one entry per foundation, and
    /// in each entry a value per combination — <c>fz[i][c]</c> is the Fz of
    /// foundation i under combination c.
    /// <para>
    /// Feed it combinations, not load cases. The envelope of each force over
    /// unfactored cases — dead load alone, wind alone — is not a value any
    /// foundation is designed for.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The six lists are not all the same length, or the foundations do not all
    /// have the same number of combinations.
    /// </exception>
    public static FoundationGroupingResult Group(
        IReadOnlyList<IReadOnlyList<double>> fx, IReadOnlyList<IReadOnlyList<double>> fy,
        IReadOnlyList<IReadOnlyList<double>> fz, IReadOnlyList<IReadOnlyList<double>> mx,
        IReadOnlyList<IReadOnlyList<double>> my, IReadOnlyList<IReadOnlyList<double>> mz,
        FoundationGroupingOptions? options = null)
        => Group(SixDof.ReadCombinations(fx, fy, fz, mx, my, mz), options);

    /// <summary>The one implementation, over <c>forces[dof][foundation][combination]</c>.</summary>
    private static FoundationGroupingResult Group(double[][][] forces, FoundationGroupingOptions? options)
    {
        options ??= new FoundationGroupingOptions();

        var axial = forces[SixDof.Fz];
        int n = axial.Length;
        int combinations = axial[0].Length;

        bool compressionPositive = options.CompressionPositive
            ?? axial.Sum(foundation => foundation.Sum()) >= 0.0;

        var envelope = new FoundationEnvelope(
            SixDof.LargestSize(forces[0]),
            SixDof.LargestSize(forces[1]),
            SixDof.Largest(axial),
            SixDof.Smallest(axial),
            SixDof.LargestSize(forces[3]),
            SixDof.LargestSize(forces[4]),
            SixDof.LargestSize(forces[5]));

        // The two ends of each foundation's axial force, turned into compression
        // so they mean the same whichever sign the analysis used: most and least
        // compression. Least below zero is uplift.
        double[] mostCompression = compressionPositive ? envelope.FzMax : envelope.FzMin.Select(v => -v).ToArray();
        double[] leastCompression = compressionPositive ? envelope.FzMin : envelope.FzMax.Select(v => -v).ToArray();

        // What the grouping sees, in degree-of-freedom order. With one
        // combination the two ends of Fz are the same number, so it is one column
        // as it came — a duplicate would count the axial force twice.
        var columns = new List<(string Name, Func<int, double> Value)>
        {
            ("|Fx|", i => envelope.Fx[i]),
            ("|Fy|", i => envelope.Fy[i]),
        };

        if (combinations == 1)
        {
            columns.Add(("Fz", i => axial[i][0]));
        }
        else
        {
            columns.Add(("Fz most compression", i => mostCompression[i]));
            columns.Add(("Fz least compression", i => leastCompression[i]));
        }

        columns.Add(("|Mx|", i => envelope.Mx[i]));
        columns.Add(("|My|", i => envelope.My[i]));
        columns.Add(("|Mz|", i => envelope.Mz[i]));

        var (features, names) = SixDof.Features(columns, n);

        // In tension under any one combination, strictly: a foundation that only
        // ever reaches zero never lifts.
        var never = Enumerable.Range(0, n).Where(i => leastCompression[i] >= 0.0).ToArray();
        var sees = Enumerable.Range(0, n).Where(i => leastCompression[i] < 0.0).ToArray();

        var parts = new List<(string? Name, int[] Elements)>();
        if (never.Length > 0) parts.Add((NeverInUplift, never));
        if (sees.Length > 0) parts.Add((SeesUplift, sees));

        var notes = new[]
        {
            $"Compression read as {(compressionPositive ? "positive" : "negative")} Fz"
                + (options.CompressionPositive is null
                    ? ", the sign of the total over every foundation and combination."
                    : ", as set."),
            combinations == 1
                ? "Fz keeps its sign; Fx, Fy, Mx, My and Mz are grouped by size."
                : $"Grouped on each foundation's envelope over {combinations} combinations: the largest "
                    + "|Fx|, |Fy|, |Mx|, |My| and |Mz|, and the most and least compression.",
        };

        var grouping = DesignGrouping.Group(features, names, options.Classification, parts, notes);
        return new FoundationGroupingResult(grouping, envelope, compressionPositive, combinations);
    }
}
