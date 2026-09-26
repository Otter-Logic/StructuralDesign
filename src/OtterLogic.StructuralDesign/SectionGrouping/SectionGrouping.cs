using OtterLogic.StructuralEngine;
using OtterLogic.Unsupervised.Clustering;

namespace OtterLogic.StructuralDesign;

/// <summary>
/// Groups a steel frame's members into the sections they can share, from its
/// geometry alone, before any analysis.
/// <para>
/// Lines and supports in; a section group for every line out. It was the Structural
/// Insight Engine until 2026-09-26, and the reading and the role clustering are that
/// engine's. What changed is the question. "What natural groups are there?" gathered
/// every reading as an output — levels, a graph, features, a list of QA flags — and
/// overlapped Geometry QA and Describe Member. "Which members can share a section?"
/// has one answer, and everything else lives with the tool that owns it.
/// </para>
/// <list type="number">
/// <item><b>Read</b> — <see cref="ModelReading"/>: points welded into joints, lines that
/// carry straight on chained into runs, triangulated runs into bodies, and every
/// line's weight drained to the supports, giving each its flow and level.</item>
/// <item><b>Cut into pieces</b> — <see cref="Pieces"/>: each run cut where it rests on
/// something part of the way along, so a girder line over five columns is five beams
/// and a column stack stays one. A piece's span, unbraced length and flow are
/// measured.</item>
/// <item><b>Role</b> — the runs sorted into role families by
/// <see cref="MultiViewClustering"/>: what each is like and what it is attached to,
/// never where it is. The connectivity view is off by default, because a section group
/// is scattered by nature.</item>
/// <item><b>Size</b> — each family split by <see cref="SizeBands"/> on design length and
/// flow into the fewest groups within the length and flow ratios, or into a fixed
/// number of sections.</item>
/// </list>
/// <para>
/// It knows no member types: nothing here says column, beam or brace. It does take
/// the model to be a steel frame, and says so in <see cref="Assumptions"/>.
/// </para>
/// </summary>
public static class SectionGrouping
{
    /// <summary>What the grouping takes as given about the model, in words for a user.</summary>
    public static readonly IReadOnlyList<string> Assumptions = new[]
    {
        "A steel frame drawn as centrelines: lines only. Slabs, walls and cores are not read; where a beam sits on a wall, give its bearing points as supports.",
        "Simple connections: a beam is a piece from bearing to bearing, cut at every column or girder it rests on part of the way along.",
        "A column stack that carries straight on is one piece. Splices are not inferred, so a section is not stepped down a tall building; after analysis, the 6DOF Behaviour Classifier groups by force.",
        "Gravity is down the negative Z axis.",
        "Flow is the share of the frame's own weight passing along a piece: a proxy for demand, not a force. The answer is groups, not sizes.",
        "The model is noded where members meet. Run Geometry QA first; points within ten times the tolerance are joined here as meant to meet.",
    };

