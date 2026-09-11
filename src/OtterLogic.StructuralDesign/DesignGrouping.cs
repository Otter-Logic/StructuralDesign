namespace OtterLogic.StructuralDesign;

/// <summary>
/// Groups structural elements for design — foundations, connections, anything
/// designed once per group rather than once per element — from the
/// six-degree-of-freedom demand on each.
/// <para>
/// A thin structural reading of <see cref="SixDofBehaviourClassifier"/>, which
/// does the grouping. What this adds is the one rule a behaviour grouping does not
/// have and a design grouping must: <b>every element ends up in exactly one design
/// group</b>. The classifier may leave an element unassigned when HDBSCAN finds it
/// fits no behaviour family. For understanding a structure that is the right
/// answer; for designing one it is not, because an unassigned element still has to
/// be designed. So each becomes a one-off group of its own.
/// </para>
/// <para>
/// A group of its own rather than the nearest group, deliberately. An element that
/// fits no family is usually the unusually loaded one. Filed with its nearest
/// family, it either drags that family's governing forces up for every element in
/// it, or — if the family is designed off its typical member — gets designed for
/// less than it carries. Designing it separately is the conservative reading of
/// "the data does not support grouping this".
/// </para>
/// <para>
/// Every force is read with its sign, which is right when direction changes the
/// design. For foundations it mostly does not — see <see cref="FoundationGrouping"/>.
/// </para>
/// </summary>
public static class DesignGrouping
{
    /// <summary>
    /// Groups n elements from their six forces under one load combination: one
    /// list per degree of freedom, one value per element in each, every force read
    /// with its sign.
    /// <para>
    /// One combination at a time. To group on an envelope over several, reduce
    /// each force to one governing value per element first and pass those — which
    /// value governs is a design decision, and it is the caller's to make.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">The six lists are not all the same length.</exception>
    public static DesignGroupingResult Group(
        IReadOnlyList<double> fx, IReadOnlyList<double> fy, IReadOnlyList<double> fz,
        IReadOnlyList<double> mx, IReadOnlyList<double> my, IReadOnlyList<double> mz,
        SixDofClassificationOptions? options = null)
    {
        var (features, names) = SixDof.Signed(SixDof.Read(fx, fy, fz, mx, my, mz));
        return Group(features, names, options, new[] { Everything(features) }, Array.Empty<string>());
    }

    /// <summary>
    /// Groups n elements described by their degree-of-freedom demands.
    /// </summary>
    /// <param name="demands">
    /// n x d, one row per element. Six columns is the intended case — Fx, Fy, Fz,
    /// Mx, My, Mz — but any consistent set of demand columns works.
    /// </param>
    /// <param name="options">Classifier settings. The intended call passes none.</param>
    public static DesignGroupingResult Group(double[,] demands, SixDofClassificationOptions? options = null)
        => Group(demands, null, options, new[] { Everything(demands) }, Array.Empty<string>());

    /// <summary>
    /// The one implementation: classifies each part on its own and numbers the
    /// design groups across all of them.
    /// <para>
    /// Parts are for a hard rule the classifier knows nothing of — elements that
    /// may never share a design, such as foundations that see uplift and ones that
    /// never do. Classifying everything together and splitting afterwards was
    /// tried first and is worse: the classifier sees only distance, and measured
    /// on two edge families alike in all but the sign of their axial force, at
    /// 15% scatter it merged them outright in most of twenty runs, leaving a
    /// whole family to be split off as one-offs. Classified apart, it never has
    /// to discover the difference.
    /// </para>
    /// </summary>
    /// <param name="features">n x d, one row per element, every part's rows together.</param>
    /// <param name="columnNames">What each column is, for the report. Null for plain degrees of freedom.</param>
    /// <param name="parts">Each part's name and elements, ascending. Together they hold every element once.</param>
    /// <param name="notes">Lines the report should carry — how the forces were read.</param>
    internal static DesignGroupingResult Group(
        double[,] features, string[]? columnNames, SixDofClassificationOptions? options,
        IReadOnlyList<(string? Name, int[] Elements)> parts, string[] notes)
    {
        int n = features.GetLength(0);
        int d = features.GetLength(1);

        if (n < SixDofBehaviourClassifier.MinimumMembers)
            throw new ArgumentException(
                $"Need at least {SixDofBehaviourClassifier.MinimumMembers} elements to group; got {n}.");

        var built = new List<DesignGroupingPart>();
        var behaviour = new List<(int[] Members, int Part)>();
        var oneOffs = new List<(int Element, int Part)>();

        for (int p = 0; p < parts.Count; p++)
        {
            var (name, elements) = parts[p];

            // Too few to classify is not the same as unlike each other, but
            // with nothing to say they are alike, each is designed on its own —
            // the reading this class already gives an element that fits no family.
            if (elements.Length < SixDofBehaviourClassifier.MinimumMembers)
            {
                built.Add(new DesignGroupingPart(name, elements, null));
                oneOffs.AddRange(elements.Select(element => (element, p)));
                continue;
            }

            var rows = new double[elements.Length, d];
            for (int r = 0; r < elements.Length; r++)
                for (int j = 0; j < d; j++)
                    rows[r, j] = features[elements[r], j];

            var classification = SixDofBehaviourClassifier.Classify(rows, options, columnNames);
            built.Add(new DesignGroupingPart(name, elements, classification));

            foreach (var members in classification.Members().Where(members => members.Length > 0))
                behaviour.Add((members.Select(r => elements[r]).ToArray(), p));

            oneOffs.AddRange(classification.Unassigned().Select(r => (elements[r], p)));
        }

        // Behaviour groups largest first, ties to the one holding the lowest
        // element, so the numbering is a property of the answer and does not
        // shuffle when the input changes slightly. Counted directly rather than
        // trusted from the model: a mixture orders by weight, which is close to
        // count but not the same.
        var groups = behaviour
            .OrderByDescending(group => group.Members.Length)
            .ThenBy(group => group.Members[0])
            .ToList();

        int behaviourCount = groups.Count;

        // Then one design group per unassigned element, in element order.
        groups.AddRange(oneOffs.OrderBy(o => o.Element).Select(o => (new[] { o.Element }, o.Part)));

        return new DesignGroupingResult(
            built,
            groups.Select(group => group.Members).ToArray(),
            groups.Select(group => group.Part).ToArray(),
            behaviourCount,
            n,
            notes);
    }

    private static (string? Name, int[] Elements) Everything(double[,] features)
        => (null, Enumerable.Range(0, features.GetLength(0)).ToArray());
}
