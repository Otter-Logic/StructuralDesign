using Xunit;

namespace OtterLogic.StructuralDesign.Tests;

public class GeometryQATests
{
    /// <summary>Lines, surfaces and supports as the arrays a component hands over, scaled and turned on the way in.</summary>
    private sealed class Model
    {
        private readonly double _unit;
        private readonly double _cos, _sin;
        public readonly List<double[]> Starts = new(), Ends = new(), Supports = new();
        public readonly List<double[,]> Surfaces = new();

        public Model(double unit = 1.0, double degrees = 0.0)
        {
            _unit = unit;
            _cos = Math.Cos(degrees * Math.PI / 180);
            _sin = Math.Sin(degrees * Math.PI / 180);
        }

        private double[] P(double x, double y, double z) => new[] { (_cos * x - _sin * y) * _unit, (_sin * x + _cos * y) * _unit, z * _unit };

        public int Line(double x1, double y1, double z1, double x2, double y2, double z2)
        {
            Starts.Add(P(x1, y1, z1));
            Ends.Add(P(x2, y2, z2));
            return Starts.Count - 1;
        }

        public int Surface(params (double X, double Y, double Z)[] corners)
        {
            var boundary = new double[corners.Length, 3];
            for (int v = 0; v < corners.Length; v++)
            {
                var p = P(corners[v].X, corners[v].Y, corners[v].Z);
                (boundary[v, 0], boundary[v, 1], boundary[v, 2]) = (p[0], p[1], p[2]);
            }
            Surfaces.Add(boundary);
            return Starts.Count + Surfaces.Count - 1;
        }

        public void Support(double x, double y, double z) => Supports.Add(P(x, y, z));

        public GeometryQAResult Check() => GeometryQA.Check(Rows(Starts), Rows(Ends), Surfaces,
            Supports.Count > 0 ? Rows(Supports) : null, new GeometryQAOptions { Tolerance = 0.001 * _unit });

        private static double[,] Rows(List<double[]> points)
        {
            var rows = new double[points.Count, 3];
            for (int i = 0; i < points.Count; i++)
                for (int a = 0; a < 3; a++)
                    rows[i, a] = points[i][a];
            return rows;
        }
    }

    private static readonly double[] GridX = { 0, 8000, 16000, 24000 };
    private static readonly double[] GridY = { 0, 6000, 12000 };

    /// <summary>A clean two-storey frame: columns split at floors, beams both ways, a slab panel per bay.</summary>
    private static Model Clean(double unit = 1.0, double degrees = 0.0)
    {
        var model = new Model(unit, degrees);
        foreach (double x in GridX)
            foreach (double y in GridY)
            {
                model.Support(x, y, 0);
                model.Line(x, y, 0, x, y, 4000);
                model.Line(x, y, 4000, x, y, 8000);
            }

        foreach (double z in new[] { 4000.0, 8000.0 })
        {
            foreach (double y in GridY)
                for (int i = 0; i < 3; i++)
                    model.Line(GridX[i], y, z, GridX[i + 1], y, z);
            foreach (double x in GridX)
                for (int j = 0; j < 2; j++)
                    model.Line(x, GridY[j], z, x, GridY[j + 1], z);
        }

        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 2; j++)
                model.Surface((GridX[i], GridY[j], 4000), (GridX[i + 1], GridY[j], 4000),
                    (GridX[i + 1], GridY[j + 1], 4000), (GridX[i], GridY[j + 1], 4000));

