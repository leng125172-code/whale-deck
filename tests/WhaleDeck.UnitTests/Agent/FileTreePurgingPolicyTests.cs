using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

/// <summary>Strict transport contracts only; never starts the privileged runner or deletes payloads.</summary>
public sealed class FileTreePurgingPolicyTests
{
    private const string Operation = "073e1d38e73f450a822640746147f35c";
    private const string Capture = "846516d56a484ba59468cd61f55728ed";
    private static FileTreePurgingCommand Command(FileTreePurgeScope scope = FileTreePurgeScope.All) => new() {
        Source = new() { RootId = "test.shared", Path = "代码/已捕获目录" }, ExpectedVersion = new('A', 64),
        CaptureId = Capture, CaptureOwner = new('B', 64), CaptureKind = "Trash", ManifestSha256 = new('C', 64), Scope = scope };

    private static FileTreePurgingPlan Plan(FileTreePurgeScope scope = FileTreePurgeScope.All)
    {
        var plan = new FileTreePurgingPlan {
            Command = Command(scope), SourcePolicyHash = new('D', 64), StorageHash = new('E', 64),
            StoreIdentity = new() { Device = ulong.MaxValue, Inode = 9007199254740993UL },
            CaptureMetadataHash = new('F', 64), OriginReceiptHash = new('1', 64), ManifestFileSha256 = new('2', 64),
            ArchiveIdentity = new() { Device = 9007199254740993UL, Inode = ulong.MaxValue },
            ArchiveBytes = FileTreeUnpackingPolicy.MaximumArchiveBytes, ArchiveSha256 = new('3', 64), ContentSha256 = new('4', 64),
            EntryCount = FileTreeCopyPolicy.MaximumEntries, TotalBytes = FileTreeCopyPolicy.MaximumBytes, Fingerprint = new('5', 64) };
        if (scope != FileTreePurgeScope.Archive)
        {
            plan.SourceParent = new() { Device = ulong.MaxValue, Inode = 9007199254740993UL };
            plan.RetainedIdentity = new() { Device = 9007199254740993UL, Inode = ulong.MaxValue };
            plan.StagingIdentity = new() { Device = 42, Inode = ulong.MaxValue };
            plan.RetainedTreeSha256 = new('6', 64);
        }
        return plan;
    }

    private static void Invalid(Action action)
    {
        var error = Record.Exception(action);
        Assert.NotNull(error);
        Assert.True(error is ArgumentException or InvalidOperationException or FormatException or OverflowException,
            "Malformed JSON must be rejected by contract validation, not a null reference or unrelated exception.");
    }

    [Theory]
    [InlineData(FileTreePurgeScope.Archive, "Archive")][InlineData(FileTreePurgeScope.Originals, "Originals")][InlineData(FileTreePurgeScope.All, "All")]
    public void SixFieldCommandPreservesCaptureAuthorityAndEachExplicitScope(FileTreePurgeScope scope, string text)
    {
        foreach (var kind in new[] { "Trash", "Overwrite" })
        {
            var command = Command(scope); command.CaptureKind = kind;
            using var value = JsonDocument.Parse(FileTreePurgingPolicy.CommandJson(command));
            var root = value.RootElement;
            Assert.Equal(6, root.EnumerateObject().Count());
            Assert.Equal(text, root.GetProperty("scope").GetString()); Assert.Equal(text, FileTreePurgingPolicy.Scope(scope));
            Assert.Equal(Capture, root.GetProperty("captureId").GetString()); Assert.Equal(kind, root.GetProperty("captureKind").GetString());
            Assert.Equal(command.CaptureOwner, root.GetProperty("captureOwner").GetString());
            Assert.Equal(3, root.GetProperty("source").EnumerateObject().Count());
            Assert.Equal(command.ExpectedVersion, root.GetProperty("source").GetProperty("expectedVersion").GetString());
            Assert.False(root.TryGetProperty("hostPath", out _));
        }
    }

