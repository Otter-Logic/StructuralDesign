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
/// Two shapes are read. One value per element is one load combination. One list
/// per element, holding a value per combination, is several — element-major,
/// because that is how a Grasshopper tree with a branch per node already holds
/// them, and it keeps each element's combinations together where its envelope is
/// taken.
/// </para>
/// </summary>
internal static class SixDof
{
    /// <summary>The degrees of freedom, in the order they are passed and read.</summary>
    internal static readonly string[] Names = { "Fx", "Fy", "Fz", "Mx", "My", "Mz" };

    /// <summary>Position of the axial force, the one degree of freedom whose sign a foundation cares about.</summary>
    internal const int Fz = 2;

    /// <summary>
    /// Reads the six inputs into <c>forces[dof][element]</c>, or says what is
    /// wrong with them.
    /// </summary>
    internal static double[][] Read(
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

        var forces = new double[inputs.Length][];
        for (int j = 0; j < inputs.Length; j++)
        {
            forces[j] = new double[n];
            for (int i = 0; i < n; i++)
            {
                double value = inputs[j][i];
                if (!double.IsFinite(value))
                    throw new ArgumentException(
                        $"{Names[j]} of element {i} is {value}. Grouping needs finite forces.");

                forces[j][i] = value;
            }
        }

        return forces;
    }

    /// <summary>
    /// Reads the six inputs, each one list per element holding a value per load
    /// combination, into <c>forces[dof][element][combination]</c>, or says what is
    /// wrong with them.
    /// <para>
    /// Every element needs the same number of combinations in every force, and
    /// that is refused rather than enveloped over whatever is there: an element
    /// with three Fz values and two Fx values would have its shear enveloped over
    /// fewer combinations than its axial force, from values that never acted
    /// together — or, if one list is short by a combination at the end, from the
    /// wrong ones.
    /// </para>
    /// </summary>
    internal static double[][][] ReadCombinations(
        IReadOnlyList<IReadOnlyList<double>> fx, IReadOnlyList<IReadOnlyList<double>> fy,
        IReadOnlyList<IReadOnlyList<double>> fz, IReadOnlyList<IReadOnlyList<double>> mx,
        IReadOnlyList<IReadOnlyList<double>> my, IReadOnlyList<IReadOnlyList<double>> mz)
    {
        var inputs = new[] { fx, fy, fz, mx, my, mz };

        for (int j = 0; j < inputs.Length; j++)
            if (inputs[j] is null || inputs[j].Any(element => element is null))
                throw new ArgumentNullException(Names[j].ToLowerInvariant(), $"{Names[j]} is missing.");

        int n = inputs[0].Count;
        for (int j = 1; j < inputs.Length; j++)
            if (inputs[j].Count != n)
                throw new ArgumentException(
                    "Every degree of freedom needs one list per element, in the same order, but "
                    + $"{Names[0]} has {n} and {Names[j]} has {inputs[j].Count}.");

        if (n == 0)
            throw new ArgumentException("Need at least one element.");

        int combinations = inputs[0][0].Count;
        if (combinations == 0)
            throw new ArgumentException($"{Names[0]} of element 0 has no values; need at least one load combination.");

        for (int j = 0; j < inputs.Length; j++)
            for (int i = 0; i < n; i++)
                if (inputs[j][i].Count != combinations)
                    throw new ArgumentException(
                        "Every element needs the same load combinations in every degree of freedom, but "
                        + $"{Names[0]} of element 0 has {combinations} and {Names[j]} of element {i} has "
                        + $"{inputs[j][i].Count}.");

        var forces = new double[inputs.Length][][];
        for (int j = 0; j < inputs.Length; j++)
        {
            forces[j] = new double[n][];
            for (int i = 0; i < n; i++)
            {
                forces[j][i] = new double[combinations];
                for (int c = 0; c < combinations; c++)
                {
                    double value = inputs[j][i][c];
                    if (!double.IsFinite(value))
                        throw new ArgumentException(
                            $"{Names[j]} of element {i} in combination {c} is {value}. Grouping needs finite forces.");

                    forces[j][i][c] = value;
                }
            }
        }

        return forces;
    }

    /// <summary>One value per element, read as a single combination: <c>forces[dof][element][0]</c>.</summary>
    internal static double[][][] AsOneCombination(double[][] forces)
        => forces.Select(force => force.Select(value => new[] { value }).ToArray()).ToArray();

    /// <summary>
    /// Each element's largest size under any combination — the envelope of a
    /// force designed to act either way, where only how big it gets matters.
    /// </summary>
    internal static double[] LargestSize(double[][] force)
        => force.Select(values => values.Max(Math.Abs)).ToArray();

    /// <summary>Each element's largest value under any combination, with its sign.</summary>
    internal static double[] Largest(double[][] force) => force.Select(values => values.Max()).ToArray();

    /// <summary>Each element's smallest value under any combination, with its sign.</summary>
    internal static double[] Smallest(double[][] force) => force.Select(values => values.Min()).ToArray();

    /// <summary>Every force as it came, with its sign: six columns, one per degree of freedom.</summary>
    internal static (double[,] Features, string[] Names) Signed(double[][] forces)
        => Features(
            forces.Select((force, j) => (Names[j], (Func<int, double>)(i => force[i]))).ToList(),
            forces[0].Length);

    /// <summary>Evaluates each named column for every element into an n x d array.</summary>
    internal static (double[,] Features, string[] Names) Features(
        IReadOnlyList<(string Name, Func<int, double> Value)> columns, int n)
    {
        var features = new double[n, columns.Count];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < columns.Count; j++)
                features[i, j] = columns[j].Value(i);

        return (features, columns.Select(column => column.Name).ToArray());
    }
}
