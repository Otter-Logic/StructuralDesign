using Xunit;
using OtterLogic.StructuralEngine;

namespace OtterLogic.StructuralDesign.Tests;

public class GridLevelInferenceTests
{
    /// <summary>Lines as the two arrays a component hands over.</summary>
    private sealed class Lines
    {
        private readonly List<double[]> _starts = new();
        private readonly List<double[]> _ends = new();

        public int Add(double x1, double y1, double z1, double x2, double y2, double z2)
        {
            _starts.Add(new[] { x1, y1, z1 });
            _ends.Add(new[] { x2, y2, z2 });
            return _starts.Count - 1;
        }

        public GridLevelResult Infer(GridLevelOptions? options = null)
            => GridLevelInference.Infer(Rows(_starts), Rows(_ends), options);

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
    private static readonly double[] Floors = { 0, 4000, 8000, 12300 };

    /// <summary>
    /// The worked example: a 4 x 3 grid at 8 m by 6 m, three storeys and a roof at
    /// 12.3 m, columns split at every floor, beams both ways on every floor. Plus a
    /// four-beam mezzanine at 1.8 m, the ground-storey column at X 16000, Y 6000
    /// built at X 16012 with the first-floor beams framing into it there, and one
    /// first-floor beam modelled 25 mm high.
    /// </summary>
    private static (Lines Lines, int OffColumn, int HighBeam) Example(Func<double, double, (double X, double Y)>? place = null)
    {
        place ??= (x, y) => (x, y);
        var lines = new Lines();
        int offColumn = -1, highBeam = -1;

        (double X, double Y) At(double x, double y, int floor)
            => floor == 1 && x == 16000 && y == 6000 ? place(16012, 6000) : place(x, y);

        for (int s = 0; s < 3; s++)
            foreach (double x in GridX)
                foreach (double y in GridY)
                {
                    var bottom = s == 0 && x == 16000 && y == 6000 ? place(16012, 6000) : place(x, y);
                    var top = At(x, y, s + 1);
                    int line = lines.Add(bottom.X, bottom.Y, Floors[s], top.X, top.Y, Floors[s + 1]);
                    if (s == 0 && x == 16000 && y == 6000)
                        offColumn = line;
                }

        for (int f = 1; f < 4; f++)
        {
            foreach (double y in GridY)
                for (int i = 0; i < GridX.Length - 1; i++)
                {
                    var a = At(GridX[i], y, f);
                    var b = At(GridX[i + 1], y, f);
                    bool high = f == 1 && y == 12000 && i == 0;
                    double z = high ? 4025 : Floors[f];
                    int line = lines.Add(a.X, a.Y, z, b.X, b.Y, z);
                    if (high)
                        highBeam = line;
                }

            foreach (double x in GridX)
                for (int j = 0; j < GridY.Length - 1; j++)
                {
                    var a = At(x, GridY[j], f);
                    var b = At(x, GridY[j + 1], f);
                    lines.Add(a.X, a.Y, Floors[f], b.X, b.Y, Floors[f]);
                }
        }

        // The mezzanine: the four edges of bay 1-2 / A-B at 1.8 m.
        var c = new[] { place(0, 0), place(8000, 0), place(8000, 6000), place(0, 6000) };
        for (int k = 0; k < 4; k++)
            lines.Add(c[k].X, c[k].Y, 1800, c[(k + 1) % 4].X, c[(k + 1) % 4].Y, 1800);

        return (lines, offColumn, highBeam);
    }

    [Fact]
    public void FindsTheLevelsIncludingTheMezzanine()
    {
        var result = Example().Lines.Infer();

        Assert.Equal(new[] { 0.0, 1800.0, 4000.0, 8000.0, 12300.0 }, result.Levels.Select(l => l.Elevation));
        Assert.Equal(new[] { "Level 00", "Level 01", "Level 02", "Level 03", "Level 04" }, result.Levels.Select(l => l.Name));
    }

    [Fact]
    public void FindsTheGridAndNamesIt()
    {
        var result = Example().Lines.Infer();

        Assert.Equal(2, result.Families.Count);
        Assert.Equal(new[] { "1", "2", "3", "4" }, result.Families[0].Gridlines.Select(g => result.Gridlines[g].Name));
        Assert.Equal(new[] { "A", "B", "C" }, result.Families[1].Gridlines.Select(g => result.Gridlines[g].Name));
        Assert.Equal(new[] { 0.0, 8000.0, 16000.0, 24000.0 },
            result.Families[0].Gridlines.Select(g => Math.Round(result.Gridlines[g].Offset)));
    }