    [Theory]
    [InlineData(FileTreePurgeScope.Archive)][InlineData(FileTreePurgeScope.Originals)][InlineData(FileTreePurgeScope.All)]
    public void EighteenFieldPlanRoundTripsCompleteUnsignedIdentitiesAndOptionalOriginalProof(FileTreePurgeScope scope)
    {
        var plan = Plan(scope); var value = FileTreePurgingPolicy.PlanInput(plan);
        var decoded = FileTreePurgingPolicy.Plan(value, Command(scope));
        Assert.Equal(18, value.EnumerateObject().Count()); Assert.Equal(plan, decoded);
        Assert.Equal(ulong.MaxValue, value.GetProperty("storeIdentity")[0].GetUInt64());
        Assert.Equal(9007199254740993UL, value.GetProperty("storeIdentity")[1].GetUInt64());
        Assert.Equal(9007199254740993UL, value.GetProperty("archiveIdentity")[0].GetUInt64());
        Assert.Equal(ulong.MaxValue, value.GetProperty("archiveIdentity")[1].GetUInt64());
        Assert.Equal(scope != FileTreePurgeScope.Archive, decoded.HasRetainedTreeSha256);
        foreach (var field in new[] { "sourceParent", "retainedIdentity", "stagingIdentity", "retainedTreeSha256" })
            Assert.Equal(scope == FileTreePurgeScope.Archive, value.GetProperty(field).ValueKind == JsonValueKind.Null);
        if (scope != FileTreePurgeScope.Archive)
        {
            Assert.Equal(ulong.MaxValue, decoded.SourceParent.Device);
            Assert.Equal(ulong.MaxValue, decoded.RetainedIdentity.Inode);
            Assert.Equal(ulong.MaxValue, decoded.StagingIdentity.Inode);
        }
        Assert.False(value.TryGetProperty("entries", out _));
        Assert.Equal(1024L * 1024 * 1024, FileTransferPolicy.MaximumBytes);
    }

    [Theory]
    [InlineData("sourceParent")][InlineData("retainedIdentity")][InlineData("stagingIdentity")][InlineData("retainedTreeSha256")]
    public void ArchiveOnlyRequiresFourExplicitNullsAndNoOptionalProtoPresence(string field)
    {
        var plan = Plan(FileTreePurgeScope.Archive);
        if (field == "sourceParent") plan.SourceParent = new() { Inode = 1 };
        else if (field == "retainedIdentity") plan.RetainedIdentity = new() { Inode = 1 };
        else if (field == "stagingIdentity") plan.StagingIdentity = new() { Inode = 1 };
        else plan.RetainedTreeSha256 = ""; // Empty optional string is still present and must not authorize originals.
        Assert.Throws<ArgumentException>(() => FileTreePurgingPolicy.PlanInput(plan));
        var node = JsonNode.Parse(FileTreePurgingPolicy.PlanInput(Plan(FileTreePurgeScope.Archive)).GetRawText())!.AsObject();
        node.Remove(field);
        Assert.Throws<InvalidOperationException>(() => FileTreePurgingPolicy.Plan(JsonSerializer.SerializeToElement(node), Command(FileTreePurgeScope.Archive)));
    }

    [Theory]
    [InlineData(FileTreePurgeScope.Originals)][InlineData(FileTreePurgeScope.All)]
    public void OriginalsScopesCannotLoseAnyPrivateIdentityOrRetainedTreeProof(FileTreePurgeScope scope)
    {
        foreach (var field in new[] { "sourceParent", "retainedIdentity", "stagingIdentity", "retainedTreeSha256" })
        {
            var node = JsonNode.Parse(FileTreePurgingPolicy.PlanInput(Plan(scope)).GetRawText())!;
            node[field] = null;
            Assert.Throws<ArgumentException>(() => FileTreePurgingPolicy.Plan(JsonSerializer.SerializeToElement(node), Command(scope)));
        }
        var plan = Plan(scope); plan.ClearRetainedTreeSha256();
        Assert.Throws<ArgumentException>(() => FileTreePurgingPolicy.PlanInput(plan));
    }

