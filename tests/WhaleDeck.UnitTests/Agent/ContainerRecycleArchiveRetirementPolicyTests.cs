using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerRecycleArchiveRetirementPolicyTests
{
    private static ContainerRecycleArchiveRetirementRequest Request()
    {
        var q = ContainerRecycleArchiveRetirementContractTests.Request(); q.Context.JobId = q.OperationId;
        q.Plan.PublishedIdentity.Device = q.Plan.Command.Archive.Plan.ParentIdentity.Device;
        q.Plan.Fingerprint = ContainerRecycleArchiveRetirementWire.Fingerprint(q.Plan); return q;
    }
    [Fact]
    public void CompleteNestedPlanAndNativeRoundtripPreserveYoungScheduling()
    {
        var q = Request(); ContainerRecycleArchiveRetirementPolicy.Request(q);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ContainerRecycleArchiveRetirementWire.Plan(q.Plan)));
        Assert.Equal(20, json.RootElement.EnumerateObject().Count());
        Assert.Equal(q.Plan, ContainerRecycleArchiveRetirementPolicy.PlanOutput(json.RootElement, q.Plan.Command));
        Assert.False(q.Plan.RetirementEligible); // Structural validation never promotes expiry.
    }

    [Theory]
    [InlineData("days-zero")][InlineData("days-max")][InlineData("mode")][InlineData("archive-id")][InlineData("history-time")][InlineData("due")][InlineData("eligibility")]
    [InlineData("source-digest")][InlineData("purge-digest")][InlineData("marker-digest")][InlineData("history-digest")][InlineData("claim-digest")][InlineData("record-digest")]
    [InlineData("storage")][InlineData("store")][InlineData("data-inode")][InlineData("data-time")][InlineData("bytes")][InlineData("sha")]
    [InlineData("published-source")][InlineData("published-archive")][InlineData("published-mode")][InlineData("published-uid")][InlineData("published-gid")][InlineData("published-link")][InlineData("published-size")][InlineData("published-time")][InlineData("published-ctime")][InlineData("published-device")]
    public void InvalidFactsRemainInvalidAfterRehash(string field)
    {
        var p = Request().Plan; var a = p.Command.Archive.Plan;
        switch (field)
        {
            case "days-zero": p.Command.RetainDays = 0; break; case "days-max": p.Command.RetainDays = 3651; break; case "mode": p.Command.Mode = "All"; break;
            case "archive-id": p.Command.Archive.OperationId = a.Command.Purge.OperationId; break;
            case "history-time": p.HistoryCreatedAtUnixMs++; break; case "due": p.NotBeforeUnixMs++; break; case "eligibility": p.RetirementEligible = true; break;
            case "source-digest": p.SourceRecordDigest = new('F', 64); break; case "purge-digest": p.PurgeRecordDigest = new('F', 64); break;
            case "marker-digest": p.PurgeMarkerDigest = new('F', 64); break; case "history-digest": p.HistoryMetadataDigest = new('F', 64); break;
            case "claim-digest": p.ClaimDigest = "invalid"; break; case "record-digest": p.ArchiveRecordDigest = "invalid"; break;
            case "storage": p.StorageHash = new('F', 64); break; case "store": p.StoreIdentity.Inode++; break;
            case "data-inode": p.ArchiveIdentity.Inode++; break; case "data-time": p.ArchiveChangedAtUnixNs++; break; case "bytes": p.ArchiveBytes++; break; case "sha": p.ArchiveSha256 = new('F', 64); break;
            case "published-source": p.PublishedIdentity = a.Command.Purge.Plan.Command.Source.Plan.SourceIdentity.Clone(); break;
            case "published-archive": p.PublishedIdentity = a.ArchiveIdentity.Clone(); break;
            case "published-mode": p.PublishedIdentity.Mode++; break; case "published-uid": p.PublishedIdentity.OwnerUid++; break; case "published-gid": p.PublishedIdentity.OwnerGid++; break;
            case "published-link": p.PublishedIdentity.LinkCount = 2; break; case "published-size": p.PublishedIdentity.SizeBytes++; break; case "published-time": p.PublishedIdentity.ModifiedAtUnixNs++; break;
            case "published-ctime": p.PublishedChangedAtUnixNs = -1; break; case "published-device": p.PublishedIdentity.Device++; break;
        }
        p.Fingerprint = ContainerRecycleArchiveRetirementWire.Fingerprint(p);
        Assert.Throws<ArgumentException>(() => ContainerRecycleArchiveRetirementPolicy.Plan(p));
    }

    [Theory][InlineData("actor")][InlineData("trace")][InlineData("job")][InlineData("key")][InlineData("capability")][InlineData("owner")][InlineData("archive-id")][InlineData("purge-id")][InlineData("source-id")]
    public void RequestContextAndFreshIdsAreMandatory(string field)
    {
        var q = Request();
        switch (field)
        {
            case "actor": q.Context.ActorSubject = " "; break; case "trace": q.Context.TraceId = "bad\ntrace"; break;
            case "job": q.Context.JobId = Guid.NewGuid().ToString("N"); break; case "key": q.Context.IdempotencyKey = ""; break;
            case "capability": q.Context.Capability = ByteString.CopyFrom([1]); break; case "owner": q.Owner = "bad"; break;
            case "archive-id": q.OperationId = q.Plan.Command.Archive.OperationId; q.Context.JobId = q.OperationId; break;
            case "purge-id": q.OperationId = q.Plan.Command.Archive.Plan.Command.Purge.OperationId; q.Context.JobId = q.OperationId; break;
            case "source-id": q.OperationId = q.Plan.Command.Archive.Plan.Command.Purge.Plan.Command.Source.OperationId; q.Context.JobId = q.OperationId; break;
        }
        Assert.Throws<ArgumentException>(() => ContainerRecycleArchiveRetirementPolicy.Request(q));
    }

    [Fact]
    public void PlanningContextCannotSmuggleExecutionJobOrAuthority()
    {
        var q = Request(); var p = new ContainerRecycleArchiveRetirementPlanRequest { Command = q.Plan.Command.Clone(), Context = new() { ActorSubject = "admin", TraceId = "trace" } };
        ContainerRecycleArchiveRetirementPolicy.PlanRequest(p); p.Context.JobId = q.OperationId;
        Assert.Throws<ArgumentException>(() => ContainerRecycleArchiveRetirementPolicy.PlanRequest(p));
        Assert.Throws<ArgumentException>(() => ContainerRecycleArchiveRetirementPolicy.Request(null));
    }

    [Theory][InlineData("request")][InlineData("plan")][InlineData("command")][InlineData("archive")][InlineData("published")][InlineData("context")]
    public void UnknownNestedProtobufFieldsNeverBecomeRootInput(string field)
    {
        var q = Request(); byte[] unknown = [248, 7, 1];
        switch (field)
        {
            case "request": q = ContainerRecycleArchiveRetirementRequest.Parser.ParseFrom(q.ToByteArray().Concat(unknown).ToArray()); break;
            case "plan": q.Plan = ContainerRecycleArchiveRetirementPlan.Parser.ParseFrom(q.Plan.ToByteArray().Concat(unknown).ToArray()); break;
            case "command": q.Plan.Command = ContainerRecycleArchiveRetirementCommand.Parser.ParseFrom(q.Plan.Command.ToByteArray().Concat(unknown).ToArray()); break;
            case "archive": q.Plan.Command.Archive = ContainerRecycleArchiveRetirementSource.Parser.ParseFrom(q.Plan.Command.Archive.ToByteArray().Concat(unknown).ToArray()); break;
            case "published": q.Plan.PublishedIdentity = FileObjectIdentity.Parser.ParseFrom(q.Plan.PublishedIdentity.ToByteArray().Concat(unknown).ToArray()); break;
            case "context": q.Context = RequestContext.Parser.ParseFrom(q.Context.ToByteArray().Concat(unknown).ToArray()); break;
        }
        Assert.Throws<ArgumentException>(() => ContainerRecycleArchiveRetirementPolicy.Request(q));
    }

    [Theory][InlineData("duplicate")][InlineData("unknown")][InlineData("float")][InlineData("boolean")][InlineData("identity")][InlineData("command")]
    public void NativeOutputRejectsDuplicateUnknownAndLossyValues(string field)
    {
        var p = Request().Plan; var raw = JsonSerializer.Serialize(ContainerRecycleArchiveRetirementWire.Plan(p));
        if (field == "duplicate") raw = raw.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal);
        else
        {
            var json = JsonNode.Parse(raw)!.AsObject();
            switch (field)
            {
                case "unknown": json["extra"] = 1; break; case "float": json["notBeforeUnixMs"] = (double)p.NotBeforeUnixMs + 0.25; break;
                case "boolean": json["retentionElapsed"] = 0; break; case "identity": json["archiveIdentity"] = new JsonArray(1, 2); break;
                case "command": json["command"]!["retainDays"] = 1; break;
            }
            raw = json.ToJsonString();
        }
        using var doc = JsonDocument.Parse(raw);
        Assert.Throws<InvalidOperationException>(() => ContainerRecycleArchiveRetirementPolicy.PlanOutput(doc.RootElement, p.Command));
    }

    [Theory][InlineData("Reserved", false)][InlineData("Aborted", false)][InlineData("Unknown", null)][InlineData("Unknown", false)][InlineData("Completed", true)]
    public void NativeReceiptKeepsNullableAppliedEvidence(string state, bool? applied)
    {
        var q = Request(); var source = q.Plan.Command.Archive.Plan.Command.Purge.Plan.Command.Source.OperationId;
        var value = new { operationId = q.OperationId, sourceOperationId = source, archiveOperationId = q.Plan.Command.Archive.OperationId, fingerprint = q.Plan.Fingerprint, state, sourceFenced = state != "Aborted", ownedFence = state is "Reserved" or "Completed", retirementApplied = applied, notBeforeUnixMs = q.Plan.NotBeforeUnixMs };
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(value)); var r = ContainerRecycleArchiveRetirementPolicy.Receipt(json.RootElement, q);
        Assert.Equal(applied.HasValue, r.HasRetirementApplied); if (applied.HasValue) Assert.Equal(applied, r.RetirementApplied);
    }
}
