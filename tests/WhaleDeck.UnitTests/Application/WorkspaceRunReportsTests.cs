using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Development;

namespace WhaleDeck.UnitTests.Application;

public sealed class WorkspaceRunReportsTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);
    private const string JUnit = """
        <testsuite tests="2" failures="1">
          <testcase name="pass"/>
          <testcase name="hidden-case"><failure>private-stack</failure></testcase>
        </testsuite>
        """;

    [Fact]
    public async Task JoinsExactStepAndArtifactAndExportsSameTerminalFacts()
    {
        var run = Run();
        var artifact = Reference(run);
        var reader = new Reader(_ => Bytes(JUnit));
        var report = await new WorkspaceRunReports(reader).BuildAsync(run, [artifact], default);
        Assert.Equal(artifact, Assert.Single(reader.Opened));
        var step = Assert.Single(report.Steps);
        var test = Assert.Single(step.Tests);
        Assert.Equal(WorkspaceArtifactIntegrity.Matched, test.Integrity);
        Assert.Equal(WorkspaceTestReportState.Parsed, test.Result!.State);
        Assert.Equal(new WorkspaceTestCounts(1, 1, 0, 0, 0), test.Result.Counts);
        Assert.Equal(artifact.Sha256, test.Result.ArtifactSha256);
        Assert.Equal(2, Assert.Single(test.Result.Issues!).CaseOrdinal);

        var jsonBytes = WorkspaceRunReportExport.ToJson(report);
        using var json = JsonDocument.Parse(jsonBytes);
        var root = json.RootElement;
        var jsonStep = root.GetProperty("steps")[0];
        Assert.Equal(run.RunId, root.GetProperty("runId").GetGuid());
        Assert.Equal("Failed", root.GetProperty("outcome").GetString());
        Assert.Equal(1, jsonStep.GetProperty("exitCode").GetInt32());
        Assert.Equal(1000, jsonStep.GetProperty("durationMilliseconds").GetDecimal());
        Assert.Equal("Matched", jsonStep.GetProperty("tests")[0].GetProperty("integrity").GetString());
        Assert.Equal(1, jsonStep.GetProperty("tests")[0].GetProperty("result").GetProperty("counts").GetProperty("failed").GetInt32());

        var markdown = WorkspaceRunReportExport.ToMarkdown(report);
        Assert.Contains(run.RunId.ToString("D"), markdown, StringComparison.Ordinal);
        Assert.Contains("步骤状态 ` Failed `", markdown, StringComparison.Ordinal);
        Assert.Contains("耗时毫秒 ` 1000 `", markdown, StringComparison.Ordinal);
        Assert.Contains("失败 ` 1 `", markdown, StringComparison.Ordinal);
        Assert.Contains(artifact.Sha256, markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden-case", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("private-stack", Encoding.UTF8.GetString(jsonBytes), StringComparison.Ordinal);
        Assert.Equal(jsonBytes, WorkspaceRunReportExport.ToJson(report));
        Assert.Equal(markdown, WorkspaceRunReportExport.ToMarkdown(report));
    }

    [Theory]
    [InlineData("workspace")]
    [InlineData("run")]
    [InlineData("step")]
    [InlineData("format")]
    [InlineData("artifact")]
    [InlineData("duplicate")]
    [InlineData("never-started")]
    public async Task RejectsMisboundRegistrationsBeforeReadingAnyBytes(string wrong)
    {
        var run = Run();
        var artifact = Reference(run);
        var registered = wrong switch
        {
            "workspace" => artifact with { WorkspaceId = Guid.NewGuid() },
            "run" => artifact with { RunId = Guid.NewGuid() },
            "step" => artifact with { StepId = Guid.NewGuid() },
            "format" => artifact with { Format = WorkspaceTestFormat.Trx },
            "artifact" => artifact with { ArtifactId = Guid.NewGuid() },
            _ => artifact
        };
        if (wrong == "never-started")
            run = run with { Steps = [run.Steps[0] with { StartedAtUtc = null, FinishedAtUtc = null, ExitCode = null }] };

        var reader = new Reader(_ => throw new InvalidOperationException("must not open"));
        var reports = new WorkspaceRunReports(reader);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            reports.BuildAsync(run, wrong == "duplicate" ? [registered, registered] : [registered], default));
        Assert.Empty(reader.Opened);
    }

    [Theory]
    [InlineData("missing-registration", WorkspaceArtifactIntegrity.Missing, null)]
    [InlineData("missing-file", WorkspaceArtifactIntegrity.Missing, null)]
    [InlineData("io", WorkspaceArtifactIntegrity.Unverified, WorkspaceTestReportState.Unreadable)]
    [InlineData("denied", WorkspaceArtifactIntegrity.Unverified, WorkspaceTestReportState.Unreadable)]
    [InlineData("hash", WorkspaceArtifactIntegrity.Mismatch, null)]
    [InlineData("invalid-but-matched", WorkspaceArtifactIntegrity.Matched, WorkspaceTestReportState.Invalid)]
    public async Task UnusableTestsKeepTheirActualEvidenceState(
        string cause, WorkspaceArtifactIntegrity integrity, WorkspaceTestReportState? parserState)
    {
        var run = Run() with { Outcome = WorkspaceRunOutcome.Canceled, ExitCode = null };
        var content = cause == "invalid-but-matched" ? "<testsuite>" : JUnit;
        var artifact = Reference(run, content);
        var reader = new Reader(_ => cause switch
        {
            "missing-file" => null,
            "io" => throw new IOException("private filesystem location"),
            "denied" => throw new UnauthorizedAccessException("private filesystem location"),
            "hash" => Bytes("<testsuite><testcase name='fabricated-success'/></testsuite>"),
            _ => Bytes(content)
        });

        var report = await new WorkspaceRunReports(reader).BuildAsync(run, cause == "missing-registration" ? [] : [artifact], default);
        Assert.Equal(WorkspaceRunOutcome.Canceled, report.Outcome);
        var test = Assert.Single(Assert.Single(report.Steps).Tests);
        Assert.Equal(integrity, test.Integrity);
        Assert.Equal(parserState, test.Result?.State);
        Assert.Null(test.Result?.Counts);
        Assert.DoesNotContain("private filesystem location", WorkspaceRunReportExport.ToMarkdown(report), StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeepsSlotOrderAndDisposesStreamsWithoutReadingMissingSlots()
    {
        var run = Run();
        var second = new WorkspaceReportTestSlot(Guid.NewGuid(), WorkspaceTestFormat.JUnit);
        run = run with { Steps = [run.Steps[0] with { Tests = [run.Steps[0].Tests[0], second] }] };
        var artifact = Reference(run) with { ArtifactId = second.ArtifactId };
        var stream = Bytes(JUnit);
        var reader = new Reader(_ => stream);
        var report = await new WorkspaceRunReports(reader).BuildAsync(run, [artifact], default);
        Assert.Equal(artifact, Assert.Single(reader.Opened));
        Assert.Equal([WorkspaceArtifactIntegrity.Missing, WorkspaceArtifactIntegrity.Matched],
            report.Steps[0].Tests.Select(t => t.Integrity).ToArray());
        Assert.False(stream.CanRead);
    }

    [Fact]
    public async Task ExportsCoverageOnlyWhenTheRegisteredBytesMatch()
    {
        const string lcov = "SF:private/target\nDA:1,1\nLF:1\nLH:1\nend_of_record\n";
        var run = Run();
        var step = run.Steps[0];
        run = run with { Steps = [step with { Tests = [step.Tests[0] with { Format = WorkspaceTestFormat.Lcov }] }] };
        var artifact = Reference(run, lcov) with { Format = WorkspaceTestFormat.Lcov };
        var reports = new WorkspaceRunReports(new Reader(_ => Bytes(lcov)));
        var report = await reports.BuildAsync(run, [artifact], default);
        var markdown = WorkspaceRunReportExport.ToMarkdown(report);
        Assert.Contains("行覆盖 ` 1/1 `", markdown, StringComparison.Ordinal);
        Assert.Contains("分支覆盖 未知", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("private/target", markdown, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(WorkspaceRunReportExport.ToJson(report));
        var result = json.RootElement.GetProperty("steps")[0].GetProperty("tests")[0].GetProperty("result");
        Assert.Equal(1, result.GetProperty("coverage").GetProperty("lines").GetProperty("total").GetInt32());

        var mismatched = await reports.BuildAsync(run, [artifact with { Sha256 = new('a', 64) }], default);
        Assert.Equal(WorkspaceArtifactIntegrity.Mismatch, mismatched.Steps[0].Tests[0].Integrity);
        Assert.DoesNotContain("行覆盖", WorkspaceRunReportExport.ToMarkdown(mismatched), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsInvalidTimeAndAvoidsOpeningAfterCancellation()
    {
        var run = Run();
        var artifact = Reference(run);
        var reader = new Reader(_ => throw new InvalidOperationException("must not open"));
        var reports = new WorkspaceRunReports(reader);
        await Assert.ThrowsAsync<ArgumentException>(() => reports.BuildAsync(
            run with { FinalizedAtUtc = run.StartedAtUtc.AddMilliseconds(-1) }, [artifact], default));
        await Assert.ThrowsAsync<ArgumentException>(() => reports.BuildAsync(
            run with { Steps = [run.Steps[0] with { FinishedAtUtc = run.FinalizedAtUtc.AddSeconds(1) }] }, [artifact], default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reports.BuildAsync(run, [artifact], new(true)));
        Assert.Empty(reader.Opened);
    }

    [Fact]
    public async Task EscapesMarkdownSyntaxInOpaqueSubjectAndRejectsLineInjection()
    {
        var run = Run() with { ActorSubject = "`[label](https://example.invalid)<script>`" };
        var reports = new WorkspaceRunReports(new Reader(_ => null));
        var report = await reports.BuildAsync(run, [], default);
        var markdown = WorkspaceRunReportExport.ToMarkdown(report);
        Assert.Contains("`` `[label](https://example.invalid)<script>` ``", markdown, StringComparison.Ordinal);
        await Assert.ThrowsAsync<ArgumentException>(() => reports.BuildAsync(
            run with { ActorSubject = "owner\n# forged heading" }, [], default));
    }

    private static WorkspaceRunReportEvidence Run() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new('a', 32), "authentik-subject",
        Start, Start.AddSeconds(2), Start.AddDays(14), WorkspaceRunOutcome.Failed, 1,
        new(new('b', 64), new('c', 40), false, null),
        new(new('d', 64), 2, [new("node", "26.13.0")]),
        [new(Guid.NewGuid(), "node-tests", WorkspaceRunOutcome.Failed, Start, Start.AddSeconds(1), 1,
            [new(Guid.NewGuid(), WorkspaceTestFormat.JUnit)])]);

    private static WorkspaceRegisteredTestArtifact Reference(WorkspaceRunReportEvidence run, string content = JUnit) =>
        new(run.WorkspaceId, run.RunId, run.Steps[0].StepId, run.Steps[0].Tests[0].ArtifactId, WorkspaceTestFormat.JUnit,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content))));

    private static MemoryStream Bytes(string content) => new(Encoding.UTF8.GetBytes(content), writable: false);

    private sealed class Reader(Func<WorkspaceRegisteredTestArtifact, Stream?> open) : IWorkspaceReportArtifactReader
    {
        public List<WorkspaceRegisteredTestArtifact> Opened { get; } = [];
        public ValueTask<Stream?> OpenAsync(WorkspaceRegisteredTestArtifact artifact, CancellationToken cancellationToken)
        {
            Opened.Add(artifact);
            return ValueTask.FromResult(open(artifact));
        }
    }
}