    [Theory]
    [InlineData(FileTreePurgeScope.Unspecified)][InlineData((FileTreePurgeScope)99)]
    public void UnspecifiedAndUnknownScopesCannotBecomeImplicitAll(FileTreePurgeScope scope)
    {
        Assert.Throws<ArgumentException>(() => FileTreePurgingPolicy.CommandJson(Command(scope)));
        Assert.Throws<ArgumentException>(() => FileTreePurgingPolicy.Scope(scope));
    }

    [Theory]
    [InlineData("")][InlineData("/etc")][InlineData("../escape")][InlineData("a//b")][InlineData("a/../b")]
    public void CommandSourceMustRemainWithinRegisteredNonRootRelativePaths(string path)
    {
        var command = Command(); command.Source.Path = path;
        Assert.Throws<ArgumentException>(() => FileTreePurgingPolicy.CommandJson(command));
    }

    [Fact]
    public void CommandRejectsMalformedResourceAuthorityAndNullPlanBeforeAnyPrivilegedWork()
    {
        foreach (var property in new[] { "ExpectedVersion", "CaptureId", "CaptureOwner", "ManifestSha256", "CaptureKind" })
        {
            var command = Command(); typeof(FileTreePurgingCommand).GetProperty(property)!.SetValue(command, "invalid");
            Assert.Throws<ArgumentException>(() => FileTreePurgingPolicy.CommandJson(command));
        }
        Assert.Throws<ArgumentException>(() => FileTreePurgingPolicy.CommandJson(null));
        Assert.Throws<ArgumentException>(() => FileTreePurgingPolicy.PlanInput(null));
    }

    [Theory]
    [InlineData("source")][InlineData("version")][InlineData("captureId")][InlineData("captureOwner")]
    [InlineData("captureKind")][InlineData("manifestSha256")][InlineData("scope")]
    public void HelperPlanCannotSubstituteAnyCaptureSourceAuthorityOrSelectedComponent(string field)
    {
        var expected = Command();
        switch (field)
        {
            case "source": expected.Source.Path = "other"; break;
            case "version": expected.ExpectedVersion = new('8', 64); break;
            case "captureId": expected.CaptureId = Operation; break;
            case "captureOwner": expected.CaptureOwner = new('8', 64); break;
            case "captureKind": expected.CaptureKind = "Overwrite"; break;
            case "manifestSha256": expected.ManifestSha256 = new('8', 64); break;
            case "scope": expected.Scope = FileTreePurgeScope.Archive; break;
        }
        Assert.Throws<InvalidOperationException>(() => FileTreePurgingPolicy.Plan(FileTreePurgingPolicy.PlanInput(Plan()), expected));
    }

