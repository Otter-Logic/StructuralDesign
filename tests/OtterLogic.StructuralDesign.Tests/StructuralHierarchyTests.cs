using Xunit;

namespace OtterLogic.StructuralDesign.Tests;

/// <summary>
/// What the engine reads before it clusters anything — members, assemblies, load
/// paths — tested on things true of any structure: a run drawn in pieces is one
/// member, what rests on something sits a level above it, lines triangulated together
/// are one body, and none of it depends on which way the model faces.
/// </summary>
public class StructuralHierarchyTests
{
    [Fact]
    public void ARunDrawnInPieces_IsOneMember()
    {
        var model = new Models().Frame(3, 3, 2);
        var result = model.Analyse();

        // Sixteen column stacks, and four beam lines each way on each of two floors.
        Assert.Equal(16 + 2 * (4 + 4), result.MemberCount);

        // The first column was drawn as two storeys, one after the other.
        Assert.Equal(result.Member[0], result.Member[1]);
        Assert.NotEqual(result.Member[0], result.Member[2]);
    }

    [Fact]
    public void ElementsOfOneMember_ShareEverythingReadOfTheMember()
    {
        var result = new Models().Frame(3, 3, 2).Analyse();

        foreach (var elements in Enumerable.Range(0, result.ElementCount).GroupBy(e => result.Member[e]))
        {
            Assert.Single(elements.Select(e => result.Labels[e]).Distinct());
            Assert.Single(elements.Select(e => result.Level[e]).Distinct());
            Assert.Single(elements.Select(e => result.Assembly[e]).Distinct());
        }
    }

    [Fact]
    public void WhatRestsOnSomething_SitsALevelAboveIt()
    {
        // Two beams resting on the frame's beams, and one spanning between the middles
        // of those two. It crosses the frame beam between them without touching it.
        var model = new Models().Frame(2, 2, 1);
        int resting = model.Line(3, 0, 4, 3, 6, 4);
        int restingToo = model.Line(9, 0, 4, 9, 6, 4);
        int restingOnThose = model.Line(3, 3, 4, 9, 3, 4);

        var result = model.Analyse();

        for (int e = 0; e < resting; e++)
            Assert.Equal(model.Vertical[e] ? 0 : 1, result.Level[e]);

        Assert.Equal(2, result.Level[resting]);
        Assert.Equal(2, result.Level[restingToo]);
        Assert.Equal(3, result.Level[restingOnThose]);
        Assert.Equal(3, result.Levels);
    }

    [Fact]
    public void WhatCarriesMore_HasMoreFlow()
    {
        var model = new Models().Frame(2, 2, 2);
        var result = model.Analyse();

        double standing = Enumerable.Range(0, model.LineCount).Where(e => model.Vertical[e]).Average(e => result.Flow[e]);
        double lying = Enumerable.Range(0, model.LineCount).Where(e => !model.Vertical[e]).Average(e => result.Flow[e]);

        Assert.True(standing > 5 * lying, $"columns carry {standing:G3} of the model each, beams {lying:G3}");
        Assert.All(result.Flow, flow => Assert.InRange(flow, 0.0, 1.0));
    }

