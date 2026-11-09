using System.Globalization;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace WhaleDeck.Application.Development;

public enum WorkspaceTestFormat { Trx, JUnit }
public enum WorkspaceTestReportState { Parsed, Missing, Unsupported, Unreadable, TooLarge, TooComplex, Invalid, NoCases, Inconsistent }
public enum WorkspaceTestOutcome { Passed, Failed, Error, Skipped, Other }
public sealed record WorkspaceTestCounts(int Passed, int Failed, int Errors, int Skipped, int Other)
{
    public int Total => Passed + Failed + Errors + Skipped + Other;
}
public sealed record WorkspaceTestIssue(int CaseOrdinal, WorkspaceTestOutcome Outcome);
public sealed record WorkspaceTestReport(
    WorkspaceTestFormat Format, WorkspaceTestReportState State, string? ArtifactSha256,
    WorkspaceTestCounts? Counts = null, IReadOnlyList<WorkspaceTestIssue>? Issues = null, bool IssuesTruncated = false);

/// <summary>Reads one run-owned test artifact. Only structural facts are returned, including for failed runs.</summary>
public static class WorkspaceTestReports
{
    public const int MaxArtifactBytes = 16 * 1024 * 1024;
    private const int MaxXmlDepth = 32;
    private const int MaxXmlNodes = 300_000;
    private const int MaxIssues = 100;
    private static readonly XNamespace TrxNamespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public static async Task<WorkspaceTestReport> AnalyzeAsync(
        Stream? artifact, WorkspaceTestFormat format, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (format is not (WorkspaceTestFormat.Trx or WorkspaceTestFormat.JUnit))
            return new(format, WorkspaceTestReportState.Unsupported, null);
        if (artifact is null)
            return new(format, WorkspaceTestReportState.Missing, null);

        using var content = new MemoryStream();
        var buffer = new byte[32 * 1024];
        try
        {
            while (true)
            {
                // One extra byte distinguishes exact-limit EOF from truncated evidence.
                var count = await artifact.ReadAsync(
                    buffer.AsMemory(0, Math.Min(buffer.Length, MaxArtifactBytes + 1 - (int)content.Length)), cancellationToken);
                if (count == 0) break;
                if (content.Length + count > MaxArtifactBytes)
                    return new(format, WorkspaceTestReportState.TooLarge, null);
                content.Write(buffer, 0, count);
            }
        }
        catch (IOException)
        {
            return new(format, WorkspaceTestReportState.Unreadable, null);
        }

        var digest = Convert.ToHexStringLower(SHA256.HashData(content.GetBuffer().AsSpan(0, (int)content.Length)));
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            MaxCharactersInDocument = MaxArtifactBytes
        };
        try
        {
            content.Position = 0;
            using (var check = XmlReader.Create(content, settings))
            {
                var nodes = 0;
                while (check.Read())
                {
                    if (check.NodeType != XmlNodeType.Element) continue;
                    cancellationToken.ThrowIfCancellationRequested();
                    nodes += 1 + check.AttributeCount;
                    if (check.Depth >= MaxXmlDepth || nodes > MaxXmlNodes)
                        return new(format, WorkspaceTestReportState.TooComplex, digest);
                }
            }

            content.Position = 0;
            using var reader = XmlReader.Create(content, settings);
            var root = XDocument.Load(reader).Root!;
            var observations = new Observations(cancellationToken);
            var consistent = format == WorkspaceTestFormat.Trx
                ? ReadTrx(root, observations)
                : ReadJUnit(root, observations);
            var counts = observations.Counts;
            var state = !consistent || counts.Other > 0 ? WorkspaceTestReportState.Inconsistent
                : counts.Total == 0 ? WorkspaceTestReportState.NoCases : WorkspaceTestReportState.Parsed;
            return new(format, state, digest, counts, observations.Issues.ToArray(), observations.IssuesTruncated);
        }
        catch (XmlException)
        {
            return new(format, WorkspaceTestReportState.Invalid, digest);
        }
    }

    private static bool ReadTrx(XElement root, Observations observations)
    {
        if (root.Name != TrxNamespace + "TestRun") throw new XmlException();
        var results = Single(root, TrxNamespace + "Results");
        var summary = Single(root, TrxNamespace + "ResultSummary");
        var counters = summary is null ? null : Single(summary, TrxNamespace + "Counters");
        var resultName = TrxNamespace + "UnitTestResult";
        var uniqueExecutions = new HashSet<Guid>();
        if (results is not null)
        {
            foreach (var test in results.Elements())
            {
                if (test.Name != resultName ||
                    !Guid.TryParseExact((string?)test.Attribute("executionId"), "D", out var execution) ||
                    execution == Guid.Empty || !uniqueExecutions.Add(execution))
                    throw new XmlException();
                observations.Add((string?)test.Attribute("outcome") switch
                {
                    "Passed" => WorkspaceTestOutcome.Passed,
                    "Failed" => WorkspaceTestOutcome.Failed,
                    "Error" => WorkspaceTestOutcome.Error,
                    "NotExecuted" => WorkspaceTestOutcome.Skipped,
                    _ => WorkspaceTestOutcome.Other
                });
            }
        }

        var counts = observations.Counts;
        // Nested TRX rollups require adapter-specific semantics. Do not count them as flat cases.
        return root.Descendants(resultName).Count() == counts.Total && counters is not null &&
            Matches(counters, "total", counts.Total, required: true) &&
            Matches(counters, "passed", counts.Passed, required: true) &&
            Matches(counters, "failed", counts.Failed, required: true) &&
            Matches(counters, "error", counts.Errors) &&
            Matches(counters, "executed", counts.Total - counts.Skipped) &&
            (Matches(counters, "notExecuted", 0) || Matches(counters, "notExecuted", counts.Skipped)) &&
            Matches(counters, "timeout", 0) && Matches(counters, "aborted", 0) &&
            Matches(counters, "inconclusive", 0) && Matches(counters, "notRunnable", 0) &&
            Matches(counters, "passedButRunAborted", 0) && Matches(counters, "disconnected", 0) &&
            Matches(counters, "inProgress", 0) && Matches(counters, "pending", 0) &&
            (string?)summary!.Attribute("outcome") is "Completed" or "Passed" or "Failed";
    }

    private static bool ReadJUnit(XElement root, Observations observations)
    {
        if (root.Name != "testsuite" && root.Name != "testsuites") throw new XmlException();
        var consistent = true;
        VisitSuite(root, observations, ref consistent);
        return consistent && root.Descendants("testcase").Count() == observations.Counts.Total;
    }

    private static void VisitSuite(XElement suite, Observations observations, ref bool consistent)
    {
        var before = observations.Counts;
        foreach (var entry in suite.Elements())
        {
            if (entry.Name == "testsuite")
                VisitSuite(entry, observations, ref consistent);
            else if (entry.Name == "testcase")
                observations.Add(ReadJUnitCase(entry));
        }
        var after = observations.Counts;
        consistent &= Matches(suite, "tests", after.Total - before.Total) &&
            Matches(suite, "failures", after.Failed - before.Failed) &&
            Matches(suite, "errors", after.Errors - before.Errors) &&
            Matches(suite, "skipped", after.Skipped - before.Skipped);
    }

    private static WorkspaceTestOutcome ReadJUnitCase(XElement test)
    {
        if (string.IsNullOrWhiteSpace((string?)test.Attribute("name"))) throw new XmlException();
        var failed = test.Element("failure") is not null;
        var error = test.Element("error") is not null;
        var skipped = test.Element("skipped") is not null;
        var status = (string?)test.Attribute("status");
        var result = (string?)test.Attribute("result");
        skipped |= status == "notrun" || result is "skipped" or "suppressed";
        if ((failed && error) || ((failed || error) && skipped))
            throw new XmlException();
        if (failed) return WorkspaceTestOutcome.Failed;
        if (error) return WorkspaceTestOutcome.Error;
        if (skipped) return WorkspaceTestOutcome.Skipped;
        if (test.Attribute("failure") is not null || test.Attribute("error") is not null ||
            test.Elements().Any(node => node.Name != "system-out" && node.Name != "system-err" && node.Name != "properties"))
            return WorkspaceTestOutcome.Other;
        return (status is null or "run") && (result is null or "completed")
            ? WorkspaceTestOutcome.Passed : WorkspaceTestOutcome.Other;
    }

    private static XElement? Single(XElement parent, XName name)
    {
        var matches = parent.Elements(name).Take(2).ToArray();
        return matches.Length > 1 ? throw new XmlException() : matches.FirstOrDefault();
    }

    private static bool Matches(XElement node, string field, int observed, bool required = false) =>
        node.Attribute(field) is { } attr
            ? int.TryParse(attr.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count == observed
            : !required;

    private sealed class Observations(CancellationToken cancellationToken)
    {
        private readonly int[] counts = new int[5];
        private int ordinal;
        public WorkspaceTestCounts Counts => new(counts[0], counts[1], counts[2], counts[3], counts[4]);
        public List<WorkspaceTestIssue> Issues { get; } = [];
        public bool IssuesTruncated { get; private set; }

        public void Add(WorkspaceTestOutcome outcome)
        {
            cancellationToken.ThrowIfCancellationRequested();
            counts[(int)outcome]++;
            ordinal++;
            if (outcome is WorkspaceTestOutcome.Passed or WorkspaceTestOutcome.Skipped) return;
            if (Issues.Count < MaxIssues) Issues.Add(new(ordinal, outcome));
            else IssuesTruncated = true;
        }
    }
}
