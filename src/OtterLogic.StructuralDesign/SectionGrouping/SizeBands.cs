namespace OtterLogic.StructuralDesign;

/// <summary>
/// Splits each role family into the sections its pieces need, by what sizes a steel
/// section before any analysis: how long the piece is, how much weight it carries,
/// and whether it carries it along its axis or across it.
/// <para>
/// Divisive and one-dimensional at every step, so every split can be said in a
/// sentence: the group whose spread is widest for its limit is cut at the widest gap
/// in that measure. Left to itself it stops when every group is within all three limits —
/// the fewest sections the limits allow. Given a count, it keeps cutting the widest
/// until there are that many, or until every group is uniform. Both measures are
/// compared as ratios, on logs, so the same limits mean the same thing in millimetres
/// and metres and on a shed and a tower.
/// </para>
/// </summary>
internal static class SizeBands
{
    /// <summary>
    /// Added to every flow before it is compared, so a piece carrying almost nothing
    /// does not read as infinitely lighter than one carrying a little. The same floor
    /// the role features log flow with.
    /// </summary>
    private const double FlowFloor = 1e-4;

    /// <summary>
    /// The most a group's pieces may differ in how upright they stand before the group
    /// is split, left to itself. Upright is the share of a vertical load a
    /// piece takes along its axis rather than in bending, and a quarter of it is a 30
    /// degree slope: a brace and the beam beside it may come out one role family when a
    /// frame has a single brace, and may be the same length and carry the same weight,
    /// but one is sized for buckling and the other for bending, and no section suits both.
    /// A pitched rafter of up to 30 degrees still shares with the level beams.
    /// </summary>
    private const double UprightSpread = 0.25;

    /// <summary>One section group: the family it came from and the pieces in it.</summary>
    internal sealed record Band(int Family, int[] Pieces);

    /// <summary>Splits the families.</summary>
    /// <param name="family">Per piece, its role family.</param>
    /// <param name="families">How many families there are.</param>
    /// <param name="length">Per piece, its design length.</param>
    /// <param name="flow">Per piece, its flow; ignored unless <paramref name="useFlow"/>.</param>
    /// <param name="upright">Per piece, how far it stands up rather than lies, 0 to 1.</param>
    /// <param name="useFlow">Whether a load path was traced, so flow means something.</param>
    /// <param name="lengthLimit">Most a group's longest may be over its shortest, left to itself.</param>
    /// <param name="flowLimit">Most a group's heaviest may be over its lightest, left to itself.</param>
    /// <param name="target">A fixed number of groups, or null to stop within the limits.</param>
    public static List<Band> Split(
        int[] family, int families, double[] length, double[] flow, double[] upright, bool useFlow,
        double lengthLimit, double flowLimit, int? target)
    {
        double longest = length.DefaultIfEmpty(1.0).Max();
        double floor = Math.Max(longest, 1.0) * 1e-9;
        var logLength = length.Select(l => Math.Log(Math.Max(l, floor))).ToArray();
        var logFlow = flow.Select(f => Math.Log(Math.Max(f, 0.0) + FlowFloor)).ToArray();

        var bands = Enumerable.Range(0, families)
            .Select(f => new Band(f, Enumerable.Range(0, family.Length).Where(p => family[p] == f).ToArray()))
            .Where(b => b.Pieces.Length > 0)
            .ToList();

        (double Excess, double[] Values) Measure(Band band)
        {
            double Spread(double[] values) => band.Pieces.Max(p => values[p]) - band.Pieces.Min(p => values[p]);

            double byLength = Spread(logLength) / Math.Log(lengthLimit);
            double byFlow = useFlow ? Spread(logFlow) / Math.Log(flowLimit) : 0.0;
            double byStanding = Spread(upright) / UprightSpread;

            if (byStanding >= byLength && byStanding >= byFlow)
                return (byStanding, upright);
            return byFlow > byLength ? (byFlow, logFlow) : (byLength, logLength);
        }

        while (target is null || bands.Count < target)
        {
            int widest = -1;
            double most = target is null ? 1.0 + 1e-9 : 1e-9;
            for (int b = 0; b < bands.Count; b++)
            {
                double excess = Measure(bands[b]).Excess;
                if (excess > most)
                    (widest, most) = (b, excess);
            }

            if (widest < 0)
                break;

            var (lower, upper) = Halve(bands[widest].Pieces, Measure(bands[widest]).Values);
            bands[widest] = bands[widest] with { Pieces = lower };
            bands.Insert(widest + 1, new Band(bands[widest].Family, upper));
        }

        return bands;
    }

    /// <summary>
    /// Cuts at the widest gap between neighbouring values. Among gaps equally wide —
    /// a ramp of lengths, evenly spaced — the one nearest the middle of the range, so
    /// a ramp is halved rather than trimmed.
    /// </summary>
    private static (int[] Lower, int[] Upper) Halve(int[] pieces, double[] values)
    {
        var sorted = pieces.OrderBy(p => values[p]).ThenBy(p => p).ToArray();
        double middle = 0.5 * (values[sorted[0]] + values[sorted[^1]]);

        int at = 1;
        double widest = -1.0, nearest = double.PositiveInfinity;
        for (int i = 1; i < sorted.Length; i++)
        {
            double gap = values[sorted[i]] - values[sorted[i - 1]];
            double offset = Math.Abs(0.5 * (values[sorted[i]] + values[sorted[i - 1]]) - middle);
            bool wider = gap > widest * (1.0 + 1e-9) + 1e-12;
            bool asWide = Math.Abs(gap - widest) <= 1e-9 * Math.Max(1.0, Math.Abs(widest)) && offset < nearest;
            if (wider || asWide)
                (at, widest, nearest) = (i, gap, offset);
        }

        return (sorted[..at].OrderBy(p => p).ToArray(), sorted[at..].OrderBy(p => p).ToArray());
    }
}