    [Theory]
    [InlineData("")][InlineData("command")][InlineData("source")]
    public void PlanRejectsExtraMissingAndDuplicateFieldsAtEveryObjectLevel(string level)
    {
        var original = FileTreePurgingPolicy.PlanInput(Plan());
        var node = JsonNode.Parse(original.GetRawText())!;
        var parent = level switch { "" => node, "command" => node["command"]!, _ => node["command"]!["source"]! };
        parent["hostPath"] = "/etc";
        Assert.Throws<InvalidOperationException>(() => FileTreePurgingPolicy.Plan(JsonSerializer.SerializeToElement(node), Command()));
        parent.AsObject().Remove("hostPath"); parent.AsObject().Remove(parent.AsObject().First().Key);
        Assert.Throws<InvalidOperationException>(() => FileTreePurgingPolicy.Plan(JsonSerializer.SerializeToElement(node), Command()));
        var field = level switch { "" => "fingerprint", "command" => "captureId", _ => "rootId" };
        using var duplicate = JsonDocument.Parse(original.GetRawText().Replace("\"" + field + "\":", "\"" + field + "\":null,\"" + field + "\":", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => FileTreePurgingPolicy.Plan(duplicate.RootElement, Command()));
    }

    [Theory]
    [InlineData("sourcePolicyHash")][InlineData("storageHash")][InlineData("captureMetadataHash")][InlineData("originReceiptHash")]
    [InlineData("manifestFileSha256")][InlineData("archiveSha256")][InlineData("contentSha256")][InlineData("retainedTreeSha256")][InlineData("fingerprint")]
    public void EveryFrozenProofHashIsRequiredAndValidated(string field)
    {
        var node = JsonNode.Parse(FileTreePurgingPolicy.PlanInput(Plan()).GetRawText())!; node[field] = "bad";
        Assert.Throws<ArgumentException>(() => FileTreePurgingPolicy.Plan(JsonSerializer.SerializeToElement(node), Command()));
    }

    [Theory]
    [InlineData("storeIdentity")][InlineData("archiveIdentity")][InlineData("sourceParent")][InlineData("retainedIdentity")][InlineData("stagingIdentity")]
    public void IdentitiesRejectWrongShapeSignedFractionalOrOutOfRangeValues(string field)
    {
        foreach (var invalid in new[] { "[]", "[1]", "[1,2,3]", "[-1,2]", "[1,18446744073709551616]", "[1,1.5]", "[1,\"2\"]", "[1,0]", "{}", "true" })
        {
            var node = JsonNode.Parse(FileTreePurgingPolicy.PlanInput(Plan()).GetRawText())!; node[field] = JsonNode.Parse(invalid);
            Invalid(() => FileTreePurgingPolicy.Plan(JsonSerializer.SerializeToElement(node), Command()));
        }
    }

    [Theory]
    [InlineData("archiveBytes")][InlineData("entryCount")][InlineData("totalBytes")][InlineData("sourcePolicyHash")][InlineData("retainedTreeSha256")]
    public void PlanScalarTypesCannotBeCoercedFromStringsNumbersBooleansOrNull(string field)
    {
        foreach (var invalid in new[] { "null", "true", "{}", "[]", "\"12\"", "1.5" })
        {
            var node = JsonNode.Parse(FileTreePurgingPolicy.PlanInput(Plan()).GetRawText())!; node[field] = JsonNode.Parse(invalid);
            Invalid(() => FileTreePurgingPolicy.Plan(JsonSerializer.SerializeToElement(node), Command()));
        }
    }

    [Fact]
    public void WholeTreeAndArchiveBoundsRejectOverflowAndNegativeValues()
    {
        var plan = Plan(); FileTreePurgingPolicy.PlanInput(plan);
        foreach (var field in new[] { "archiveBytes", "entryCount", "totalBytes" })
        {
            foreach (var invalid in new[] { -1L, field == "archiveBytes" ? FileTreeUnpackingPolicy.MaximumArchiveBytes + 1 :
                field == "totalBytes" ? FileTreeCopyPolicy.MaximumBytes + 1 : FileTreeCopyPolicy.MaximumEntries + 1L })
            {
                var node = JsonNode.Parse(FileTreePurgingPolicy.PlanInput(plan).GetRawText())!; node[field] = invalid;
                Invalid(() => FileTreePurgingPolicy.Plan(JsonSerializer.SerializeToElement(node), Command()));
            }
        }
        plan.ArchiveBytes = 0; Assert.Throws<ArgumentException>(() => FileTreePurgingPolicy.PlanInput(plan));
        plan = Plan(); plan.EntryCount = 0; Assert.Throws<ArgumentException>(() => FileTreePurgingPolicy.PlanInput(plan));
    }

    private static JsonNode Receipt(FileTreePurgeScope scope, string state, bool started, bool archive, bool originals) => JsonSerializer.SerializeToNode(new {
        operationId = Operation, kind = "DirectoryPurge", state, fingerprint = new string('5', 64), captureId = Capture,
        scope = FileTreePurgingPolicy.Scope(scope), entryCount = FileTreeCopyPolicy.MaximumEntries,
        totalBytes = FileTreeCopyPolicy.MaximumBytes, archiveBytes = FileTreeUnpackingPolicy.MaximumArchiveBytes,
        deletionStarted = started, archiveRemoved = archive, originalsRemoved = originals })!;

    [Theory]
    [InlineData(FileTreePurgeScope.Archive, "Completed", true, true, false)]
    [InlineData(FileTreePurgeScope.Originals, "Completed", true, false, true)]
    [InlineData(FileTreePurgeScope.All, "Completed", true, true, true)]
    [InlineData(FileTreePurgeScope.Archive, "Aborted", false, false, false)]
    [InlineData(FileTreePurgeScope.Originals, "Aborted", false, false, false)]
    [InlineData(FileTreePurgeScope.All, "Aborted", false, false, false)]
    [InlineData(FileTreePurgeScope.Archive, "Unknown", true, false, false)]
    [InlineData(FileTreePurgeScope.Originals, "Unknown", true, false, false)]
    [InlineData(FileTreePurgeScope.All, "Unknown", true, false, false)]
    [InlineData(FileTreePurgeScope.All, "Unknown", true, true, false)]
    [InlineData(FileTreePurgeScope.All, "Unknown", true, false, true)]
    [InlineData(FileTreePurgeScope.All, "Unknown", true, true, true)]
    public void TwelveFieldReceiptKeepsSelectedComponentEffectsAndUncertaintySeparate(FileTreePurgeScope scope, string state, bool started, bool archive, bool originals)
    {
        var node = Receipt(scope, state, started, archive, originals);
        Assert.Equal(12, node.AsObject().Count);
        var result = FileTreePurgingPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation, Plan(scope));
        Assert.Equal(state, result.State); Assert.Equal(scope, result.Scope); Assert.Equal(Capture, result.CaptureId);
        Assert.NotEqual(result.OperationId, result.CaptureId); Assert.Equal(started, result.DeletionStarted);
        Assert.Equal(archive, result.ArchiveRemoved); Assert.Equal(originals, result.OriginalsRemoved);
    }