    /// <summary>Groups a frame's lines into sections.</summary>
    /// <param name="lineStarts">n x 3, the start of each line.</param>
    /// <param name="lineEnds">n x 3, the end of each line, in the same order.</param>
    /// <param name="supports">m x 3 support points. Without them no load path is traced: runs are not cut and flow is not compared.</param>
    /// <param name="options">Settings; null for the defaults.</param>
    public static SectionGroupingResult Group(
        double[,]? lineStarts,
        double[,]? lineEnds,
        double[,]? supports = null,
        SectionGroupingOptions? options = null)
    {
        options ??= new SectionGroupingOptions();
        options.Validate();

        var (starts, ends) = ModelInput.CheckLines(lineStarts, lineEnds);
        int lines = starts.GetLength(0);
        if (lines < 3)
            throw new ArgumentException($"Need at least three lines to group into sections; got {lines}.", nameof(lineStarts));

        // Read. Fewer than three runs is too few to find families among; then every
        // line stands for itself, as it would have anyway.
        var notes = new List<string>();
        var reading = ModelReading.Read(starts, ends, null, supports, options.ToReading(options.ChainMembers));
        if (reading.MemberCount < 3)
        {
            reading = ModelReading.Read(starts, ends, null, supports, options.ToReading(chain: false));
            if (options.ChainMembers)
                notes.Add("The lines chain into fewer than three runs, so every line was read as a run of its own.");
        }

        var members = reading.Members;
        var pieces = reading.Pieces;

        if (options.Groups is { } asked && asked > pieces.Count)
            throw new ArgumentException(
                $"The frame reads as {pieces.Count} pieces, so it cannot be put into {asked} sections. Ask for {pieces.Count} or fewer.",
                nameof(options));

        // Role.
        var (affinity, role, density) = RoleFeatures.Views(reading);
        var clustering = MultiViewClustering.Fit(members.Graph, affinity, role, density, options.ToClustering(families: null));
        if (options.Groups is { } few && few < clustering.Groups)
        {
            notes.Add($"The runs fall into {clustering.Groups} role families; {few} section(s) were asked for, so the runs were put "
                + $"into {few} families and none was split by size.");
            clustering = MultiViewClustering.Fit(members.Graph, affinity, role, density, options.ToClustering(families: few));
        }

        var runFamily = clustering.Labels;
        int families = clustering.Groups;

        // Size.
        var designLength = Enumerable.Range(0, pieces.Count)
            .Select(p => pieces.Upright[p] * pieces.Unbraced[p] + (1.0 - pieces.Upright[p]) * pieces.Span[p])
            .ToArray();
        var pieceFamily = pieces.Member.Select(m => runFamily[m]).ToArray();

        // Fewer sections than families were asked for, so the families were refitted to
        // that count above and nothing is split by size.
        var bands = SizeBands.Split(pieceFamily, families, designLength, pieces.Flow, pieces.Upright, pieces.Traced,
            options.MaximumLengthRatio, options.MaximumFlowRatio, options.Groups);

        if (options.Groups is { } wanted && bands.Count < wanted)
            notes.Add($"{wanted} sections were asked for, but the pieces are alike enough to make only {bands.Count} distinct groups.");

        // Families in the consensus's order, largest first; within a family, longest first.
        var ordered = bands
            .OrderBy(b => b.Family)
            .ThenByDescending(b => b.Pieces.Max(p => designLength[p]))
            .ThenBy(b => b.Pieces.Min())
            .ToList();

        var pieceGroup = new int[pieces.Count];
        for (int g = 0; g < ordered.Count; g++)
            foreach (int p in ordered[g].Pieces)
                pieceGroup[p] = g;

        var group = pieces.Of.Select(p => pieceGroup[p]).ToArray();
        var confidence = members.Of.Select(m => clustering.Agreement[m]).ToArray();
        var level = pieces.Member.Select(m => reading.Paths.Level[reading.Assemblies.Of[m]]).ToArray();

        var groups = ordered.Select((band, g) =>
        {
            var own = band.Pieces;
            var lineIndices = Enumerable.Range(0, lines).Where(e => group[e] == g).ToArray();
            var placed = own.Select(p => level[p]).Where(l => l >= 0).ToArray();
            return new SectionGroup(
                g,
                band.Family,
                own,
                lineIndices,
                own.Max(p => designLength[p]),
                own.Min(p => designLength[p]),
                own.Max(p => pieces.Span[p]),
                own.Min(p => pieces.Span[p]),
                own.Max(p => pieces.Flow[p]),
                own.Min(p => pieces.Flow[p]),
                own.Average(p => pieces.Upright[p]),
                placed.Length > 0 ? placed.Min() : -1,
                placed.Length > 0 ? placed.Max() : -1,
                lineIndices.Length > 0 ? lineIndices.Average(e => confidence[e]) : double.NaN);
        }).ToList();

        var outliers = clustering.Outliers();
        if (!pieces.Traced)
            notes.Add("No supports given, or none reached, so no load path was traced: runs were not cut into pieces, and flow was "
                + "not compared. Wire the supports in to group the frame the way it stands.");
        if (!reading.Paths.Converged)
            notes.Add("The load-path flow stopped short of its tolerance, so flow, levels and where runs were cut are approximate. "
                + "Members many orders of magnitude apart in length are the usual cause.");
        if (reading.Structure.StrandedSupports.Length > 0)
            notes.Add($"{reading.Structure.StrandedSupports.Length} support(s) sit at no joint, so they hold nothing up: "
                + string.Join(", ", reading.Structure.StrandedSupports) + ".");
        if (reading.Tree.Traced && reading.Tree.MemberCantilever.Any(c => c))
            notes.Add($"{reading.Tree.MemberCantilever.Count(c => c)} run(s) are held up through a single joint and read as cantilevers; "
                + "a cantilever's effective length is longer than its span, so check their groups by hand.");
        notes.AddRange(clustering.Notes);

        return new SectionGroupingResult
        {
            Options = options,
            LineCount = lines,
            Reading = reading,
            Clustering = clustering,
            RunFamily = runFamily,
            Families = families,
            DesignLength = designLength,
            PieceGroup = pieceGroup,
            Group = group,
            Confidence = confidence,
            Groups = groups,
            FixedCount = options.Groups,
            OutlierRuns = outliers,
            Notes = notes,
        };
    }
}