    [Fact]
    public void LabelsEveryColumnWithItsGridPosition()
    {
        var result = Example().Lines.Infer();

        // Columns were added x-major, y-minor: the first is at X 0, Y 0; the fifth at X 8000, Y 6000.
        Assert.Equal("A/1", result.GridLabel[0]);
        Assert.Equal("B/2", result.GridLabel[4]);
    }

    [Fact]
    public void FlagsTheColumnOffItsGridline()
    {
        var (lines, offColumn, _) = Example();
        var result = lines.Infer();

        var issue = Assert.Single(result.Issues, i => i.Element == offColumn);
        Assert.Equal("3", issue.Reference);
        Assert.Equal(12.0, Math.Abs(issue.Deviation), 6);
        Assert.Equal("B/3", result.GridLabel[offColumn]);
    }

    [Fact]
    public void FlagsTheBeamOffItsLevel()
    {
        var (lines, _, highBeam) = Example();
        var result = lines.Infer();

        var issue = Assert.Single(result.Issues, i => i.Element == highBeam);
        Assert.Equal("Level 02", issue.Reference);
        Assert.Equal(25.0, issue.Deviation, 6);
        Assert.Contains("above Level 02", issue.Message);
    }

    /// <summary>
    /// Joists at 1 m centres between the gridlines, framing into beams rather than
    /// columns, change nothing: they are not primary framing, so they neither make
    /// gridlines of their own nor fill the gaps between the real ones.
    /// </summary>
    [Fact]
    public void JoistsDoNotMakeGridlines()
    {
        var (lines, _, _) = Example();
        for (double x = 1000; x < 24000; x += 1000)
            if (x % 8000 != 0)
                for (int j = 0; j < 2; j++)
                    lines.Add(x, GridY[j], 8000, x, GridY[j + 1], 8000);

        var result = lines.Infer();

        Assert.Equal(new[] { "1", "2", "3", "4" }, result.Families[0].Gridlines.Select(g => result.Gridlines[g].Name));
    }

    /// <summary>The whole building turned 30 degrees: the grid turns with it and is found the same.</summary>
    [Fact]
    public void AGridAtAnAngleIsFoundTheSame()
    {
        double c = Math.Cos(Math.PI / 6), s = Math.Sin(Math.PI / 6);
        var result = Example((x, y) => (c * x - s * y, s * x + c * y)).Lines.Infer();

        Assert.Equal(2, result.Families.Count);
        Assert.Equal(4, result.Families[0].Gridlines.Length);
        Assert.Equal(3, result.Families[1].Gridlines.Length);
        Assert.Contains(result.Families, f => Math.Abs(f.Direction - 30.0) < 1e-6);
        Assert.Equal(5, result.Levels.Count);
    }

    [Fact]
    public void BracesAreLeftOutButLandOnTheirLevels()
    {
        var (lines, _, _) = Example();
        int brace = lines.Add(0, 0, 0, 8000, 0, 4000);

        var result = lines.Infer();

        Assert.Equal(LineOrientation.Pitched, result.Orientation[brace]);
        Assert.Equal(0, result.StartLevel[brace]);
        Assert.Equal(2, result.EndLevel[brace]);
        Assert.Equal(5, result.Levels.Count);
    }

    [Fact]
    public void WithoutColumnsThereIsNoGridAndItSaysSo()
    {
        var lines = new Lines();
        for (int i = 0; i < 20; i++)
            lines.Add(0, i * 1000, 3000, 6000, i * 1000, 3000);

        var result = lines.Infer();

        Assert.Empty(result.Gridlines);
        Assert.Single(result.Levels);
        Assert.Contains(result.Notes, note => note.Contains("No plumb lines"));
    }

    [Fact]
    public void LettersSkipIAndO()
    {
        var letters = GridNaming.Letters(26);

        Assert.DoesNotContain("I", letters);
        Assert.DoesNotContain("O", letters);
        Assert.Equal("Z", letters[23]);
        Assert.Equal("AA", letters[24]);
    }

    [Fact]
    public void ALeaningColumnIsStillPlumb()
    {
        Assert.Equal(LineOrientation.Plumb, LineOrientations.Nearest(Math.Sin(80 * Math.PI / 180)));
        Assert.Equal(LineOrientation.Level, LineOrientations.Nearest(Math.Sin(5 * Math.PI / 180)));
        Assert.Equal(LineOrientation.Pitched, LineOrientations.Nearest(Math.Sin(35 * Math.PI / 180)));
    }
}
