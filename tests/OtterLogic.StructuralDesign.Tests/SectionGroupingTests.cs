using Xunit;

namespace OtterLogic.StructuralDesign.Tests;

/// <summary>
/// Section grouping knows no member types, so it is tested on things true of any
/// steel frame by construction: what stands and what lies never share a section, a
/// beam is sized for its bay and not for the line it was drawn in, a column for its
/// storey and not its stack, bays of two lengths are two sections, and none of it
/// depends on which way the frame faces.
/// </summary>
public class SectionGroupingTests
{
    [Fact]
    public void NoGroupMixesStandingAndLyingLines()
    {
        var model = new Models().Frame(3, 3, 2);
        var result = model.Group();

        Assert.True(result.GroupCount >= 2);
        foreach (var lines in result.Lines())
            Assert.Single(lines.Select(e => model.Vertical[e]).Distinct());
    }

    [Fact]
    public void EveryLineIsPlacedOnce()
    {
        var result = new Models().Frame(3, 2, 2).Group();

        Assert.Equal(Enumerable.Range(0, result.LineCount), result.Lines().SelectMany(g => g).OrderBy(e => e));
        Assert.All(result.Groups, g => Assert.All(g.Lines, e => Assert.Equal(g.Index, result.Group[e])));
        Assert.All(result.Confidence, c => Assert.InRange(c, 0.0, 1.0));
    }

    [Fact]
    public void AColumnIsSizedForItsStorey_AndABeamForItsBay()
    {
        var model = new Models().Frame(2, 2, 3, bay: 6.0, storey: 4.0);
        var result = model.Group();

        foreach (var group in result.Groups)
        {
            bool standing = model.Vertical[group.Lines[0]];
            Assert.Equal(standing ? 4.0 : 6.0, group.DesignLength, 6);
            Assert.Equal(standing ? 12.0 : 6.0, group.LongestSpan, 6);
        }
    }

    [Fact]
    public void BaysOfTwoLengths_AreTwoSections()
    {
        var model = new Models().Frame(new[] { 6.0, 9.0, 6.0 }, new[] { 6.0, 6.0 }, 2);
        var result = model.Group();

        double Length(int e) => Math.Round(result.DesignLength[result.Pieces.Of[e]], 6);
        foreach (var lines in result.Lines())
        {
            var beams = lines.Where(e => !model.Vertical[e]).ToArray();
            Assert.True(beams.Select(Length).Distinct().Count() <= 1,
                $"a group holds beams of {string.Join(" and ", beams.Select(Length).Distinct())} m");
        }
    }

    /// <summary>
    /// One brace in a frame of beams and columns: as long as the beams beside it and
    /// carrying about as much, but carrying it along its axis.
    /// </summary>
    [Fact]
    public void ABrace_DoesNotShareASectionWithTheBeamsBesideIt()
    {
        var model = new Models().Frame(new[] { 6.0, 9.0, 6.0 }, new[] { 6.0, 6.0 }, 3);
        model.Line(3, 0, 4, 3, 6, 4);
        model.Line(3, 6, 4, 3, 12, 4);
        int brace = model.Line(9, 0, 0, 15, 0, 4);

        var result = model.Group();

        Assert.Equal(new[] { brace }, result.Groups[result.Group[brace]].Lines);
    }

    [Fact]
    public void AskingForACount_GivesThatMany()
    {
        var model = new Models().Frame(new[] { 6.0, 9.0, 6.0 }, new[] { 6.0, 6.0 }, 2);
        int families = model.Group().Families;

        Assert.Equal(families + 2, model.Group(new SectionGroupingOptions { Groups = families + 2 }).GroupCount);
        Assert.Equal(1, model.Group(new SectionGroupingOptions { Groups = 1 }).GroupCount);
    }

    [Fact]
    public void AskingForMoreSectionsThanPieces_FailsWithSomethingReadable()
    {
        var model = new Models().Frame(1, 1, 1);
        var error = Assert.Throws<ArgumentException>(() => model.Group(new SectionGroupingOptions { Groups = 500 }));

        Assert.Contains("cannot be put into 500 sections", error.Message);
    }

    /// <summary>
    /// Gravity is the only direction the grouping knows. Turned about the vertical, the
    /// same frame falls into the same groups.
    /// </summary>
    [Fact]
    public void TurningTheFrameAboutTheVertical_ChangesNothing()
    {
        Models Turned(double degrees)
        {
            var flat = new Models().Frame(new[] { 6.0, 9.0, 6.0 }, new[] { 6.0, 6.0 }, 2);
            flat.Line(3, 0, 4, 3, 6, 4);
            flat.Line(3, 6, 4, 3, 12, 4);
            return flat.TurnedAboutVertical(degrees);
        }

        var square = Turned(0.0).Group();
        var turned = Turned(37.0).Group();

        Assert.Equal(square.GroupCount, turned.GroupCount);
        for (int a = 0; a < square.LineCount; a++)
            for (int b = a + 1; b < square.LineCount; b++)
                Assert.Equal(square.Group[a] == square.Group[b], turned.Group[a] == turned.Group[b]);
    }

    [Fact]
    public void SameInputTwice_GivesTheSameAnswer()
    {
        var model = new Models().Frame(3, 2, 2);

        Assert.Equal(model.Group().Group, model.Group().Group);
    }

    [Fact]
    public void WithoutSupports_RunsAreNotCut_AndTheReportSaysSo()
    {
        var result = new Models().Frame(2, 2, 1).GroupWithoutSupports();

        Assert.False(result.Pieces.Traced);
        Assert.Equal(result.Reading.MemberCount, result.Pieces.Count);
        Assert.Contains("not traced", result.Report());
        Assert.Contains(result.Notes, note => note.Contains("No supports given"));
    }

    [Fact]
    public void TheReportStatesItsAssumptions()
    {
        string report = new Models().Frame(2, 1, 1).Group().Report();

        Assert.Contains("steel frame", report);
        Assert.Contains("Simple connections", report);
    }

    [Fact]
    public void TooFewLines_FailWithSomethingReadable()
    {
        var error = Assert.Throws<ArgumentException>(() => SectionGrouping.Group(new double[2, 3], new double[2, 3]));
        Assert.Contains("at least three lines", error.Message);
    }

    [Fact]
    public void MismatchedStartsAndEnds_FailWithSomethingReadable()
    {
        var error = Assert.Throws<ArgumentException>(() => SectionGrouping.Group(new double[4, 3], new double[3, 3]));
        Assert.Contains("same order", error.Message);
    }
}
