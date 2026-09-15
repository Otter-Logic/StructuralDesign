using Xunit;

namespace OtterLogic.StructuralDesign.Tests;

/// <summary>
/// The engine knows no structural type, so it is tested across several — a frame, a
/// shell with edge beams, a dome — on things true by construction: elements that
/// stand and elements that lie are not the same kind of thing, a line and a panel
/// are not, and a model's problems are where they were put.
/// </summary>
public class StructuralInsightEngineTests
{
    private const InsightFlag GeometryProblems = InsightFlag.Duplicate | InsightFlag.Degenerate | InsightFlag.Isolated
        | InsightFlag.Disconnected | InsightFlag.NoPathToSupport | InsightFlag.FreeEnd | InsightFlag.WeakConnection
        | InsightFlag.NearMiss;

    [Fact]
    public void Frame_NoGroupMixesStandingAndLyingElements()
    {
        var model = new Models().Frame(3, 3, 2);
        var result = model.Analyse();

        Assert.True(result.Groups >= 2);
        foreach (var members in result.Members())
        {
            double standing = members.Count(e => model.Vertical[e]) / (double)members.Length;
            Assert.True(standing <= 0.1 || standing >= 0.9, $"a group of {members.Length} is {standing:P0} standing elements");
        }
    }

    [Fact]
    public void CleanFrame_HasNoGeometryProblems()
    {
        var result = new Models().Frame(3, 3, 2).Analyse();

        Assert.All(result.Flags, flags => Assert.Equal(InsightFlag.None, flags & GeometryProblems));
        Assert.Equal(1, result.ComponentCount);
        Assert.All(result.Labels, label => Assert.True(label >= 0));
    }

    [Fact]
    public void ProblemsAreFlaggedWhereTheyWerePut()
    {
        var model = new Models().Frame(3, 3, 2);
        int copied = 40;
        int duplicate = model.Line(0, 0, 4, 6, 0, 4);
        int stray = model.Line(100, 100, 100, 106, 100, 100);
        int missed = model.Line(0.005, 0, 8, 0, 6.0, 8);

        Assert.False(model.Vertical[copied]);
        var result = model.Analyse();

        Assert.True(result.Flags[duplicate].HasFlag(InsightFlag.Duplicate));
        Assert.True(result.Flags[stray].HasFlag(InsightFlag.Isolated));
        Assert.True(result.Flags[stray].HasFlag(InsightFlag.NoPathToSupport));
        Assert.True(result.Flags[stray].HasFlag(InsightFlag.FreeEnd));
        Assert.True(result.Flags[missed].HasFlag(InsightFlag.NearMiss));
        Assert.Contains(result.Issues, issue => issue.Element == stray && issue.Flag == InsightFlag.FreeEnd && issue.X == 100);
    }

    /// <summary>
    /// Two frames joined by one beam: that beam alone holds half the model on, and
    /// nothing else in either frame does.
    /// </summary>
    [Fact]
    public void OneElementHoldingTwoPartsTogether_IsAWeakConnection()
    {
        var model = new Models().Frame(2, 2, 1).Frame(2, 2, 1, x0: 18.0);
        int link = model.Line(12, 0, 4, 18, 0, 4);

        var result = model.Analyse();

        Assert.True(result.Flags[link].HasFlag(InsightFlag.WeakConnection));
        Assert.Single(result.Flags.Where(f => f.HasFlag(InsightFlag.WeakConnection)));
    }

    /// <summary>A beam framing into the middle of another with no node drawn there is connected, not loose.</summary>
    [Fact]
    public void AnEndRestingAlongAnElement_IsConnectedToIt()
    {
        var model = new Models().Frame(1, 1, 1);
        int secondary = model.Line(3, 0, 4, 3, 6, 4);

        var result = model.Analyse();

        Assert.Equal(InsightFlag.None, result.Flags[secondary] & GeometryProblems);
        Assert.Equal(2, (int)result.Features[secondary, 7]);
    }

