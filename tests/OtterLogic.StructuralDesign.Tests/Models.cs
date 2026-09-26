namespace OtterLogic.StructuralDesign.Tests;

/// <summary>
/// Structural models as plain arrays, built so what the engine should find is known
/// by construction — the same arrays a Grasshopper component would hand over.
/// </summary>
internal sealed class Models
{
    private readonly List<double[]> _starts = new();
    private readonly List<double[]> _ends = new();
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

    public void Support(double x, double y, double z) => _supports.Add(new[] { x, y, z });

    /// <summary>A rectilinear frame: columns at every grid point, beams both ways at every floor, pinned at the base.</summary>
    public Models Frame(int baysX, int baysY, int storeys, double bay = 6.0, double storey = 4.0, double x0 = 0.0)
        => Frame(Enumerable.Repeat(bay, baysX).ToArray(), Enumerable.Repeat(bay, baysY).ToArray(), storeys, storey, x0);

    /// <summary>
    /// A rectilinear frame with bays of the widths given each way: columns at every grid
    /// point, beams both ways at every floor, pinned at the base.
    /// </summary>
    public Models Frame(double[] baysX, double[] baysY, int storeys, double storey = 4.0, double x0 = 0.0)
    {
        var xs = Grid(baysX, x0);
        var ys = Grid(baysY, 0.0);

        foreach (double x in xs)
            foreach (double y in ys)
            {
                Support(x, y, 0.0);
                for (int s = 0; s < storeys; s++)
                    Line(x, y, s * storey, x, y, (s + 1) * storey);
            }

        for (int s = 1; s <= storeys; s++)
        {
            double z = s * storey;
            foreach (double y in ys)
                for (int i = 0; i + 1 < xs.Length; i++)
                    Line(xs[i], y, z, xs[i + 1], y, z);

            foreach (double x in xs)
                for (int j = 0; j + 1 < ys.Length; j++)
                    Line(x, ys[j], z, x, ys[j + 1], z);
        }

        return this;
    }

    private static double[] Grid(double[] bays, double origin)
    {
        var at = new double[bays.Length + 1];
        at[0] = origin;
        for (int i = 0; i < bays.Length; i++)
            at[i + 1] = at[i] + bays[i];
        return at;
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

    public SectionGroupingResult Group(SectionGroupingOptions? options = null)
        => SectionGrouping.Group(Rows(_starts), Rows(_ends), _supports.Count > 0 ? Rows(_supports) : null, options);

    public SectionGroupingResult GroupWithoutSupports(SectionGroupingOptions? options = null)
        => SectionGrouping.Group(Rows(_starts), Rows(_ends), null, options);

    private static double[,] Rows(List<double[]> points)
    {
        var rows = new double[points.Count, 3];
        for (int i = 0; i < points.Count; i++)
            for (int c = 0; c < 3; c++)
                rows[i, c] = points[i][c];
        return rows;
    }
}
