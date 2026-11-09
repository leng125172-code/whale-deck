using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace WhaleDeck.Application.Development;

public enum WorkspaceRunOutcome { Succeeded, Failed, Canceled, TimedOut, Interrupted, Unknown, Skipped }
public enum WorkspaceArtifactIntegrity { Missing, Unverified, Mismatch, Matched }

public sealed record WorkspaceReportSource(
    string? Fingerprint, string? GitCommit, bool? Dirty, bool? ChangedDuringRun);
public sealed record WorkspaceReportTool(string Name, string Version);
public sealed record WorkspaceReportEnvironment(
    string Digest, long Version, ImmutableArray<WorkspaceReportTool> Tools);
public sealed record WorkspaceReportTestSlot(Guid ArtifactId, WorkspaceTestFormat Format, WorkspaceArtifactCollectionState? Collection = null);
public enum WorkspaceArtifactCollectionState { Present, Missing, Rejected, TooLarge, Unavailable, IntegrityFailure }
public sealed record WorkspaceReportStepEvidence(
    Guid StepId, string Key, WorkspaceRunOutcome Outcome, DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc, int? ExitCode, ImmutableArray<WorkspaceReportTestSlot> Tests);
public sealed record WorkspaceRunReportEvidence(
    Guid WorkspaceId, Guid RunId, Guid JobId, string TraceId, string ActorSubject,
    DateTimeOffset StartedAtUtc, DateTimeOffset FinalizedAtUtc, DateTimeOffset RetainUntilUtc,
    WorkspaceRunOutcome Outcome, int? ExitCode, WorkspaceReportSource Source,
    WorkspaceReportEnvironment Environment, ImmutableArray<WorkspaceReportStepEvidence> Steps);

public sealed record WorkspaceRegisteredTestArtifact(
    Guid WorkspaceId, Guid RunId, Guid StepId, Guid ArtifactId, WorkspaceTestFormat Format, string Sha256);

public interface IWorkspaceReportArtifactReader
{
    /// <summary>Opens the registered immutable artifact in its exact scope, or returns null if unavailable.</summary>
    ValueTask<Stream?> OpenAsync(WorkspaceRegisteredTestArtifact artifact, CancellationToken cancellationToken);
}

public sealed record WorkspaceReportTest(
    Guid ArtifactId, WorkspaceTestFormat Format, WorkspaceArtifactIntegrity Integrity, WorkspaceTestReport? Result)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkspaceArtifactCollectionState? Collection { get; init; }
}
public sealed record WorkspaceReportStep(
    Guid StepId, string Key, WorkspaceRunOutcome Outcome, DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc, int? ExitCode, ImmutableArray<WorkspaceReportTest> Tests)
{
    public decimal? DurationMilliseconds => StartedAtUtc is { } start && FinishedAtUtc is { } end
        ? (decimal)(end - start).Ticks / TimeSpan.TicksPerMillisecond : null;
}
public sealed record WorkspaceRunReport(
    int SchemaVersion, Guid WorkspaceId, Guid RunId, Guid JobId, string TraceId, string ActorSubject,
    DateTimeOffset StartedAtUtc, DateTimeOffset FinalizedAtUtc, DateTimeOffset RetainUntilUtc,
    WorkspaceRunOutcome Outcome, int? ExitCode, WorkspaceReportSource Source,
    WorkspaceReportEnvironment Environment, ImmutableArray<WorkspaceReportStep> Steps);