        return model;
    }

    private static GeometryIssue[] Of(GeometryQAResult result, GeometryIssueKind kind) => result.Issues.Where(i => i.Kind == kind).ToArray();

    /// <summary>
    /// The check that matters most: a model with nothing wrong reports nothing — in
    /// millimetres, in metres, and turned. A health check that cries wolf is ignored.
    /// </summary>
    [Theory]
    [InlineData(1.0, 0.0)]
    [InlineData(0.001, 0.0)]
    [InlineData(1.0, 23.0)]
    public void ACleanModelReportsNothing(double unit, double degrees)
    {
        var result = Clean(unit, degrees).Check();

        Assert.Empty(result.Issues);
        Assert.Equal(1, result.PartCount);
    }

    [Fact]
    public void AColumnStartingTwelveMillimetresAboveTheFloorIsANearMiss()
    {
        var model = Clean();
        int column = model.Starts.FindIndex(p => p[0] == 24000 && p[1] == 12000 && p[2] == 4000);
        model.Starts[column] = new[] { 24000.0, 12000.0, 4012.0 };

        var miss = Assert.Single(Of(model.Check(), GeometryIssueKind.NearMiss));
        Assert.Equal(12.0, miss.Measure, 6);
        Assert.Contains(column, miss.Elements);
    }

    /// <summary>A beam 25 mm high touches nothing: a separate part, and a near miss between parts.</summary>
    [Fact]
    public void ABeamOffItsLevelIsFoundThreeWays()
    {
        var model = Clean();

        // Replace the first-floor beam on line C, bay 1-2, with one 25 mm higher.
        int beam = Enumerable.Range(0, model.Starts.Count).Single(i =>
            model.Starts[i].SequenceEqual(new[] { 0.0, 12000, 4000 }) && model.Ends[i].SequenceEqual(new[] { 8000.0, 12000, 4000 }));
        model.Starts[beam] = new[] { 0.0, 12000, 4025 };
        model.Ends[beam] = new[] { 8000.0, 12000, 4025 };

        var result = model.Check();

        Assert.Contains(Of(result, GeometryIssueKind.NearMiss), i => Math.Abs(i.Measure - 25) < 1e-6);
        Assert.Contains(Of(result, GeometryIssueKind.SeparatePart), i => i.Elements.Contains(beam));
        Assert.Contains(Of(result, GeometryIssueKind.OffLevel), i => i.Elements.Contains(beam));
    }

    [Fact]
    public void AnEndOnAColumnFaceIsAnUnnodedBearing()
    {
        var model = Clean();
        int mezzanine = model.Line(0, 0, 1800, 8000, 0, 1800);

        var bearings = Of(model.Check(), GeometryIssueKind.UnnodedBearing);

        Assert.Equal(2, bearings.Count(i => i.Elements.Contains(mezzanine)));
    }

    /// <summary>
    /// A panel corner landing mid-beam is a non-conforming mesh: the solver does not
    /// connect the panel to the beam there.
    /// </summary>
    [Fact]
    public void APanelCornerMidBeamIsAnUnnodedBearing()
    {
        var model = Clean();
        int split = model.Surface((8000, 6000, 8000), (12000, 6000, 8000), (12000, 12000, 8000), (8000, 12000, 8000));

        Assert.Contains(Of(model.Check(), GeometryIssueKind.UnnodedBearing), i => i.Elements.Contains(split));
    }

    [Fact]
    public void CrossingsSayWhetherTheyAreAPatternOrTheOnlyOne()
    {
        var model = Clean();
        model.Line(0, 0, 0, 8000, 0, 4000);
        model.Line(8000, 0, 0, 0, 0, 4000);
        model.Line(16000, 0, 0, 24000, 0, 4000);
        model.Line(24000, 0, 0, 16000, 0, 4000);
        model.Line(8000, 3000, 8000, 16000, 3000, 8000);
        model.Line(12000, 0, 8000, 12000, 6000, 8000);

        var crossings = Of(model.Check(), GeometryIssueKind.UnjoinedCrossing);

        Assert.Equal(3, crossings.Length);
        Assert.Single(crossings, i => i.Message.Contains("the only unjoined Level–Level crossing"));
        Assert.Equal(2, crossings.Count(i => i.Message.Contains("one of 2 unjoined Pitched–Pitched")));
    }

    /// <summary>
    /// A braced ring canopy held off the roof by two cantilevers: a closed sub-structure
    /// held by fewer elements than meet at its own nodes. The spectral sweep finds it.
    /// </summary>
    [Fact]
    public void ACanopyOnTwoCantileversIsWeaklyAttached()
    {
        var model = Clean();
        var c = new[] { (27000.0, 1000.0), (30000.0, 1000.0), (30000.0, 5000.0), (27000.0, 5000.0) };
        for (int k = 0; k < 4; k++)
            model.Line(c[k].Item1, c[k].Item2, 8000, c[(k + 1) % 4].Item1, c[(k + 1) % 4].Item2, 8000);
        model.Line(27000, 1000, 8000, 30000, 5000, 8000);
        int a = model.Line(24000, 0, 8000, 27000, 1000, 8000);
        int b = model.Line(24000, 6000, 8000, 27000, 5000, 8000);

        var weak = Assert.Single(Of(model.Check(), GeometryIssueKind.WeaklyAttached));

        Assert.Equal(2.0, weak.Measure);
        Assert.Contains(a, weak.Elements);
        Assert.Contains(b, weak.Elements);
    }

    [Fact]
    public void AFloatingFrameIsASeparateUnsupportedPart()
    {
        var model = Clean();
        var c = new[] { (-3000.0, 0.0), (-1000.0, 0.0), (-1000.0, 2000.0), (-3000.0, 2000.0) };
        for (int k = 0; k < 4; k++)
            model.Line(c[k].Item1, c[k].Item2, 3000, c[(k + 1) % 4].Item1, c[(k + 1) % 4].Item2, 3000);

        var result = model.Check();
        var part = Assert.Single(Of(result, GeometryIssueKind.SeparatePart));

        Assert.Equal(4, part.Elements.Length);
        Assert.Contains("no support", part.Message);
        Assert.Equal(2, result.PartCount);
    }

    [Fact]
    public void DuplicatesOverlapsAndZeroLengthsAreReported()
    {
        var model = Clean();
        model.Line(0, 0, 8000, 8000, 0, 8000);
        model.Line(8000, 12000, 8000, 20000, 12000, 8000);
        model.Line(16000, 12000, 4000, 16000, 12000, 4000);

        var result = model.Check();

        Assert.Single(Of(result, GeometryIssueKind.Duplicate));
        Assert.Equal(2, Of(result, GeometryIssueKind.Overlap).Length);
        Assert.Single(Of(result, GeometryIssueKind.ZeroLength));
    }

    [Fact]
    public void ASliverIsShortEvenInAGridOfIdenticalMembers()
    {
        var model = new Model();
        foreach (double x in new[] { 0.0, 6000, 12000 })
            foreach (double y in new[] { 0.0, 6000 })
            {
                model.Line(x, y, 0, x, y, 6000);
                if (x < 12000) model.Line(x, y, 6000, x + 6000, y, 6000);
                if (y < 6000) model.Line(x, y, 6000, x, y + 6000, 6000);
            }
        int sliver = model.Line(6000, 6000, 6000, 6000, 6000, 6003);

        var shortest = Assert.Single(Of(model.Check(), GeometryIssueKind.ShortElement));
        Assert.Equal(new[] { sliver }, shortest.Elements);
    }

    [Fact]
    public void SupportsAtNoNodeAndAnUnsupportedStructureAreReported()
    {
        var stranded = Clean();
        stranded.Support(30000, 30000, 0);
        Assert.Single(Of(stranded.Check(), GeometryIssueKind.StrandedSupport));

        var floating = Clean();
        floating.Supports.Clear();
        floating.Support(-50000, 0, 0);
        Assert.Single(Of(floating.Check(), GeometryIssueKind.Unsupported));
    }

    /// <summary>The same faults in metres are the same faults.</summary>
    [Fact]
    public void FindingsDoNotDependOnUnits()
    {
        GeometryQAResult Faulty(double unit)
        {
            var model = Clean(unit);
            int column = model.Starts.FindIndex(p => Math.Abs(p[0] - 24000 * unit) < 1e-9 && Math.Abs(p[1] - 12000 * unit) < 1e-9 && Math.Abs(p[2] - 4000 * unit) < 1e-9);
            model.Starts[column] = new[] { 24000 * unit, 12000 * unit, 4012 * unit };
            model.Line(0, 0, 1800, 8000, 0, 1800);
            model.Line(8000, 6000, 8000, 8000, 6000, 8003);
            return model.Check();
        }

        var millimetres = Faulty(1.0).Issues.Select(i => i.Kind).ToArray();
        var metres = Faulty(0.001).Issues.Select(i => i.Kind).ToArray();

        Assert.NotEmpty(millimetres);
        Assert.Equal(millimetres, metres);
    }

    /// <summary>
    /// Grouped as a model check lists them: one group per kind, most serious first,
    /// each element once per group however many findings of that kind it is in.
    /// </summary>
    [Fact]
    public void IssuesGroupByKindWithEachElementOnce()
    {
        var model = Clean();
        int mezzanine = model.Line(0, 0, 1800, 8000, 0, 1800);
        model.Line(0, 0, 8000, 8000, 0, 8000);

        var groups = model.Check().Groups();

        Assert.Equal(new[] { GeometryIssueKind.SeparatePart, GeometryIssueKind.UnnodedBearing, GeometryIssueKind.Duplicate },
            groups.Select(g => g.Kind));

        var bearings = groups.Single(g => g.Kind == GeometryIssueKind.UnnodedBearing);
        Assert.Equal(2, bearings.Count);
        Assert.Equal(1, bearings.Elements.Count(e => e == mezzanine));
        Assert.Equal("Ends bearing with no node", bearings.Name);
    }

    /// <summary>Each element says which kinds of issue it is in; a clean element says nothing.</summary>
    [Fact]
    public void EachElementIsLabelledWithItsIssues()
    {
        var model = Clean();
        int mezzanine = model.Line(0, 0, 1800, 8000, 0, 1800);

        var labels = model.Check().IssueLabels();

        Assert.Equal("separate part; end bearing with no node", labels[mezzanine]);
        Assert.Equal(string.Empty, labels[model.Starts.FindIndex(p => p[0] == 16000 && p[1] == 12000 && p[2] == 4000)]);
    }

    [Fact]
    public void TheSummaryGroupsByKind()
    {
        var model = Clean();
        model.Line(0, 0, 1800, 8000, 0, 1800);

        var summary = model.Check().Summary();

        Assert.Contains("Ends bearing with no node (2)", summary);
        Assert.Contains("Separate parts (1)", summary);
    }
}
