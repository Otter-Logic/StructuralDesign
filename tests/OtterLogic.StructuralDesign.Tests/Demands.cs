namespace OtterLogic.StructuralDesign.Tests;

/// <summary>
/// Six-degree-of-freedom demand built around known family centres, so the right
/// grouping is known by construction.
/// </summary>
internal static class Demands
{
    private const int Seed = 20;

    /// <summary>
    /// Builds demand rows around the given family centres, with an optional
    /// share of members scattered far from all of them.
    /// </summary>
    internal static double[,] Families(
        double spread, double outlierFraction, double[][] centres, int perFamily = 45)
        => Families(spread, outlierFraction, centres, Enumerable.Repeat(perFamily, centres.Length).ToArray());

    /// <summary>As above, with a different member count per family.</summary>
    internal static double[,] Families(
        double spread, double outlierFraction, double[][] centres, int[] perFamily)
    {
        var rng = new Random(Seed);
        var rows = new List<double[]>();

        for (int f = 0; f < centres.Length; f++)
        {
            var centre = centres[f];
            for (int i = 0; i < perFamily[f]; i++)
            {
                var row = new double[centre.Length];
                for (int j = 0; j < centre.Length; j++)
                {
                    // A zero column is a degree of freedom the structure has
                    // none of, and stays exactly zero.
                    row[j] = centre[j] == 0.0 ? 0.0 : Math.Max(0.0, centre[j] + Gauss(rng) * spread);
                }

                rows.Add(row);
            }
        }

        int outliers = (int)Math.Round(rows.Count * outlierFraction / (1.0 - outlierFraction));
        for (int i = 0; i < outliers; i++)
        {
            var row = new double[centres[0].Length];
            for (int j = 0; j < row.Length; j++)
                row[j] = centres[0][j] == 0.0 ? 0.0 : rng.NextDouble() * 600.0;

            rows.Add(row);
        }

        var data = new double[rows.Count, centres[0].Length];
        for (int i = 0; i < rows.Count; i++)
            for (int j = 0; j < data.GetLength(1); j++)
                data[i, j] = rows[i][j];

        return data;
    }

    /// <summary>
    /// Reactions for families of foundations under one load combination, as
    /// <c>forces[dof][element]</c>. Each foundation's reactions scatter by a share
    /// of their own size, the way real ones do, and keep their sign.
    /// </summary>
    internal static double[][] Reactions((double[] Centre, int Count)[] families, double scatter, int seed = Seed)
    {
        var rng = new Random(seed);
        int n = families.Sum(family => family.Count);

        var forces = new double[6][];
        for (int j = 0; j < 6; j++)
            forces[j] = new double[n];

        int i = 0;
        foreach (var (centre, count) in families)
            for (int k = 0; k < count; k++, i++)
                for (int j = 0; j < 6; j++)
                    forces[j][i] = centre[j] * (1.0 + scatter * Gauss(rng));

        return forces;
    }

    /// <summary>
    /// Reactions for families of foundations under several load combinations, as
    /// <c>forces[dof][element][combination]</c> — element-major, the way a
    /// Grasshopper tree with a branch per node holds them. Combination c is each
    /// foundation's gravity reactions plus <c>factors[c]</c> times its wind
    /// reactions, so factors of 0, 1 and −1 are gravity alone and wind either way.
    /// Each foundation's reactions scatter by a share of their own size and keep
    /// their sign.
    /// </summary>
    internal static double[][][] Combinations(
        (double[] Gravity, double[] Wind, int Count)[] families, double[] factors, double scatter, int seed = Seed)
    {
        var rng = new Random(seed);
        int n = families.Sum(family => family.Count);

        var forces = new double[6][][];
        for (int j = 0; j < 6; j++)
        {
            forces[j] = new double[n][];
            for (int i = 0; i < n; i++)
                forces[j][i] = new double[factors.Length];
        }

        int element = 0;
        foreach (var (gravity, wind, count) in families)
        {
            for (int k = 0; k < count; k++, element++)
            {
                for (int j = 0; j < 6; j++)
                {
                    double g = gravity[j] * (1.0 + scatter * Gauss(rng));
                    double w = wind[j] * (1.0 + scatter * Gauss(rng));
                    for (int c = 0; c < factors.Length; c++)
                        forces[j][element][c] = g + factors[c] * w;
                }
            }
        }

        return forces;
    }

    /// <summary>Which family each element of <see cref="Combinations"/> was built from.</summary>
    internal static int[] Truth((double[] Gravity, double[] Wind, int Count)[] families)
        => families.SelectMany((family, f) => Enumerable.Repeat(f, family.Count)).ToArray();

    /// <summary>Which family each element of <see cref="Reactions"/> was built from.</summary>
    internal static int[] Truth((double[] Centre, int Count)[] families)
        => families.SelectMany((family, f) => Enumerable.Repeat(f, family.Count)).ToArray();

    /// <summary>The rows split back into one list per degree of freedom.</summary>
    internal static double[][] Columns(double[,] data)
    {
        var columns = new double[data.GetLength(1)][];
        for (int j = 0; j < columns.Length; j++)
        {
            columns[j] = new double[data.GetLength(0)];
            for (int i = 0; i < columns[j].Length; i++)
                columns[j][i] = data[i, j];
        }

        return columns;
    }

    private static double Gauss(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