    /// <summary>
    /// A triangulated body on two posts with something resting on it: the body is one
    /// assembly on one level, however its own lines hand load between themselves.
    /// </summary>
    [Fact]
    public void LinesTriangulatedTogether_AreOneBodyOnOneLevel()
    {
        var model = new Models();
        const int panels = 4;
        const double panel = 2.0, low = 3.0, high = 4.5;

        int firstPost = model.Line(0, 0, 0, 0, 0, low);
        model.Line(panels * panel, 0, 0, panels * panel, 0, low);
        model.Support(0, 0, 0);
        model.Support(panels * panel, 0, 0);

        int firstOfBody = model.LineCount;
        for (int p = 0; p < panels; p++)
        {
            model.Line(p * panel, 0, low, (p + 1) * panel, 0, low);
            model.Line(p * panel, 0, high, (p + 1) * panel, 0, high);
            model.Line(p * panel, 0, low, (p + 1) * panel, 0, high);
        }

        for (int p = 1; p < panels; p++)
            model.Line(p * panel, 0, low, p * panel, 0, high);
        int afterBody = model.LineCount;

        // The end uprights carry straight on from the posts, so they are the posts.
        int endUpright = model.Line(0, 0, low, 0, 0, high);
        model.Line(panels * panel, 0, low, panels * panel, 0, high);

        // Something resting on the middle of the body, held at its far end by a post of its own.
        int resting = model.Line(2 * panel, 0, high, 2 * panel, 5, high);
        model.Line(2 * panel, 5, 0, 2 * panel, 5, high);
        model.Support(2 * panel, 5, 0);

        var result = model.Analyse();
        var body = Enumerable.Range(firstOfBody, afterBody - firstOfBody).ToArray();

        Assert.Single(body.Select(e => result.Assembly[e]).Distinct());
        Assert.Equal(result.Member[firstPost], result.Member[endUpright]);
        Assert.NotEqual(result.Assembly[firstPost], result.Assembly[body[0]]);
        Assert.Equal(0, result.Level[firstPost]);
        Assert.All(body, e => Assert.Equal(1, result.Level[e]));
        Assert.Equal(2, result.Level[resting]);
    }

    /// <summary>
    /// Gravity is the only direction the engine knows. Turned about the vertical, the
    /// same model falls into the same groups on the same levels.
    /// </summary>
    [Fact]
    public void TurningTheModelAboutTheVertical_ChangesNothing()
    {
        var square = Turned(0.0).Analyse();
        var turned = Turned(37.0).Analyse();

        Assert.Equal(square.Level, turned.Level);
        Assert.Equal(square.Member, turned.Member);
        Assert.Equal(square.Groups, turned.Groups);

        for (int a = 0; a < square.ElementCount; a++)
            for (int b = a + 1; b < square.ElementCount; b++)
                Assert.Equal(square.Labels[a] == square.Labels[b], turned.Labels[a] == turned.Labels[b]);
    }

    [Fact]
    public void WithoutSupports_NoLoadPathIsTraced()
    {
        var result = new Models().Frame(2, 2, 1).AnalyseWithoutSupports();

        Assert.Equal(-1, result.Levels);
        Assert.All(result.Level, level => Assert.Equal(-1, level));
        Assert.All(result.Flow, flow => Assert.Equal(0.0, flow));
        Assert.Contains("not traced", result.Report());
    }

    [Fact]
    public void TheHierarchy_HoldsEveryElementOnce()
    {
        var result = new Models().Frame(3, 2, 2).Analyse();
        var hierarchy = result.Hierarchy();

        Assert.Equal(Enumerable.Range(0, result.ElementCount), hierarchy.SelectMany(h => h.Elements).OrderBy(e => e));
        Assert.All(hierarchy, h => Assert.All(h.Elements, e =>
        {
            Assert.Equal(h.Level, result.Level[e]);
            Assert.Equal(h.Group, result.Labels[e]);
        }));
    }

    [Fact]
    public void ElementsAsMembers_ChainsNothing()
    {
        var result = new Models().Frame(2, 2, 2).Analyse(new StructuralInsightOptions { ElementsAsMembers = true });

        Assert.Equal(result.ElementCount, result.MemberCount);
        Assert.True(double.IsNaN(result.TurnLimit));
    }

    /// <summary>A frame with beams resting on its beams, turned about the vertical through the origin.</summary>
    private static Models Turned(double degrees)
    {
        var flat = new Models().Frame(3, 2, 2);
        flat.Line(3, 0, 4, 3, 6, 4);
        flat.Line(9, 0, 4, 9, 6, 4);
        flat.Line(3, 6, 8, 3, 12, 8);
        return flat.TurnedAboutVertical(degrees);
    }
}
