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
    {
        var rng = new Random(Seed);
        var rows = new List<double[]>();

        foreach (var centre in centres)
        {
            for (int i = 0; i < perFamily; i++)
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
