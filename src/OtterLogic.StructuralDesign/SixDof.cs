namespace OtterLogic.StructuralDesign;

/// <summary>
/// The six degrees of freedom as six named inputs — one value per element in
/// each — checked and read into one array.
/// <para>
/// Six named inputs rather than one row of six per element, because a row can
/// be the wrong length or the wrong order and still look right: a row of five
/// puts every value after the gap under the wrong name, and nothing downstream
/// can tell. A list named Fz can only ever be Fz. What is left to go wrong is a
/// list that is too long or too short, and that is caught here, where every
/// caller — a Grasshopper component, a Rhino command, a test — gets the same
/// answer.
/// </para>
/// <para>
/// One value per element, and nothing about which one. An envelope over load
/// combinations, a sign dropped for a force designed either way, a governing
/// combination picked out — each is a judgement about what the grouping is for,
/// and the user makes it upstream, in their own definition, where it can be seen.
/// </para>
/// </summary>
internal static class SixDof
{
    /// <summary>The degrees of freedom, in the order they are passed and read.</summary>
    internal static readonly string[] Names = { "Fx", "Fy", "Fz", "Mx", "My", "Mz" };

    /// <summary>
    /// Reads the six inputs into an n x 6 array, one row per element, or says what
    /// is wrong with them.
    /// </summary>
    internal static double[,] Read(
        IReadOnlyList<double> fx, IReadOnlyList<double> fy, IReadOnlyList<double> fz,
        IReadOnlyList<double> mx, IReadOnlyList<double> my, IReadOnlyList<double> mz)
    {
        var inputs = new[] { fx, fy, fz, mx, my, mz };

        for (int j = 0; j < inputs.Length; j++)
            if (inputs[j] is null)
                throw new ArgumentNullException(Names[j].ToLowerInvariant(), $"{Names[j]} is missing.");

        // Refused rather than trimmed to the shortest: every list is read by
        // position, so one value short means every element after the gap is
        // grouped on a neighbour's force.
        int n = inputs[0].Count;
        for (int j = 1; j < inputs.Length; j++)
            if (inputs[j].Count != n)
                throw new ArgumentException(
                    "Every degree of freedom needs one value per element, in the same order, but "
                    + $"{Names[0]} has {n} and {Names[j]} has {inputs[j].Count}.");

        var demands = new double[n, inputs.Length];
        for (int j = 0; j < inputs.Length; j++)
        {
            for (int i = 0; i < n; i++)
            {
                double value = inputs[j][i];
                if (!double.IsFinite(value))
                    throw new ArgumentException(
                        $"{Names[j]} of element {i} is {value}. Grouping needs finite values.");

                demands[i, j] = value;
            }
        }

        return demands;
    }
}