/// <summary>Composes a report from the scheduler's finalized run snapshot and trusted artifact registration.</summary>
public sealed class WorkspaceRunReports(IWorkspaceReportArtifactReader reader)
{
    public async Task<WorkspaceRunReport> BuildAsync(
        WorkspaceRunReportEvidence run, ImmutableArray<WorkspaceRegisteredTestArtifact> artifacts,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registered = Validate(run, artifacts);
        var steps = ImmutableArray.CreateBuilder<WorkspaceReportStep>(run.Steps.Length);
        foreach (var step in run.Steps)
        {
            var tests = ImmutableArray.CreateBuilder<WorkspaceReportTest>(step.Tests.Length);
            foreach (var slot in step.Tests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = registered.TryGetValue(slot.ArtifactId, out var artifact)
                    ? await ReadAsync(artifact, cancellationToken)
                    : new(slot.ArtifactId, slot.Format, slot.Collection == WorkspaceArtifactCollectionState.IntegrityFailure
                        ? WorkspaceArtifactIntegrity.Mismatch : WorkspaceArtifactIntegrity.Missing, null);
                tests.Add(result with { Collection = slot.Collection });
            }
            steps.Add(new(step.StepId, step.Key, step.Outcome,
                step.StartedAtUtc is { } start ? DatabaseTime(start) : null,
                step.FinishedAtUtc is { } finish ? DatabaseTime(finish) : null,
                step.ExitCode, tests.MoveToImmutable()));
        }

        return new(1, run.WorkspaceId, run.RunId, run.JobId, run.TraceId, run.ActorSubject,
            DatabaseTime(run.StartedAtUtc), DatabaseTime(run.FinalizedAtUtc), DatabaseTime(run.RetainUntilUtc),
            run.Outcome, run.ExitCode,
            run.Source, run.Environment, steps.MoveToImmutable());
    }

    private async Task<WorkspaceReportTest> ReadAsync(
        WorkspaceRegisteredTestArtifact artifact, CancellationToken cancellationToken)
    {
        WorkspaceTestReport result;
        try
        {
            await using var stream = await reader.OpenAsync(artifact, cancellationToken);
            if (stream is null)
                return new(artifact.ArtifactId, artifact.Format, WorkspaceArtifactIntegrity.Missing, null);
            result = await WorkspaceTestReports.AnalyzeAsync(stream, artifact.Format, cancellationToken);
        }
        catch (IOException)
        {
            result = new(artifact.Format, WorkspaceTestReportState.Unreadable, null);
        }
        catch (UnauthorizedAccessException)
        {
            result = new(artifact.Format, WorkspaceTestReportState.Unreadable, null);
        }

        if (result.ArtifactSha256 is null)
            return new(artifact.ArtifactId, artifact.Format, WorkspaceArtifactIntegrity.Unverified, result);
        return string.Equals(result.ArtifactSha256, artifact.Sha256, StringComparison.Ordinal)
            ? new(artifact.ArtifactId, artifact.Format, WorkspaceArtifactIntegrity.Matched, result)
            : new(artifact.ArtifactId, artifact.Format, WorkspaceArtifactIntegrity.Mismatch, null);
    }

    private static Dictionary<Guid, WorkspaceRegisteredTestArtifact> Validate(
        WorkspaceRunReportEvidence run, ImmutableArray<WorkspaceRegisteredTestArtifact> artifacts)
    {
        if (run.WorkspaceId == Guid.Empty || run.RunId == Guid.Empty || run.JobId == Guid.Empty ||
            !Hex(run.TraceId, 32) || run.TraceId.All(c => c == '0') || !Subject(run.ActorSubject) ||
            !Utc(run.StartedAtUtc) || !Utc(run.FinalizedAtUtc) || !Utc(run.RetainUntilUtc) ||
            run.FinalizedAtUtc < run.StartedAtUtc || run.RetainUntilUtc <= run.FinalizedAtUtc ||
            !Enum.IsDefined(run.Outcome) || run.Outcome == WorkspaceRunOutcome.Skipped ||
            (run.Outcome == WorkspaceRunOutcome.Succeeded && run.ExitCode is not (null or 0)) ||
            run.Source is null || (run.Source.Fingerprint is null ? run.RunId != run.JobId : !Hex(run.Source.Fingerprint, 64)) ||
            (run.Source.GitCommit is { } commit && !Hex(commit, 40) && !Hex(commit, 64)) ||
            run.Environment is null || !Hex(run.Environment.Digest, 64) || run.Environment.Version < 1 ||
            run.Environment.Tools.IsDefault || run.Environment.Tools.Length > 16 ||
            run.Steps.IsDefault || run.Steps.Length > 32 || artifacts.IsDefault || artifacts.Length > 8)
            throw Invalid();

        var tools = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in run.Environment.Tools)
        {
            if (tool is null || !Token(tool.Name) || !Token(tool.Version) || !tools.Add(tool.Name)) throw Invalid();
        }