    [Fact]
    public void Shell_LinesAndPanelsFallIntoDifferentGroups()
    {
        var model = new Models();
        const int panels = 8;
        const double size = 1.25, z = 4.0;

        for (int i = 0; i < panels; i++)
            for (int j = 0; j < panels; j++)
                model.Surface((i * size, j * size, z), ((i + 1) * size, j * size, z), ((i + 1) * size, (j + 1) * size, z), (i * size, (j + 1) * size, z));

        for (int k = 0; k < panels; k++)
        {
            model.Line(k * size, 0, z, (k + 1) * size, 0, z);
            model.Line(k * size, panels * size, z, (k + 1) * size, panels * size, z);
            model.Line(0, k * size, z, 0, (k + 1) * size, z);
            model.Line(panels * size, k * size, z, panels * size, (k + 1) * size, z);
        }

        foreach (var (x, y) in new[] { (0.0, 0.0), (10.0, 0.0), (0.0, 10.0), (10.0, 10.0) })
        {
            model.Line(x, y, 0, x, y, z);
            model.Support(x, y, 0);
        }

        var result = model.Analyse();

        Assert.Equal(model.LineCount, result.LineCount);
        Assert.Equal(panels * panels, result.SurfaceCount);
        foreach (var members in result.Members())
        {
            double surfaces = members.Count(e => e >= result.LineCount) / (double)members.Length;
            Assert.True(surfaces <= 0.1 || surfaces >= 0.9, $"a group of {members.Length} is {surfaces:P0} surfaces");
        }
    }

    [Fact]
    public void Dome_IsReadWithoutBeingToldWhatItIs()
    {
        var model = new Models();
        const int meridians = 16, rings = 5;
        const double radius = 10.0;

        (double, double, double) At(int ring, int meridian)
        {
            double polar = Math.PI / 2 * (1.0 - (double)ring / rings);
            double azimuth = 2 * Math.PI * meridian / meridians;
            return (radius * Math.Cos(polar) * Math.Cos(azimuth), radius * Math.Cos(polar) * Math.Sin(azimuth), radius * Math.Sin(polar));
        }

        for (int m = 0; m < meridians; m++)
        {
            var (bx, by, bz) = At(0, m);
            model.Support(bx, by, bz);
            for (int r = 0; r < rings - 1; r++)
            {
                var (x1, y1, z1) = At(r, m);
                var (x2, y2, z2) = At(r + 1, m);
                model.Line(x1, y1, z1, x2, y2, z2);

                var (x3, y3, z3) = At(r + 1, (m + 1) % meridians);
                model.Line(x2, y2, z2, x3, y3, z3);
            }
        }

        var result = model.Analyse();

        Assert.True(result.Groups >= 2);
        Assert.Equal(result.ElementCount, result.Features.GetLength(0));
        Assert.Equal(InsightFlag.None, result.Flags.Aggregate(InsightFlag.None, (all, f) => all | f)
            & (InsightFlag.Isolated | InsightFlag.Disconnected | InsightFlag.NoPathToSupport));
    }

    [Fact]
    public void WithoutSupports_TheSupportChecksAreSkippedAndSaidSo()
    {
        var result = new Models().Frame(2, 2, 1).AnalyseWithoutSupports();

        Assert.All(result.SupportDistance, d => Assert.True(double.IsNaN(d)));
        Assert.DoesNotContain(result.Issues, issue => issue.Flag == InsightFlag.NoPathToSupport);
        Assert.Contains("none given", result.Report());
    }

    [Fact]
    public void SameInputTwice_GivesTheSameAnswer()
    {
        var model = new Models().Frame(3, 2, 2);

        var first = model.Analyse();
        var second = model.Analyse();

        Assert.Equal(first.Labels, second.Labels);
        Assert.Equal(first.Flags, second.Flags);
    }

    [Fact]
    public void ASurfaceWithTwoCorners_FailsWithSomethingReadable()
    {
        var error = Assert.Throws<ArgumentException>(() => StructuralInsightEngine.Analyse(
            null, null, new[] { new double[,] { { 0, 0, 0 }, { 1, 0, 0 } } }));

        Assert.Contains("at least three corners", error.Message);
    }

    [Fact]
    public void MismatchedStartsAndEnds_FailWithSomethingReadable()
    {
        var error = Assert.Throws<ArgumentException>(() => StructuralInsightEngine.Analyse(new double[4, 3], new double[3, 3]));
        Assert.Contains("same order", error.Message);
    }
}