    [Theory]
    [InlineData(FileTreePurgeScope.Archive, false, true)]
    [InlineData(FileTreePurgeScope.Originals, true, false)]
    public void ReceiptCannotClaimDeletionOfAnUnselectedComponent(FileTreePurgeScope scope, bool archive, bool originals)
    {
        Invalid(() => FileTreePurgingPolicy.Receipt(JsonSerializer.SerializeToElement(Receipt(scope, "Unknown", true, archive, originals)), Operation));
    }

    [Theory]
    [InlineData("Aborted", true, false, false)][InlineData("Aborted", false, true, false)]
    [InlineData("Unknown", false, false, false)][InlineData("Unknown", false, false, true)]
    [InlineData("Completed", false, true, true)][InlineData("Completed", true, true, false)][InlineData("Completed", true, false, true)]
    public void StateAndDeletionFactsCannotContradictEachOther(string state, bool started, bool archive, bool originals)
    {
        Assert.Throws<InvalidOperationException>(() => FileTreePurgingPolicy.Receipt(JsonSerializer.SerializeToElement(Receipt(FileTreePurgeScope.All, state, started, archive, originals)), Operation));
    }

    [Theory]
    [InlineData("fingerprint")][InlineData("captureId")][InlineData("scope")][InlineData("entryCount")][InlineData("totalBytes")][InlineData("archiveBytes")]
    public void ReceiptMustMatchTheFrozenCaptureScopeAndCounts(string field)
    {
        var node = Receipt(FileTreePurgeScope.All, "Aborted", false, false, false);
        if (field == "scope") node[field] = "Archive";
        else if (field is "entryCount" or "totalBytes" or "archiveBytes") node[field] = 1;
        else node[field] = field == "captureId" ? "a25c9e3d2e24476a9c8e0899f93a65fb" : new string('8', 64);
        Assert.Throws<InvalidOperationException>(() => FileTreePurgingPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation, Plan()));
    }

    [Fact]
    public void ReceiptRejectsExtraMissingDuplicateOrForeignOperationFields()
    {
        var original = Receipt(FileTreePurgeScope.All, "Completed", true, true, true);
        var node = original.DeepClone(); node["hostPath"] = "/private";
        Assert.Throws<InvalidOperationException>(() => FileTreePurgingPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation));
        node = original.DeepClone(); node.AsObject().Remove("deletionStarted");
        Assert.Throws<InvalidOperationException>(() => FileTreePurgingPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation));
        using var duplicate = JsonDocument.Parse(original.ToJsonString().Replace("\"scope\":", "\"scope\":\"All\",\"scope\":", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => FileTreePurgingPolicy.Receipt(duplicate.RootElement, Operation));
        Assert.Throws<InvalidOperationException>(() => FileTreePurgingPolicy.Receipt(JsonSerializer.SerializeToElement(original), Capture));
        node = original.DeepClone(); node["operationId"] = Capture;
        Assert.Throws<InvalidOperationException>(() => FileTreePurgingPolicy.Receipt(JsonSerializer.SerializeToElement(node), Capture));
    }

    [Theory]
    [InlineData("scope", "\"all\"")][InlineData("scope", "1")][InlineData("kind", "\"DirectoryRestore\"")]
    [InlineData("state", "\"Running\"")][InlineData("state", "\"completed\"")]
    [InlineData("deletionStarted", "\"true\"")][InlineData("archiveRemoved", "1")][InlineData("originalsRemoved", "null")]
    [InlineData("entryCount", "\"10000\"")][InlineData("totalBytes", "true")][InlineData("archiveBytes", "-1")]
    public void ReceiptWrongValuesOrJsonTypesCannotBeCoerced(string field, string invalid)
    {
        var node = Receipt(FileTreePurgeScope.All, "Completed", true, true, true); node[field] = JsonNode.Parse(invalid);
        Invalid(() => FileTreePurgingPolicy.Receipt(JsonSerializer.SerializeToElement(node), Operation));
    }

    [Fact]
    public async Task InvalidRequestsStopBeforeRunnerAndPurgeCapabilityIsIndependent()
    {
        var service = new FileTreePurgingGrpcService(new FileTransferRunner());
        var missing = await Assert.ThrowsAsync<RpcException>(() => service.Plan(new() { Command = Command() }, null!));
        Assert.Equal("FILES_ACTOR_REQUIRED", missing.Status.Detail);
        var owner = await Assert.ThrowsAsync<RpcException>(() => service.Inspect(new() { OperationId = Operation, Owner = "bad" }, null!));
        Assert.Equal("FILES_HASH_INVALID", owner.Status.Detail);
        var operation = await Assert.ThrowsAsync<RpcException>(() => service.Apply(new() { Context = new() { ActorSubject = "admin" }, OperationId = "bad" }, null!));
        Assert.Equal("FILES_OPERATION_INVALID", operation.Status.Detail);
        var discard = await Assert.ThrowsAsync<RpcException>(() => service.Discard(new() { OperationId = Operation, Owner = new('A', 64), Plan = Plan() }, null!));
        Assert.Equal("FILES_ACTOR_REQUIRED", discard.Status.Detail);
        var sameCapture = await Assert.ThrowsAsync<RpcException>(() => service.Apply(new() { Context = new() { ActorSubject = "admin" },
            OperationId = Capture, Owner = new('A', 64), Plan = Plan() }, null!));
        Assert.Equal("FILES_TREE_PURGE_INVALID", sameCapture.Status.Detail);
        var invalid = Command(); invalid.CaptureId = "bad";
        var capture = await Assert.ThrowsAsync<RpcException>(() => service.Plan(new() { Context = new() { ActorSubject = "admin" }, Command = invalid }, null!));
        Assert.Equal("FILES_OPERATION_INVALID", capture.Status.Detail);
        var capabilities = await new AgentGrpcService(null!).GetCapabilities(new(), null!);
        Assert.Equal(36U, capabilities.ProtocolMinor); Assert.Contains("files.tree-purge-intents", capabilities.Capabilities);
        Assert.Equal("whaledeck.agent.v1.FileTreePurgingService", FileTreePurgingService.Descriptor.FullName);
    }
}
