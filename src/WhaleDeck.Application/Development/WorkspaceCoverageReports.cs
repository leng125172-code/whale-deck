using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace WhaleDeck.Application.Development;

public sealed record WorkspaceCoverageMetric(long Covered, long Total);
public sealed record WorkspaceCoverageCounts(WorkspaceCoverageMetric Lines, WorkspaceCoverageMetric? Branches);

internal static class WorkspaceCoverageReports
{
    private const int MaxLcovLines = 300_000;
    private const int MaxLcovLineLength = 8192;

    public static WorkspaceTestReport ReadCobertura(XElement root, string digest, CancellationToken token)
    {
        const WorkspaceTestFormat format = WorkspaceTestFormat.Cobertura;
        var invalid = new WorkspaceTestReport(format, WorkspaceTestReportState.Invalid, digest);
        if (root.Name != "coverage" ||
            !TryMetric(root.Attribute("lines-covered")?.Value, root.Attribute("lines-valid")?.Value, out var lines))
            return invalid;

        WorkspaceCoverageMetric? branches = null;
        var branchCovered = root.Attribute("branches-covered")?.Value;
        var branchValid = root.Attribute("branches-valid")?.Value;
        if (branchCovered is not null || branchValid is not null)
        {
            if (!TryMetric(branchCovered, branchValid, out branches)) return invalid;
        }

        long observedLines = 0, observedHits = 0, observedBranches = 0, observedBranchesHit = 0;
        var classes = new HashSet<(string? Package, string File, string Name)>();
        foreach (var package in root.Elements("packages").Elements("package"))
        foreach (var type in package.Elements("classes").Elements("class"))
        {
            if ((string?)type.Attribute("filename") is not { Length: > 0 } file ||
                (string?)type.Attribute("name") is not { Length: > 0 } name ||
                !classes.Add(((string?)package.Attribute("name"), file, name)))
                return invalid;

            var lineNumbers = new HashSet<long>();
            // Method rows repeat the class rows. Count the class-level inventory once.
            foreach (var row in type.Elements("lines").Elements("line"))
            {
                token.ThrowIfCancellationRequested();
                if (!TryCount(row.Attribute("number")?.Value, out var line) || line == 0 || !lineNumbers.Add(line) ||
                    !TryCount(row.Attribute("hits")?.Value, out var hits))
                    return invalid;

                observedLines++;
                if (hits > 0) observedHits++;
                var branch = row.Attribute("branch")?.Value;
                if (branch is null) continue;
                if (!bool.TryParse(branch, out var hasBranch)) return invalid;
                if (!hasBranch) continue;
                if (!TryCondition(row.Attribute("condition-coverage")?.Value, out var condition) ||
                    observedBranches > long.MaxValue - condition.Total)
                    return invalid;
                observedBranches += condition.Total;
                observedBranchesHit += condition.Covered;
            }
        }

        if (lines != new WorkspaceCoverageMetric(observedHits, observedLines) ||
            (branches is not null && branches != new WorkspaceCoverageMetric(observedBranchesHit, observedBranches)))
            return new(format, WorkspaceTestReportState.Inconsistent, digest);
        return Complete(format, digest, lines, branches);
    }

    public static WorkspaceTestReport ReadLcov(Stream content, string digest, CancellationToken token)
    {
        const WorkspaceTestFormat format = WorkspaceTestFormat.Lcov;
        var invalid = new WorkspaceTestReport(format, WorkspaceTestReportState.Invalid, digest);
        try
        {
            using var reader = new StreamReader(content, new UTF8Encoding(true, true), detectEncodingFromByteOrderMarks: false);
            var files = new HashSet<string>(StringComparer.Ordinal);
            LcovFile? file = null;
            long lines = 0, lineHits = 0, branches = 0, branchHits = 0;
            var allBranchesMeasured = true;
            var count = 0;
            while (reader.ReadLine() is { } line)
            {
                token.ThrowIfCancellationRequested();
                if (++count > MaxLcovLines || line.Length > MaxLcovLineLength)
                    return new(format, WorkspaceTestReportState.TooComplex, digest);
                if (line.Length == 0) continue;

                if (line == "end_of_record")
                {
                    if (file is null) return invalid;
                    if (!file.Consistent)
                        return new(format, WorkspaceTestReportState.Inconsistent, digest);
                    lines += file.Lines.Count;
                    lineHits += file.LineHits;
                    branches += file.Branches.Count;
                    branchHits += file.BranchHits;
                    allBranchesMeasured &= file.BranchesFound.HasValue;
                    file = null;
                    continue;
                }

                var separator = line.IndexOf(':');
                if (separator < 1) return invalid;
                var name = line[..separator];
                var value = line[(separator + 1)..];
                if (name == "TN")
                {
                    if (file is not null) return invalid;
                    continue;
                }
                if (name == "SF")
                {
                    if (file is not null || string.IsNullOrWhiteSpace(value) || !files.Add(value)) return invalid;
                    file = new();
                    continue;
                }
                if (file is null || !file.Read(name, value)) return invalid;
            }

            if (file is not null) return invalid;
            return Complete(format, digest, new(lineHits, lines),
                allBranchesMeasured && files.Count > 0 ? new(branchHits, branches) : null);
        }
        catch (DecoderFallbackException)
        {
            return invalid;
        }
    }

