namespace OtterLogic.StructuralDesign.Tests;

/// <summary>
/// Structural models as plain arrays, built so what the engine should find is known
/// by construction — the same arrays a Grasshopper component would hand over.
/// </summary>
internal sealed class Models
{
    private readonly List<double[]> _starts = new();
    private readonly List<double[]> _ends = new();
    private readonly List<double[,]> _surfaces = new();
    private readonly List<double[]> _supports = new();

    /// <summary>Whether each line stands up rather than lies level, in the order added.</summary>
    public List<bool> Vertical { get; } = new();

    public int LineCount => _starts.Count;

    public int Line(double x1, double y1, double z1, double x2, double y2, double z2)
    {
        _starts.Add(new[] { x1, y1, z1 });
        _ends.Add(new[] { x2, y2, z2 });
        Vertical.Add(Math.Abs(z2 - z1) > Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1)));
        return _starts.Count - 1;
    }

    public void Surface(params (double X, double Y, double Z)[] corners)
    {
        var boundary = new double[corners.Length, 3];
        for (int i = 0; i < corners.Length; i++)
            (boundary[i, 0], boundary[i, 1], boundary[i, 2]) = corners[i];
        _surfaces.Add(boundary);
    }

    public void Support(double x, double y, double z) => _supports.Add(new[] { x, y, z });

    /// <summary>A rectilinear frame: columns at every grid point, beams both ways at every floor, pinned at the base.</summary>
    public Models Frame(int baysX, int baysY, int storeys, double bay = 6.0, double storey = 4.0, double x0 = 0.0)
    {
        for (int i = 0; i <= baysX; i++)
            for (int j = 0; j <= baysY; j++)
            {
                Support(x0 + i * bay, j * bay, 0.0);
                for (int s = 0; s < storeys; s++)
                    Line(x0 + i * bay, j * bay, s * storey, x0 + i * bay, j * bay, (s + 1) * storey);
            }

        for (int s = 1; s <= storeys; s++)
        {
            double z = s * storey;
            for (int j = 0; j <= baysY; j++)
                for (int i = 0; i < baysX; i++)
                    Line(x0 + i * bay, j * bay, z, x0 + (i + 1) * bay, j * bay, z);

            for (int i = 0; i <= baysX; i++)
                for (int j = 0; j < baysY; j++)
                    Line(x0 + i * bay, j * bay, z, x0 + i * bay, (j + 1) * bay, z);
        }

        return this;
    }

    /// <summary>The same model turned about the vertical through the origin — lines and supports, in the same order.</summary>
    public Models TurnedAboutVertical(double degrees)
    {
        double angle = degrees * Math.PI / 180.0;
        double[] Turn(double[] p) => new[]
        {
            p[0] * Math.Cos(angle) - p[1] * Math.Sin(angle),
            p[0] * Math.Sin(angle) + p[1] * Math.Cos(angle),
            p[2],
        };

        var turned = new Models();
        for (int i = 0; i < _starts.Count; i++)
        {
            var (a, b) = (Turn(_starts[i]), Turn(_ends[i]));
            turned.Line(a[0], a[1], a[2], b[0], b[1], b[2]);
        }

        foreach (var support in _supports)
        {
            var p = Turn(support);
            turned.Support(p[0], p[1], p[2]);
        }

        return turned;
    }

    public StructuralInsightResult Analyse(StructuralInsightOptions? options = null)
        => StructuralInsightEngine.Analyse(Rows(_starts), Rows(_ends), _surfaces, _supports.Count > 0 ? Rows(_supports) : null, options);

    public StructuralInsightResult AnalyseWithoutSupports()
        => StructuralInsightEngine.Analyse(Rows(_starts), Rows(_ends), _surfaces, null);

    private static double[,] Rows(List<double[]> points)
    {
        var rows = new double[points.Count, 3];
        for (int i = 0; i < points.Count; i++)
            for (int c = 0; c < 3; c++)
                rows[i, c] = points[i][c];
        return rows;
    }
}
