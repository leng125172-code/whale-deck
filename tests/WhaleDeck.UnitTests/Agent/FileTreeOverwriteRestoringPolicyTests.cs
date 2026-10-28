using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class FileTreeOverwriteRestoringPolicyTests
{
    private const string Operation = "073e1d38e73f450a822640746147f35c";
    private const string Capture = "846516d56a484ba59468cd61f55728ed";
    private static FileTreeOverwriteRestoringCommand Command() => new() {
        Source = new() { RootId = "test.shared", Path = "代码/原目录" }, ExpectedVersion = new('A', 64),
        Target = new() { RootId = "test.shared", Path = "代码/原目录" }, TargetExpectedVersion = new('9', 64),
        CaptureId = Capture, CaptureOwner = new('B', 64), CaptureKind = "Trash", ManifestSha256 = new('C', 64) };

    private static FileTreeOverwriteRestoringPlan Plan() => new() {
        Command = Command(), TargetParent = new() { Device = ulong.MaxValue, Inode = 9007199254740993UL },
        TargetIdentity = new() { Device = 9007199254740993UL, Inode = ulong.MaxValue },
        SourcePolicyHash = new('D', 64), TargetPolicyHash = new('E', 64), Fingerprint = new('F', 64),
        ArchiveSha256 = new('1', 64), ContentSha256 = new('2', 64), ArchiveBytes = FileTreeUnpackingPolicy.MaximumArchiveBytes,
        EntryCount = FileTreeCopyPolicy.MaximumEntries, TotalBytes = FileTreeCopyPolicy.MaximumBytes,
        TargetManifestSha256 = new('4', 64), TargetTreeSha256 = new('5', 64), TargetContentSha256 = new('6', 64),
        TargetEntryCount = FileTreeCopyPolicy.MaximumEntries, TargetTotalBytes = FileTreeCopyPolicy.MaximumBytes };

    [Theory]
    [InlineData("Trash")][InlineData("Overwrite")]
    public void FlatCommandBindsHistoricalSourceAndExistingTargetVersionsIndependently(string kind)
    {
        var command = Command(); command.CaptureKind = kind;
        using var document = JsonDocument.Parse(FileTreeOverwriteRestoringPolicy.CommandJson(command));
        var root = document.RootElement;
        Assert.Equal(7, root.EnumerateObject().Count());
        Assert.Equal("tar.gz", root.GetProperty("format").GetString());
        Assert.Equal(Capture, root.GetProperty("captureId").GetString());
        Assert.Equal(command.CaptureOwner, root.GetProperty("captureOwner").GetString());
        Assert.Equal(command.ExpectedVersion, root.GetProperty("source").GetProperty("expectedVersion").GetString());
        Assert.Equal(command.TargetExpectedVersion, root.GetProperty("target").GetProperty("expectedVersion").GetString());
        Assert.Equal(3, root.GetProperty("target").EnumerateObject().Count());
        Assert.Equal("代码/原目录", root.GetProperty("target").GetProperty("path").GetString());
        Assert.False(root.TryGetProperty("command", out _));
    }

    [Fact]
    public void FlatPlanRoundTripsFullUInt64IdentitiesAndIndependentWholeDirectoryLimits()
    {
        var plan = Plan(); var value = FileTreeOverwriteRestoringPolicy.PlanInput(plan);
        Assert.Equal(16, value.EnumerateObject().Count());
        Assert.Equal(plan, FileTreeOverwriteRestoringPolicy.Plan(value, Command()));
        Assert.Equal(ulong.MaxValue, value.GetProperty("targetParent")[0].GetUInt64());
        Assert.Equal(9007199254740993UL, value.GetProperty("targetParent")[1].GetUInt64());
        Assert.Equal(9007199254740993UL, value.GetProperty("targetIdentity")[0].GetUInt64());
        Assert.Equal(ulong.MaxValue, value.GetProperty("targetIdentity")[1].GetUInt64());
        Assert.Equal(FileTreeCopyPolicy.MaximumBytes, value.GetProperty("targetTotalBytes").GetInt64());
        Assert.Equal(plan.TargetContentSha256, value.GetProperty("targetContentSha256").GetString());
        Assert.NotEqual(plan.TargetTreeSha256, plan.TargetContentSha256);
        Assert.False(value.TryGetProperty("entries", out _));
        Assert.False(value.TryGetProperty("plan", out _));
        Assert.Equal(1024L * 1024 * 1024, FileTransferPolicy.MaximumBytes);
    }

    [Theory]
    [InlineData("")][InlineData("/etc")][InlineData("../escape")][InlineData("a//b")][InlineData("a/../b")]
    public void InvalidSourceAndTargetNeverReachPrivilegedCode(string path)
    {
        var command = Command(); command.Source.Path = path;
        Assert.Throws<ArgumentException>(() => FileTreeOverwriteRestoringPolicy.CommandJson(command));
        command = Command(); command.Target.Path = path;
        Assert.Throws<ArgumentException>(() => FileTreeOverwriteRestoringPolicy.CommandJson(command));
    }

    [Theory]
    [InlineData("CaptureId", "../capture")][InlineData("CaptureOwner", "administrator")]
    [InlineData("CaptureKind", "trash")][InlineData("ManifestSha256", "bad")]
    [InlineData("ExpectedVersion", "")][InlineData("TargetExpectedVersion", "")]
    [InlineData("TargetExpectedVersion", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void CommandRejectsInvalidPrivateResourceAndExistingTargetFacts(string property, string invalid)
    {
        var command = Command(); typeof(FileTreeOverwriteRestoringCommand).GetProperty(property)!.SetValue(command, invalid);
        Assert.Throws<ArgumentException>(() => FileTreeOverwriteRestoringPolicy.CommandJson(command));
        Assert.Throws<ArgumentException>(() => FileTreeOverwriteRestoringPolicy.CommandJson(null));
        Assert.Throws<ArgumentException>(() => FileTreeOverwriteRestoringPolicy.PlanInput(null));
    }

    [Theory]
    [InlineData("CaptureId")][InlineData("CaptureOwner")][InlineData("CaptureKind")][InlineData("ManifestSha256")]
    [InlineData("ExpectedVersion")][InlineData("TargetExpectedVersion")][InlineData("Source")][InlineData("Target")]
    public void HelperPlanCannotSubstituteAnySourceCaptureDestinationOrTargetVersion(string property)
    {
        var expected = Command();
        switch (property)
        {
            case "CaptureId": expected.CaptureId = Operation; break;
            case "CaptureOwner": expected.CaptureOwner = new('8', 64); break;
            case "CaptureKind": expected.CaptureKind = "Overwrite"; break;
            case "ManifestSha256": expected.ManifestSha256 = new('8', 64); break;
            case "ExpectedVersion": expected.ExpectedVersion = new('8', 64); break;
            case "TargetExpectedVersion": expected.TargetExpectedVersion = new('8', 64); break;
            case "Source": expected.Source.Path = "other-source"; break;
            case "Target": expected.Target.Path = "other-target"; break;
        }
        Assert.Throws<InvalidOperationException>(() => FileTreeOverwriteRestoringPolicy.Plan(FileTreeOverwriteRestoringPolicy.PlanInput(Plan()), expected));
    }

    [Theory]
    [InlineData("")][InlineData("command")][InlineData("source")][InlineData("target")]
    public void PlanRejectsUnknownMissingAndDuplicateFieldsAtEveryStructuredLevel(string level)
    {
        var original = FileTreeOverwriteRestoringPolicy.PlanInput(Plan());
        var node = JsonNode.Parse(original.GetRawText())!;
        var parent = level switch { "" => node, "command" => node["command"]!, _ => node["command"]![level]! };
        parent["hostPath"] = "/etc";
        Assert.Throws<InvalidOperationException>(() => FileTreeOverwriteRestoringPolicy.Plan(JsonSerializer.SerializeToElement(node), Command()));
        parent.AsObject().Remove("hostPath");
        parent.AsObject().Remove(parent.AsObject().First().Key);
        Assert.Throws<InvalidOperationException>(() => FileTreeOverwriteRestoringPolicy.Plan(JsonSerializer.SerializeToElement(node), Command()));
        var duplicateField = level switch { "" => "targetTreeSha256", "command" => "captureId", _ => "rootId" };
        using var duplicate = JsonDocument.Parse(original.GetRawText().Replace("\"" + duplicateField + "\":",
            "\"" + duplicateField + "\":null,\"" + duplicateField + "\":", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => FileTreeOverwriteRestoringPolicy.Plan(duplicate.RootElement, Command()));
    }

    [Theory]
    [InlineData("ArchiveSha256")][InlineData("ContentSha256")][InlineData("SourcePolicyHash")]
    [InlineData("TargetPolicyHash")][InlineData("Fingerprint")][InlineData("TargetManifestSha256")][InlineData("TargetTreeSha256")][InlineData("TargetContentSha256")]
    public void PlanRejectsInvalidArchiveAndExistingTargetHashes(string property)
    {
        var plan = Plan(); typeof(FileTreeOverwriteRestoringPlan).GetProperty(property)!.SetValue(plan, "bad");
        Assert.Throws<ArgumentException>(() => FileTreeOverwriteRestoringPolicy.PlanInput(plan));
    }

    [Theory]
    [InlineData("")][InlineData("bad")][InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void TargetContentProofIsRequiredAndValidatedIndependentlyOfRenameStableTreeProof(string invalid)
    {
        var original = FileTreeOverwriteRestoringPolicy.PlanInput(Plan());
        var node = JsonNode.Parse(original.GetRawText())!.AsObject();
        node["targetContentSha256"] = invalid;
        Assert.Throws<ArgumentException>(() => FileTreeOverwriteRestoringPolicy.Plan(JsonSerializer.SerializeToElement(node), Command()));
        node.Remove("targetContentSha256");
        Assert.Throws<InvalidOperationException>(() => FileTreeOverwriteRestoringPolicy.Plan(JsonSerializer.SerializeToElement(node), Command()));
    }

    [Fact]
    public void PlanRejectsOutOfRangeCountsBytesAndMissingIdentities()
    {
        foreach (var property in new[] { "EntryCount", "TargetEntryCount" })
        {
            foreach (var invalid in new[] { 0U, FileTreeCopyPolicy.MaximumEntries + 1 })
            {
                var plan = Plan(); typeof(FileTreeOverwriteRestoringPlan).GetProperty(property)!.SetValue(plan, invalid);
                Assert.Throws<ArgumentException>(() => FileTreeOverwriteRestoringPolicy.PlanInput(plan));
            }
        }
        foreach (var property in new[] { "TotalBytes", "TargetTotalBytes" })
        {
            foreach (var invalid in new[] { -1L, FileTreeCopyPolicy.MaximumBytes + 1 })
            {
                var plan = Plan(); typeof(FileTreeOverwriteRestoringPlan).GetProperty(property)!.SetValue(plan, invalid);
                Assert.Throws<ArgumentException>(() => FileTreeOverwriteRestoringPolicy.PlanInput(plan));
            }
        }
        foreach (var invalid in new[] { 0L, FileTreeUnpackingPolicy.MaximumArchiveBytes + 1 })
        {
            var plan = Plan(); plan.ArchiveBytes = invalid;
            Assert.Throws<ArgumentException>(() => FileTreeOverwriteRestoringPolicy.PlanInput(plan));
        }
        var missing = Plan(); missing.TargetIdentity = null;
        Assert.Throws<ArgumentException>(() => FileTreeOverwriteRestoringPolicy.PlanInput(missing));
        missing = Plan(); missing.TargetIdentity.Inode = 0;
        Assert.Throws<ArgumentException>(() => FileTreeOverwriteRestoringPolicy.PlanInput(missing));
        missing = Plan(); missing.TargetParent.Inode = 0;
        Assert.Throws<ArgumentException>(() => FileTreeOverwriteRestoringPolicy.PlanInput(missing));
    }

    [Theory]
    [InlineData("[]")][InlineData("[1]")][InlineData("[1,2,3]")][InlineData("[-1,2]")]
    [InlineData("[1,18446744073709551616]")][InlineData("[1,1.5]")][InlineData("[1,\"2\"]")]
    public void HelperIdentityMustBeExactlyTwoUnsignedIntegers(string identity)
    {
        var node = JsonNode.Parse(FileTreeOverwriteRestoringPolicy.PlanInput(Plan()).GetRawText())!;
        node["targetIdentity"] = JsonNode.Parse(identity);
        Assert.ThrowsAny<Exception>(() => FileTreeOverwriteRestoringPolicy.Plan(JsonSerializer.SerializeToElement(node), Command()));
    }

    private static JsonNode Receipt(string state, bool verified, bool changed = false, bool partial = false) => JsonSerializer.SerializeToNode(new {
        operationId = Operation, state, fingerprint = new string('F', 64), format = "tar.gz", entryCount = FileTreeCopyPolicy.MaximumEntries,
        totalBytes = FileTreeCopyPolicy.MaximumBytes, archiveBytes = FileTreeUnpackingPolicy.MaximumArchiveBytes, archiveSha256 = new string('1', 64),
        version = state == "Completed" ? new string('3', 64) : "", partialRetained = partial, kind = "DirectoryOverwriteRestore", captureId = Capture,
        overwrittenCaptureId = verified ? Operation : "", backupMayBeRetained = verified, backupVerified = verified,
        originalsRetained = state == "Completed", targetMayHaveChanged = changed,
        backupArchiveBytes = verified ? FileTreeUnpackingPolicy.MaximumArchiveBytes : 0, backupArchiveSha256 = verified ? new string('6', 64) : "" })!;

    [Theory]
    [InlineData("Completed", true, true, false)][InlineData("Aborted", false, false, false)]
    [InlineData("Aborted", true, false, true)][InlineData("Unknown", false, false, true)]
    [InlineData("Unknown", true, false, true)][InlineData("Unknown", true, true, true)]
    public void TerminalReceiptDistinguishesHistoricalCaptureNewBackupAndUncertainEffects(string state, bool verified, bool changed, bool partial)
    {
        var result = FileTreeOverwriteRestoringPolicy.Receipt(JsonSerializer.SerializeToElement(Receipt(state, verified, changed, partial)), Operation, Plan());
        Assert.Equal(state, result.State); Assert.Equal(Capture, result.CaptureId);
        Assert.Equal(verified ? Operation : "", result.OverwrittenCaptureId);
        Assert.NotEqual(result.CaptureId, result.OperationId);
        Assert.Equal(verified, result.BackupVerified); Assert.Equal(changed, result.TargetMayHaveChanged);
        Assert.Equal(partial, result.PartialRetained); Assert.Equal(state == "Completed", result.Version.Length > 0);
    }

    [Fact]
    public void UnverifiedStartedBackupMayRemainButCannotBecomeVerifiedOrClaimTargetEffects()
    {
        var node = Receipt("Unknown", false, false, true); node["backupMayBeRetained"] = true;
        var result = FileTreeOverwriteRestoringPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation, Plan());
        Assert.True(result.BackupMayBeRetained); Assert.False(result.BackupVerified);
        Assert.Equal(0, result.BackupArchiveBytes); Assert.Empty(result.BackupArchiveSha256); Assert.Empty(result.OverwrittenCaptureId);
        Assert.False(result.TargetMayHaveChanged); Assert.False(result.OriginalsRetained);
    }

    [Theory]
    [InlineData("backupMayBeRetained")][InlineData("backupVerified")][InlineData("originalsRetained")][InlineData("targetMayHaveChanged")]
    public void CompletedRequiresAllIndependentBackupAndOriginalRetentionProofs(string field)
    {
        var node = Receipt("Completed", true, true); node[field] = false;
        Assert.Throws<InvalidOperationException>(() => FileTreeOverwriteRestoringPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation));
    }

    [Theory]
    [InlineData("overwrittenCaptureId")][InlineData("backupArchiveSha256")][InlineData("backupArchiveBytes")]
    public void UnverifiedReceiptCannotSmuggleBackupIdentityOrArchiveProof(string field)
    {
        var node = Receipt("Unknown", false);
        if (field == "backupArchiveBytes") node[field] = 1;
        else node[field] = field == "overwrittenCaptureId" ? Operation : new string('6', 64);
        Assert.Throws<InvalidOperationException>(() => FileTreeOverwriteRestoringPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation));
    }

    [Theory]
    [InlineData("overwrittenCaptureId", "846516d56a484ba59468cd61f55728ed")]
    [InlineData("overwrittenCaptureId", "")][InlineData("backupArchiveSha256", "")]
    [InlineData("backupArchiveSha256", "bad")][InlineData("backupArchiveBytes", "zero")]
    [InlineData("backupArchiveBytes", "oversized")]
    public void VerifiedReceiptNeedsCurrentOperationBackupAndBoundedValidArchiveProof(string field, string invalid)
    {
        var node = Receipt("Completed", true, true);
        if (field == "backupArchiveBytes") node[field] = invalid == "zero" ? 0 : FileTreeUnpackingPolicy.MaximumArchiveBytes + 1;
        else node[field] = invalid;
        Assert.ThrowsAny<Exception>(() => FileTreeOverwriteRestoringPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation));
    }

    [Fact]
    public void TargetEffectsRequireVerifiedBackupAndCannotBeAbortedOrInventSuccessfulVersion()
    {
        var node = Receipt("Unknown", false); node["targetMayHaveChanged"] = true;
        Assert.Throws<InvalidOperationException>(() => FileTreeOverwriteRestoringPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation));
        node = Receipt("Unknown", true); node["originalsRetained"] = true;
        Assert.Throws<InvalidOperationException>(() => FileTreeOverwriteRestoringPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation));
        node = Receipt("Aborted", true, true);
        Assert.Throws<InvalidOperationException>(() => FileTreeOverwriteRestoringPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation));
        node = Receipt("Unknown", true, true); node["version"] = new string('3', 64);
        Assert.Throws<InvalidOperationException>(() => FileTreeOverwriteRestoringPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation));
        node = Receipt("Completed", true, true, true);
        Assert.Throws<InvalidOperationException>(() => FileTreeOverwriteRestoringPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation));
    }

    [Theory]
    [InlineData("fingerprint")][InlineData("archiveSha256")][InlineData("entryCount")]
    [InlineData("totalBytes")][InlineData("archiveBytes")][InlineData("captureId")]
    public void ReceiptMustMatchRegisteredPlanNotAnotherHistoricalCapture(string field)
    {
        var node = Receipt("Completed", true, true);
        if (field is "entryCount" or "totalBytes" or "archiveBytes") node[field] = 1;
        else node[field] = field == "captureId" ? Operation : new string('8', 64);
        Assert.Throws<InvalidOperationException>(() => FileTreeOverwriteRestoringPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation, Plan()));
    }

    [Theory]
    [InlineData("kind", "DirectoryRestore")][InlineData("format", "tar")][InlineData("state", "Running")]
    [InlineData("state", "completed")][InlineData("version", "")]
    public void ReceiptRejectsWrongOperationKindFormatStateOrSuccessVersion(string field, string changed)
    {
        var node = Receipt("Completed", true, true); node[field] = changed;
        Assert.ThrowsAny<Exception>(() => FileTreeOverwriteRestoringPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation, Plan()));
    }

    [Fact]
    public void ReceiptRejectsExtraMissingDuplicateFieldsAndForeignCurrentOperation()
    {
        var original = Receipt("Completed", true, true);
        Assert.Throws<InvalidOperationException>(() => FileTreeOverwriteRestoringPolicy.Receipt(JsonSerializer.SerializeToElement(original), Capture, Plan()));
        var node = original.DeepClone(); node["path"] = "/private";
        Assert.Throws<InvalidOperationException>(() => FileTreeOverwriteRestoringPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation));
        node = original.DeepClone(); node.AsObject().Remove("backupVerified");
        Assert.Throws<InvalidOperationException>(() => FileTreeOverwriteRestoringPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation));
        using var duplicate = JsonDocument.Parse(original.ToJsonString().Replace("\"backupVerified\":", "\"backupVerified\":true,\"backupVerified\":", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => FileTreeOverwriteRestoringPolicy.Receipt(duplicate.RootElement, Operation));
    }

    [Fact]
    public async Task InvalidCallsAreRejectedBeforeRunnerAndCapabilityIsIndependentFromAbsentTargetRestore()
    {
        var service = new FileTreeOverwriteRestoringGrpcService(new FileTransferRunner());
        var missing = await Assert.ThrowsAsync<RpcException>(() => service.Plan(new() { Command = Command() }, null!));
        Assert.Equal("FILES_ACTOR_REQUIRED", missing.Status.Detail);
        var owner = await Assert.ThrowsAsync<RpcException>(() => service.Inspect(new() { OperationId = Operation, Owner = "bad" }, null!));
        Assert.Equal("FILES_HASH_INVALID", owner.Status.Detail);
        var apply = await Assert.ThrowsAsync<RpcException>(() => service.Apply(new() { Context = new() { ActorSubject = "admin" }, OperationId = "bad" }, null!));
        Assert.Equal("FILES_OPERATION_INVALID", apply.Status.Detail);
        var discard = await Assert.ThrowsAsync<RpcException>(() => service.Discard(new() { OperationId = Operation, Owner = new('A', 64), Plan = Plan() }, null!));
        Assert.Equal("FILES_ACTOR_REQUIRED", discard.Status.Detail);
        var invalidTarget = Command(); invalidTarget.TargetExpectedVersion = "";
        var plan = await Assert.ThrowsAsync<RpcException>(() => service.Plan(new() { Context = new() { ActorSubject = "admin" }, Command = invalidTarget }, null!));
        Assert.Equal(StatusCode.InvalidArgument, plan.StatusCode);
        var capabilities = await new AgentGrpcService(null!).GetCapabilities(new(), null!);
        Assert.Equal(45U, capabilities.ProtocolMinor);
        Assert.Contains("files.tree-overwrite-restore-intents", capabilities.Capabilities);
        Assert.Contains("files.tree-overwrite-restore-content-proof", capabilities.Capabilities);
        Assert.Contains("files.tree-restore-intents", capabilities.Capabilities);
        Assert.Equal("whaledeck.agent.v1.FileTreeOverwriteRestoringService", FileTreeOverwriteRestoringService.Descriptor.FullName);
    }
}