    private static WorkspaceTestReport Complete(
        WorkspaceTestFormat format, string digest, WorkspaceCoverageMetric lines, WorkspaceCoverageMetric? branches) =>
        lines.Total == 0 && (branches?.Total ?? 0) == 0
            ? new(format, WorkspaceTestReportState.NoCoverage, digest)
            : new(format, WorkspaceTestReportState.Parsed, digest)
                { Coverage = new(lines, branches) };

    private static bool TryCondition(string? value, out WorkspaceCoverageMetric metric)
    {
        metric = new(0, 0);
        if (value is not { Length: > 0 } || !value.EndsWith(')')) return false;
        var open = value.LastIndexOf('(');
        var slash = value.LastIndexOf('/');
        return open >= 0 && slash > open && TryMetric(
            value.AsSpan(open + 1, slash - open - 1), value.AsSpan(slash + 1, value.Length - slash - 2), out metric)
            && metric.Total > 0;
    }

    private static bool TryMetric(ReadOnlySpan<char> covered, ReadOnlySpan<char> total, out WorkspaceCoverageMetric metric)
    {
        metric = new(0, 0);
        if (!TryCount(covered, out var hits) || !TryCount(total, out var count) || hits > count) return false;
        metric = new(hits, count);
        return true;
    }

    private static bool TryCount(ReadOnlySpan<char> text, out long count) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out count);

    private sealed class LcovFile
    {
        private long? linesFound, linesHit, branchesHit;
        public long? BranchesFound { get; private set; }
        public HashSet<long> Lines { get; } = [];
        public HashSet<(long Line, string Block, string Branch)> Branches { get; } = [];
        public long LineHits { get; private set; }
        public long BranchHits { get; private set; }
        public bool Consistent => linesFound == Lines.Count && linesHit == LineHits &&
            (BranchesFound is null && branchesHit is null && Branches.Count == 0 ||
             BranchesFound == Branches.Count && branchesHit == BranchHits);

        public bool Read(string tag, string value)
        {
            switch (tag)
            {
                case "DA":
                    var line = value.Split(',', 3);
                    if (line.Length < 2 || !TryCount(line[0], out var number) || number == 0 ||
                        !TryCount(line[1], out var hits) || !Lines.Add(number))
                        return false;
                    if (hits > 0) LineHits++;
                    return true;
                case "BRDA":
                    var branch = value.Split(',', 4);
                    if (branch.Length != 4 || !TryCount(branch[0], out var branchLine) || branchLine == 0 ||
                        branch[1].Length == 0 || branch[2].Length == 0 ||
                        !Branches.Add((branchLine, branch[1], branch[2])))
                        return false;
                    if (branch[3] == "-") return true;
                    if (!TryCount(branch[3], out var taken)) return false;
                    if (taken > 0) BranchHits++;
                    return true;
                case "LF": return ReadSummary(ref linesFound, value);
                case "LH": return ReadSummary(ref linesHit, value);
                case "BRH": return ReadSummary(ref branchesHit, value);
                case "BRF":
                    if (BranchesFound.HasValue || !TryCount(value, out var total)) return false;
                    BranchesFound = total;
                    return true;
                // Function and source-version rows carry no line or branch totals.
                case "FN": case "FNDA": case "FNF": case "FNH": case "FNL": case "FNA": case "VER":
                    return true;
                default: return false;
            }
        }

        private static bool ReadSummary(ref long? field, string value)
        {
            if (field.HasValue || !TryCount(value, out var total)) return false;
            field = total;
            return true;
        }
    }
}
