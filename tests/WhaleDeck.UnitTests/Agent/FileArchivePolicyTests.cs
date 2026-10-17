using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class FileArchivePolicyTests
{
    private const string Id = "11111111111111111111111111111111";
    private const string Version = "22222222222222222222222222222222";
    private static FileArchivePlan Plan(FileArchiveKind kind = FileArchiveKind.Trash, bool absent = false) => new() {
        Command = new() { Kind = kind, Location = new() { RootId = "whaledeck.files.shared", Path = "目录/source.bin" }, VersionId = kind == FileArchiveKind.Trash ? "" : Version,
            ExpectedVersion = kind == FileArchiveKind.Purge || absent ? "ABSENT" : new('A', 64) },
        ParentIdentity = kind == FileArchiveKind.Purge ? null : new() { Device = ulong.MaxValue, Inode = 9007199254740993UL },
        SourceIdentity = kind == FileArchiveKind.Purge || absent ? null : new() { Device = ulong.MaxValue, Inode = 9007199254740993UL, Mode = 0x81A4, OwnerUid = 1000, OwnerGid = 1000, LinkCount = 1, SizeBytes = 7, ModifiedAtUnixNs = -1 },
        SourceSha256 = kind == FileArchiveKind.Purge || absent ? "" : new('B', 64),
        ArchiveIdentity = kind == FileArchiveKind.Trash ? null : new() { MetadataHash = new('C', 64), DataIdentity = new() { Device = 1, Inode = 2, Mode = 0x8180, OwnerUid = 0, OwnerGid = 0, LinkCount = 1, SizeBytes = 7 } },
        RestoreMetadata = kind == FileArchiveKind.Trash ? null : new() { Device = 1, Inode = 3, Mode = 0x89E8, OwnerUid = 1000, OwnerGid = 1000, LinkCount = 1, SizeBytes = 7, ModifiedAtUnixNs = -1 },
        SizeBytes = 7, Sha256 = new('B', 64), Fingerprint = new('D', 64)
    };
    [Theory]
    [InlineData(FileArchiveKind.Trash, false)][InlineData(FileArchiveKind.Restore, false)]
    [InlineData(FileArchiveKind.Restore, true)][InlineData(FileArchiveKind.Purge, true)]
    public void DedicatedPlanRoundtripPreservesUnsignedIdentitiesAndNegativeNanoseconds(FileArchiveKind kind, bool absent)
    {
        var plan = Plan(kind, absent); var value = FileArchivePolicy.PlanInput(plan);
        Assert.Equal(plan, FileArchivePolicy.Plan(value, plan.Command));
        if (plan.SourceIdentity is not null)
        { Assert.Equal(ulong.MaxValue, value.GetProperty("sourceIdentity")[0].GetUInt64()); Assert.Equal(-1, value.GetProperty("sourceIdentity")[7].GetInt64()); }
        var other = plan.Command.Clone(); other.Location.Path = "elsewhere";
        Assert.Throws<InvalidOperationException>(() => FileArchivePolicy.Plan(value, other));
        var node = JsonNode.Parse(value.GetRawText())!; node["unexpected"] = true;
        using var extra = JsonDocument.Parse(node.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => FileArchivePolicy.Plan(extra.RootElement, plan.Command));
    }
    [Theory]
    [InlineData("/etc/passwd")][InlineData("../escape")][InlineData("a//b")][InlineData("a\\b")]
    public void CommandsRejectEscapingLocations(string path)
    { var command = Plan().Command; command.Location.Path = path; Assert.Throws<ArgumentException>(() => FileArchivePolicy.Command(command)); }
    [Fact]
    public void InvalidKindsArchiveIdsAndObjectTypesAreNotAccepted()
    {
        var plan = Plan(); plan.Command.Kind = (FileArchiveKind)99; Assert.Throws<ArgumentException>(() => FileArchivePolicy.PlanInput(plan));
        plan = Plan(); plan.Command.VersionId = Id; Assert.Throws<ArgumentException>(() => FileArchivePolicy.PlanInput(plan));
        plan = Plan(FileArchiveKind.Restore); plan.Command.VersionId = new('0', 32); Assert.Throws<ArgumentException>(() => FileArchivePolicy.PlanInput(plan));
        plan = Plan(); plan.SourceIdentity.LinkCount = 2; Assert.Throws<ArgumentException>(() => FileArchivePolicy.PlanInput(plan));
        plan = Plan(); plan.SourceIdentity.OwnerUid = uint.MaxValue; Assert.Throws<ArgumentException>(() => FileArchivePolicy.PlanInput(plan));
        plan = Plan(); plan.SourceIdentity.Mode = 0x41A4; Assert.Throws<ArgumentException>(() => FileArchivePolicy.PlanInput(plan));
        plan = Plan(FileArchiveKind.Restore); plan.ArchiveIdentity.DataIdentity.OwnerUid = 1000; Assert.Throws<ArgumentException>(() => FileArchivePolicy.PlanInput(plan));
        plan = Plan(FileArchiveKind.Restore); plan.ArchiveIdentity.DataIdentity.Mode = 0x81A4; Assert.Throws<ArgumentException>(() => FileArchivePolicy.PlanInput(plan));
        plan = Plan(FileArchiveKind.Restore); plan.RestoreMetadata.SizeBytes++; Assert.Throws<ArgumentException>(() => FileArchivePolicy.PlanInput(plan));
        plan = Plan(FileArchiveKind.Purge); plan.ParentIdentity = new() { Inode = 2 }; Assert.Throws<ArgumentException>(() => FileArchivePolicy.PlanInput(plan));
    }
    private static JsonElement Receipt(FileArchivePlan plan, string state = "Completed")
    {
        var retained = plan.Command.Kind == FileArchiveKind.Trash || plan.Command.Kind == FileArchiveKind.Restore && plan.SourceIdentity is not null;
        return JsonSerializer.SerializeToElement(new { operationId = Id, kind = FileArchivePolicy.Kind(plan.Command.Kind), state, fingerprint = plan.Fingerprint,
            version = state == "Completed" ? new string('E', 64) : "", sizeBytes = plan.SizeBytes, sha256 = plan.Sha256,
            sourceRetained = state == "Completed" && retained, bindingCurrent = state == "Completed", versionId = plan.Command.Kind == FileArchiveKind.Trash ? Id : Version,
            hddBackup = retained ? new { operationId = Id, storageId = "database-platform.backup.hdd", sourceVersion = plan.Command.ExpectedVersion,
                sizeBytes = plan.SourceIdentity!.SizeBytes, sha256 = plan.SourceSha256, createdAtUnixMs = 1760000000000L, differentSourceDevice = false } : null });
    }
    [Theory]
    [InlineData(FileArchiveKind.Trash, false)][InlineData(FileArchiveKind.Restore, false)]
    [InlineData(FileArchiveKind.Restore, true)][InlineData(FileArchiveKind.Purge, true)]
    public void ReceiptsRequireExactHddProofTargetAndNoFalseUnknownSuccess(FileArchiveKind kind, bool absent)
    {
        var plan = Plan(kind, absent);
        foreach (var state in new[] { "Completed", "Aborted", "Unknown" }) Assert.Equal(state, FileArchivePolicy.Receipt(Receipt(plan, state), Id, plan).State);
        var node = JsonNode.Parse(Receipt(plan).GetRawText())!; node["versionId"] = "33333333333333333333333333333333";
        using var wrong = JsonDocument.Parse(node.ToJsonString()); Assert.Throws<InvalidOperationException>(() => FileArchivePolicy.Receipt(wrong.RootElement, Id, plan));
        node = JsonNode.Parse(Receipt(plan).GetRawText())!; node["state"] = "Unknown";
        using var falseSuccess = JsonDocument.Parse(node.ToJsonString()); Assert.Throws<InvalidOperationException>(() => FileArchivePolicy.Receipt(falseSuccess.RootElement, Id, plan));
        if (node["hddBackup"] is not null)
        {
            node = JsonNode.Parse(Receipt(plan).GetRawText())!; node["hddBackup"]!["sizeBytes"] = 8;
            using var badProof = JsonDocument.Parse(node.ToJsonString()); Assert.Throws<InvalidOperationException>(() => FileArchivePolicy.Receipt(badProof.RootElement, Id, plan));
        }
    }
    [Fact]
    public void HistoryBoundedPagingRejectsWrongRootCursorDuplicatesAndImpossibleStates()
    {
        var request = new FileHistoryRequest { Location = Plan().Command.Location, Take = 50 };
        JsonElement Page(string root = "whaledeck.files.shared", string state = "Available", string next = "", bool duplicate = false) {
            var row = new { versionId = Id, rootId = root, path = request.Location.Path, sourceVersion = new string('A', 64), sizeBytes = 7,
                sha256 = new string('B', 64), createdAtUnixMs = 1760000000000L, captureKind = "Overwrite", captureState = state, availability = "Present", differentSourceDevice = false };
            return JsonSerializer.SerializeToElement(new { entries = duplicate ? new[] { row, row } : [row], nextCursor = next, version = new string('D', 64), scanned = 2, invalidRecords = 0, complete = next == "" });
        }
        Assert.Single(FileArchivePolicy.History(Page(), request).Entries);
        Assert.Throws<InvalidOperationException>(() => FileArchivePolicy.History(Page(root: "other"), request));
        Assert.Throws<InvalidOperationException>(() => FileArchivePolicy.History(Page(state: "Trashed"), request));
        Assert.Throws<InvalidOperationException>(() => FileArchivePolicy.History(Page(duplicate: true), request));
        Assert.Throws<InvalidOperationException>(() => FileArchivePolicy.History(Page(next: "00000000000000000000000000000001"), request));
        request.Take = 51; Assert.Throws<ArgumentException>(() => FileArchivePolicy.HistoryInput(request));
    }
}
