using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// How each line stands — level, pitched or plumb — read from the model's own
/// spread of inclinations rather than from cut-off angles.
/// <para>
/// Shared by every tool here that reads a line model, so a line is called the same
/// thing by grid inference and by connection typology. The inclinations are banded
/// by <see cref="ValueBands"/>, and each band named by its nearest prototype in
/// <see cref="GridNaming"/>: a roof whose beams all sit at a few degrees reads as
/// level, and a leaning column as plumb.
/// </para>
/// </summary>
internal static class LineOrientations
{
    /// <summary>Orientation per line; lines with no length at the tolerance are degenerate.</summary>
    public static LineOrientation[] Classify(
        Vec[] starts, Vec[] ends, double tolerance, ValueBandsOptions banding, List<string> notes)
    {
        int n = starts.Length;
        var orientation = new LineOrientation[n];
        var measured = new List<int>();
        var sines = new List<double>();

        for (int i = 0; i < n; i++)
        {
            var d = ends[i] - starts[i];
            if (d.Length <= tolerance)
            {
                orientation[i] = LineOrientation.Degenerate;
                continue;
            }

            measured.Add(i);
            sines.Add(Math.Abs(d.Z) / d.Length);
        }

        int degenerate = n - measured.Count;
        if (degenerate > 0)
            notes.Add($"{degenerate} line(s) have no length at this tolerance and were left out.");

        if (sines.Count == 0)
            return orientation;

        var bands = ValueBands.Fit(sines, banding with { Resolution = 0.0 });
        var names = bands.Bands.Select(band => GridNaming.Nearest(band.Median)).ToArray();

        for (int k = 0; k < measured.Count; k++)
            orientation[measured[k]] = names[bands.Band[k]];

        return orientation;
    }
}