        var stepIds = new HashSet<Guid>();
        var stepKeys = new HashSet<string>(StringComparer.Ordinal);
        var slots = new Dictionary<Guid, (Guid Step, WorkspaceTestFormat Format, bool Started)>();
        foreach (var step in run.Steps)
        {
            if (step is null || step.StepId == Guid.Empty || !stepIds.Add(step.StepId) ||
                !Token(step.Key) || !stepKeys.Add(step.Key) || !Enum.IsDefined(step.Outcome) ||
                !ValidTiming(step, run) || step.Tests.IsDefault || step.Tests.Length > 8)
                throw Invalid();

            foreach (var slot in step.Tests)
            {
                if (slot is null || slot.ArtifactId == Guid.Empty || !Enum.IsDefined(slot.Format) ||
                    (slot.Collection is { } collection && !Enum.IsDefined(collection)) ||
                    slots.Count == 8 || !slots.TryAdd(slot.ArtifactId, (step.StepId, slot.Format, step.StartedAtUtc.HasValue)))
                    throw Invalid();
            }
        }

        var registered = new Dictionary<Guid, WorkspaceRegisteredTestArtifact>();
        foreach (var artifact in artifacts)
        {
            if (artifact is null || artifact.WorkspaceId != run.WorkspaceId || artifact.RunId != run.RunId ||
                !slots.TryGetValue(artifact.ArtifactId, out var slot) ||
                slot != (artifact.StepId, artifact.Format, true) || !Hex(artifact.Sha256, 64) ||
                !registered.TryAdd(artifact.ArtifactId, artifact))
                throw Invalid();
        }
        return registered;
    }

    private static bool ValidTiming(WorkspaceReportStepEvidence step, WorkspaceRunReportEvidence run)
    {
        if (step.Outcome == WorkspaceRunOutcome.Skipped)
            return step.StartedAtUtc is null && step.FinishedAtUtc is null && step.ExitCode is null;
        if (step.Outcome == WorkspaceRunOutcome.Succeeded &&
            (step.StartedAtUtc is null || step.FinishedAtUtc is null || step.ExitCode != 0))
            return false;
        if (step.StartedAtUtc is { } started && (!Utc(started) || started < run.StartedAtUtc || started > run.FinalizedAtUtc))
            return false;
        if (step.FinishedAtUtc is { } finished &&
            (!Utc(finished) || step.StartedAtUtc is null || finished < step.StartedAtUtc || finished > run.FinalizedAtUtc))
            return false;
        return step.ExitCode is null || step.FinishedAtUtc is not null;
    }

    // PostgreSQL stores timestamp values at microsecond precision. Render the
    // same bytes before and after the scheduler reloads its persisted snapshot.
    private static DateTimeOffset DatabaseTime(DateTimeOffset value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
    private static bool Utc(DateTimeOffset value) => value.Offset == TimeSpan.Zero && value != default;
    private static bool Hex(string? value, int length) =>
        value?.Length == length && value.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');
    private static bool Token(string? value) =>
        value is { Length: > 0 and <= 64 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '+');
    private static bool Subject(string? value) =>
        value is { Length: > 0 and <= 256 } && !string.IsNullOrWhiteSpace(value) &&
        value.All(c => !char.IsControl(c) && !char.IsSurrogate(c) &&
            char.GetUnicodeCategory(c) is not (System.Globalization.UnicodeCategory.Format or
                System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator));
    private static ArgumentException Invalid() => new("WORKSPACE_REPORT_EVIDENCE_INVALID");
}
