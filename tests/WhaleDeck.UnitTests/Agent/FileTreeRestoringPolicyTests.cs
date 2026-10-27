using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class FileTreeRestoringPolicyTests
{
    private const string Operation = "073e1d38e73f450a822640746147f35c";
    private const string Capture = "846516d56a484ba59468cd61f55728ed";
    private static FileTreeRestoringCommand Command() => new() {
        Source = new() { RootId = "test.shared", Path = "代码/原目录" }, ExpectedVersion = new('A', 64),
        Target = new() { RootId = "test.shared", Path = "代码/原目录" }, CaptureId = Capture,
        CaptureOwner = new('B', 64), CaptureKind = "Trash", ManifestSha256 = new('C', 64) };
    private static FileTreeRestoringPlan Plan() => new() {
        Command = Command(), TargetParent = new() { Device = ulong.MaxValue, Inode = 9007199254740993UL },
        SourcePolicyHash = new('D', 64), TargetPolicyHash = new('E', 64), Fingerprint = new('F', 64),
        ArchiveSha256 = new('1', 64), ContentSha256 = new('2', 64), ArchiveBytes = FileTreeUnpackingPolicy.MaximumArchiveBytes,
        EntryCount = FileTreeCopyPolicy.MaximumEntries, TotalBytes = FileTreeCopyPolicy.MaximumBytes };

    [Theory]
    [InlineData("Trash")][InlineData("Overwrite")]
    public void PrivateCaptureAndAbsentOriginalPathAreBoundWithoutLiveCopyOverlapRestriction(string kind)
    {
        var command = Command(); command.CaptureKind = kind;
        using var document = JsonDocument.Parse(FileTreeRestoringPolicy.CommandJson(command));
        var root = document.RootElement;
        Assert.Equal(7, root.EnumerateObject().Count()); Assert.Equal("tar.gz", root.GetProperty("format").GetString());
        Assert.Equal(Capture, root.GetProperty("captureId").GetString()); Assert.Equal(new string('B', 64), root.GetProperty("captureOwner").GetString());
        Assert.Equal("代码/原目录", root.GetProperty("source").GetProperty("path").GetString());
        Assert.Equal(root.GetProperty("source").GetProperty("path").GetString(), root.GetProperty("target").GetProperty("path").GetString());
        var plan = Plan(); plan.Command = command; var value = FileTreeRestoringPolicy.PlanInput(plan);
        Assert.Equal(plan, FileTreeRestoringPolicy.Plan(value, command));
        Assert.Equal(ulong.MaxValue, value.GetProperty("targetParent")[0].GetUInt64());
        Assert.Equal(9007199254740993UL, value.GetProperty("targetParent")[1].GetUInt64());
        Assert.False(value.TryGetProperty("entries", out _));
    }

    [Theory]
    [InlineData("")][InlineData("/etc")][InlineData("../escape")][InlineData("a//b")][InlineData("a/../b")]
    public void SourceAndTargetMustBeNonRootRegisteredRelativePaths(string path)
    {
        var command = Command(); command.Source.Path = path;
        Assert.Throws<ArgumentException>(() => FileTreeRestoringPolicy.CommandJson(command));
        command = Command(); command.Target.Path = path;
        Assert.Throws<ArgumentException>(() => FileTreeRestoringPolicy.CommandJson(command));
    }

    [Fact]
    public void CommandRejectsInvalidCaptureResourceFactsWithoutCallingPrivilegedCode()
    {
        foreach (var field in new[] { "captureId", "captureOwner", "manifestSha256", "captureKind", "expectedVersion", "root" })
        {
            var command = Command();
            switch (field)
            {
                case "captureId": command.CaptureId = "../capture"; break;
                case "captureOwner": command.CaptureOwner = "admin"; break;
                case "manifestSha256": command.ManifestSha256 = "not-a-hash"; break;
                case "captureKind": command.CaptureKind = "trash"; break;
                case "expectedVersion": command.ExpectedVersion = ""; break;
                case "root": command.Target.RootId = "/data"; break;
            }
            Assert.Throws<ArgumentException>(() => FileTreeRestoringPolicy.CommandJson(command));
        }
        Assert.Throws<ArgumentException>(() => FileTreeRestoringPolicy.CommandJson(null));
        Assert.Throws<ArgumentException>(() => FileTreeRestoringPolicy.PlanInput(null));
    }

    [Theory]
    [InlineData("captureId")][InlineData("captureOwner")][InlineData("captureKind")][InlineData("manifestSha256")]
    [InlineData("expectedVersion")][InlineData("source")][InlineData("target")]
    public void HelperPlanCannotSubstituteAnyCaptureOrDestinationFact(string field)
    {
        var value = FileTreeRestoringPolicy.PlanInput(Plan()); var expected = Command();
        switch (field)
        {
            case "captureId": expected.CaptureId = Operation; break;
            case "captureOwner": expected.CaptureOwner = new('9', 64); break;
            case "captureKind": expected.CaptureKind = "Overwrite"; break;
            case "manifestSha256": expected.ManifestSha256 = new('9', 64); break;
            case "expectedVersion": expected.ExpectedVersion = new('9', 64); break;
            case "source": expected.Source.Path = "different"; break;
            case "target": expected.Target.Path = "different"; break;
        }
        Assert.Throws<InvalidOperationException>(() => FileTreeRestoringPolicy.Plan(value, expected));
    }

    [Fact]
    public void PlanFieldSetsRejectExtraMissingDuplicateAndInjectedOwnershipOrPaths()
    {
        var original = FileTreeRestoringPolicy.PlanInput(Plan());
        foreach (var level in new[] { "", "command", "source", "target" })
        {
            var node = JsonNode.Parse(original.GetRawText())!;
            var parent = level switch { "" => node, "command" => node["command"]!, _ => node["command"]![level]! };
            parent["hostPath"] = "/etc";
            Assert.Throws<InvalidOperationException>(() => FileTreeRestoringPolicy.Plan(JsonSerializer.SerializeToElement(node), Command()));
            parent.AsObject().Remove("hostPath"); parent.AsObject().Remove(parent.AsObject().First().Key);
            Assert.Throws<InvalidOperationException>(() => FileTreeRestoringPolicy.Plan(JsonSerializer.SerializeToElement(node), Command()));
        }
        using var duplicate = JsonDocument.Parse(original.GetRawText().Replace("\"captureId\":", "\"captureId\":\"" + Capture + "\",\"captureId\":", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => FileTreeRestoringPolicy.Plan(duplicate.RootElement, Command()));
        var format = JsonNode.Parse(original.GetRawText())!; format["command"]!["format"] = "tar";
        Assert.Throws<InvalidOperationException>(() => FileTreeRestoringPolicy.Plan(JsonSerializer.SerializeToElement(format), Command()));
    }

    [Fact]
    public void PlanLimitsAndHashesRemainIndependentOfLegacyOneGiBFileLimit()
    {
        foreach (var field in new[] { "ArchiveSha256", "ContentSha256", "SourcePolicyHash", "TargetPolicyHash", "Fingerprint" })
        {
            var plan = Plan(); typeof(FileTreeRestoringPlan).GetProperty(field)!.SetValue(plan, "bad");
            Assert.Throws<ArgumentException>(() => FileTreeRestoringPolicy.PlanInput(plan));
        }
        var invalid = Plan(); invalid.ArchiveBytes++; Assert.Throws<ArgumentException>(() => FileTreeRestoringPolicy.PlanInput(invalid));
        invalid = Plan(); invalid.ArchiveBytes = 0; Assert.Throws<ArgumentException>(() => FileTreeRestoringPolicy.PlanInput(invalid));
        invalid = Plan(); invalid.TotalBytes++; Assert.Throws<ArgumentException>(() => FileTreeRestoringPolicy.PlanInput(invalid));
        invalid = Plan(); invalid.TotalBytes = -1; Assert.Throws<ArgumentException>(() => FileTreeRestoringPolicy.PlanInput(invalid));
        invalid = Plan(); invalid.EntryCount++; Assert.Throws<ArgumentException>(() => FileTreeRestoringPolicy.PlanInput(invalid));
        invalid = Plan(); invalid.EntryCount = 0; Assert.Throws<ArgumentException>(() => FileTreeRestoringPolicy.PlanInput(invalid));
        invalid = Plan(); invalid.TargetParent.Inode = 0; Assert.Throws<ArgumentException>(() => FileTreeRestoringPolicy.PlanInput(invalid));
        Assert.Equal(1024L * 1024 * 1024, FileTransferPolicy.MaximumBytes);
    }

    private static JsonNode Receipt(string state, bool partial) => JsonSerializer.SerializeToNode(new {
        operationId = Operation, state, fingerprint = new string('F', 64), format = "tar.gz", entryCount = FileTreeCopyPolicy.MaximumEntries,
        totalBytes = FileTreeCopyPolicy.MaximumBytes, archiveBytes = FileTreeUnpackingPolicy.MaximumArchiveBytes, archiveSha256 = new string('1', 64),
        version = state == "Completed" ? new string('3', 64) : "", partialRetained = partial, kind = "DirectoryRestore", captureId = Capture })!;

    [Theory]
    [InlineData("Completed", false)][InlineData("Aborted", false)][InlineData("Aborted", true)][InlineData("Unknown", false)][InlineData("Unknown", true)]
    public void ReceiptKeepsHistoricalCaptureDistinctFromNewOperationAndDoesNotInventUnknownSuccess(string state, bool partial)
    {
        var node = Receipt(state, partial); var result = FileTreeRestoringPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation, Plan());
        Assert.Equal(state, result.State); Assert.Equal(Capture, result.CaptureId); Assert.NotEqual(result.OperationId, result.CaptureId);
        Assert.Equal(partial, result.PartialRetained); Assert.Equal(state == "Completed", result.Version.Length > 0);
        var plan = Plan(); plan.Command.CaptureId = Operation;
        Assert.Throws<InvalidOperationException>(() => FileTreeRestoringPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation, plan));
    }

    [Theory]
    [InlineData("captureId", "bad")][InlineData("kind", "DirectoryUnpack")][InlineData("format", "tar")]
    [InlineData("state", "Running")][InlineData("version", "")][InlineData("archiveSha256", "bad")]
    public void ReceiptRejectsInvalidOrForeignFacts(string field, string changed)
    {
        var node = Receipt("Completed", false); node[field] = changed;
        Assert.ThrowsAny<Exception>(() => FileTreeRestoringPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation, Plan()));
    }

    [Fact]
    public void ReceiptRejectsForeignOperationProofChangesExtraDuplicateFieldsAndContradictoryEffects()
    {
        var node = Receipt("Completed", false);
        Assert.Throws<InvalidOperationException>(() => FileTreeRestoringPolicy.Receipt(JsonSerializer.SerializeToElement(node), Capture, Plan()));
        node["partialRetained"] = true;
        Assert.Throws<InvalidOperationException>(() => FileTreeRestoringPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation));
        node = Receipt("Unknown", true); node["version"] = new string('3', 64);
        Assert.Throws<InvalidOperationException>(() => FileTreeRestoringPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation));
        foreach (var field in new[] { "fingerprint", "archiveSha256", "entryCount", "totalBytes", "archiveBytes", "captureId" })
        {
            node = Receipt("Completed", false);
            if (field is "entryCount" or "totalBytes" or "archiveBytes") node[field] = 1;
            else node[field] = field == "captureId" ? Operation : new string('9', 64);
            Assert.Throws<InvalidOperationException>(() => FileTreeRestoringPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation, Plan()));
        }
        node = Receipt("Completed", false); node["path"] = "/private";
        Assert.Throws<InvalidOperationException>(() => FileTreeRestoringPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation));
        using var duplicate = JsonDocument.Parse(Receipt("Completed", false).ToJsonString().Replace("\"kind\":", "\"kind\":\"DirectoryRestore\",\"kind\":", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => FileTreeRestoringPolicy.Receipt(duplicate.RootElement, Operation));
    }

    [Fact]
    public async Task InvalidRequestsStopBeforePrivilegedRunnerAndCapabilityIsAdvertisedIndependently()
    {
        var service = new FileTreeRestoringGrpcService(new FileTransferRunner());
        var missing = await Assert.ThrowsAsync<RpcException>(() => service.Plan(new() { Command = Command() }, null!));
        Assert.Equal("FILES_ACTOR_REQUIRED", missing.Status.Detail);
        var owner = await Assert.ThrowsAsync<RpcException>(() => service.Inspect(new() { OperationId = Operation, Owner = "bad" }, null!));
        Assert.Equal("FILES_HASH_INVALID", owner.Status.Detail);
        var apply = await Assert.ThrowsAsync<RpcException>(() => service.Apply(new() { Context = new() { ActorSubject = "current-admin" }, OperationId = "bad" }, null!));
        Assert.Equal("FILES_OPERATION_INVALID", apply.Status.Detail);
        var discard = await Assert.ThrowsAsync<RpcException>(() => service.Discard(new() { OperationId = Operation, Owner = new('A', 64), Plan = Plan() }, null!));
        Assert.Equal("FILES_ACTOR_REQUIRED", discard.Status.Detail);
        var capabilities = await new AgentGrpcService(null!).GetCapabilities(new(), null!);
        Assert.Equal(43U, capabilities.ProtocolMinor); Assert.Contains("files.tree-restore-intents", capabilities.Capabilities);
        Assert.Equal("whaledeck.agent.v1.FileTreeRestoringService", FileTreeRestoringService.Descriptor.FullName);
    }
}
