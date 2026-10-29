using System.Reflection;
using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerMovePolicyTests
{
    internal static ContainerMovePlan Plan(bool directory = false)
    {
        var plan = new ContainerMovePlan { SchemaVersion = 1, OperationKind = "Move", Kind = directory ? "Directory" : "File",
            Command = new() { Source = new() { ContainerId = new('a', 64), InstanceId = "777:2026-10-28T01:02:03.123456789Z", Path = "work/中文源" },
                TargetPath = "work/中文副本", ExpectedVersion = new('A', 64) }, TargetProof = new('B', 64), TargetParentVersion = new('C', 64), Fingerprint = new('D', 64),
            SourceIdentity = new() { Device = ulong.MaxValue, Inode = 101, Mode = directory ? 16872U : 33184U, OwnerUid = 1000, OwnerGid = 1001,
                LinkCount = directory ? 2U : 1U, SizeBytes = directory ? 4096 : 7, ModifiedAtUnixNs = 1700000000123456789 }, SourceChangedAtUnixNs = 1700000000987654321,
            SourceParent = new() { Device = 22, Inode = 33 }, TargetParent = new() { Device = 44, Inode = 55 }, OwnerUid = 1000, OwnerGid = 1001 };
        if (directory) plan.TreePlan = new() { SourceParent = plan.SourceParent.Clone(), Fingerprint = new('C', 64), CopyPlan = new() { Command = new() { Source = new() { RootId = ContainerMovePolicy.SourceRoot, Path = plan.Command.Source.Path },
                Target = new() { RootId = ContainerMovePolicy.TargetRoot, Path = plan.Command.TargetPath }, ExpectedVersion = ContainerMovePolicy.HostVersion(plan) },
            TargetParent = plan.TargetParent.Clone(), EntryCount = 3, TotalBytes = 7, ManifestSha256 = new('E', 64), SourcePolicyHash = new('F', 64), TargetPolicyHash = new('A', 64), Fingerprint = new('B', 64) } };
        else plan.FilePlan = new() { Command = new() { Kind = FileOperationKind.MoveFile, Source = new() { RootId = ContainerMovePolicy.SourceRoot, Path = plan.Command.Source.Path },
                Target = new() { RootId = ContainerMovePolicy.TargetRoot, Path = plan.Command.TargetPath }, ExpectedVersion = ContainerMovePolicy.HostVersion(plan) },
            SourceIdentity = plan.SourceIdentity.Clone(), SourceParent = plan.SourceParent.Clone(), TargetParent = plan.TargetParent.Clone(), SizeBytes = 7, Sha256 = new('E', 64), Fingerprint = new('F', 64) };
        return plan;
    }
    internal static ContainerMoveRequest Request() => new() { OperationId = "aa0a8865343e49bdb01a72680131bf39", Owner = new('B', 64), Plan = Plan(),
        Context = new() { ActorSubject = "operator", TraceId = "move-test", JobId = "aa0a8865343e49bdb01a72680131bf39", IdempotencyKey = "bb0a8865343e49bdb01a72680131bf39" } };

    [Theory][InlineData(false)][InlineData(true)]
    public void TypedPlansRoundTripCompleteIdentitiesWithoutUnsignedOrNanosecondLoss(bool directory)
    {
        var plan = Plan(directory); var input = ContainerMovePolicy.PlanInput(plan);
        Assert.Equal(plan, ContainerMovePolicy.PlanOutput(input, plan.Command));
        Assert.Equal(ulong.MaxValue, input.GetProperty("sourceIdentity")[0].GetUInt64());
        Assert.Equal(1700000000987654321, input.GetProperty("sourceIdentity")[8].GetInt64());
    }
    [Theory]
    [InlineData("schema")][InlineData("kind")][InlineData("mode")][InlineData("uid")][InlineData("parent")][InlineData("root")]
    [InlineData("copy")][InlineData("ctime")][InlineData("source")][InlineData("target")][InlineData("sourceIdentity")][InlineData("innerKind")]
    public void PlanCannotGrantOtherOperationsRootsOwnersOrDestinations(string field)
    {
        var plan = Plan();
        switch (field)
        {
            case "schema": plan.SchemaVersion = 2; break;
            case "kind": plan.Kind = "Move"; break;
            case "mode": plan.SourceIdentity.Mode |= 0x800; break;
            case "uid": plan.OwnerUid = uint.MaxValue; break;
            case "parent": plan.TargetParent.Inode++; break;
            case "root": plan.FilePlan.Command.Target.RootId = "apps"; break;
            case "copy": plan.FilePlan.Command.Kind = FileOperationKind.CopyFile; break;
            case "ctime": plan.SourceChangedAtUnixNs++; break;
            case "source": plan.Command.Source.Path = "work/different"; break;
            case "target": plan.Command.TargetPath = "work/different"; break;
            case "sourceIdentity": plan.SourceIdentity.OwnerUid++; break;
            case "innerKind": plan.TreePlan = Plan(true).TreePlan; break;
        }
        Assert.Throws<ArgumentException>(() => ContainerMovePolicy.Plan(plan));
    }
    [Theory][InlineData("../escape")][InlineData("/etc")][InlineData("work//bad")][InlineData("")][InlineData("work/中文源")][InlineData("work/中文源/child")]
    public void InvalidOrOverlappingTargetPathsAreRejected(string path)
    { var command = Plan().Command; command.TargetPath = path; Assert.Throws<ArgumentException>(() => ContainerMovePolicy.Command(command)); }
    [Fact]
    public void ExtraFieldsDuplicateFieldsAndChangedCommandInRootOutputAreRejected()
    {
        var plan = Plan(); var json = ContainerMovePolicy.PlanInput(plan).GetRawText();
        using var duplicate = JsonDocument.Parse(json[..^1] + ",\"fingerprint\":\"" + new string('D', 64) + "\"}");
        Assert.Throws<InvalidOperationException>(() => ContainerMovePolicy.PlanOutput(duplicate.RootElement, plan.Command));
        using var extra = JsonDocument.Parse(json[..^1] + ",\"hostPath\":\"/etc\"}");
        Assert.Throws<InvalidOperationException>(() => ContainerMovePolicy.PlanOutput(extra.RootElement, plan.Command));
        var changed = plan.Command.Clone(); changed.TargetPath = "other";
        Assert.Throws<InvalidOperationException>(() => ContainerMovePolicy.PlanOutput(ContainerMovePolicy.PlanInput(plan), changed));
    }
    [Fact]
    public void ContextHasNoEmbeddedAuthorityAndKeepsOriginalJobIdentity()
    {
        var request = Request(); ContainerMovePolicy.Request(request); request.Context.JobId = request.Context.IdempotencyKey;
        Assert.Throws<ArgumentException>(() => ContainerMovePolicy.Request(request));
        request = Request(); request.Context.Capability = Google.Protobuf.ByteString.CopyFromUtf8("not-in-context");
        Assert.Throws<ArgumentException>(() => ContainerMovePolicy.Request(request));
        Assert.Throws<ArgumentException>(() => ContainerMovePolicy.Context(Request().Context, planning: true));
    }
    [Theory][InlineData("Completed", true, false)][InlineData("Aborted", false, true)][InlineData("Unknown", false, true)]
    public void ReceiptKeepsBoundHistoricalResultAndHonestPrivateUncertainty(string state, bool binding, bool mayExist)
    {
        var request = Request(); var plan = request.Plan;
        var node = JsonSerializer.SerializeToNode(new { operationId = request.OperationId, containerId = plan.Command.Source.ContainerId,
            instanceId = plan.Command.Source.InstanceId, operationKind = "Move", kind = plan.Kind, fingerprint = plan.Fingerprint, targetProof = plan.TargetProof,
            state, version = state == "Completed" ? new string('E', 64) : "", bindingCurrent = binding, privateMayExist = mayExist, replayed = true, destinationPublished = state == "Unknown" ? (bool?)null : state == "Completed",
            sourceRemoved = state == "Unknown" ? (bool?)null : state == "Completed", sourceRetained = state == "Unknown" ? (bool?)null : state == "Completed" })!;
        using var good = JsonDocument.Parse(node.ToJsonString()); Assert.Equal(state, ContainerMovePolicy.Receipt(good.RootElement, request).State);
        node["kind"] = "Directory"; using var wrong = JsonDocument.Parse(node.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => ContainerMovePolicy.Receipt(wrong.RootElement, request));
    }
    [Theory][InlineData(null)][InlineData(false)][InlineData(true)]
    public void UnknownFactsPreservePresenceAcrossJsonAndProtobuf(bool? fact)
    {
        var request = Request(); var p = request.Plan;
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { operationId = request.OperationId, operationKind = "Move",
            containerId = p.Command.Source.ContainerId, instanceId = p.Command.Source.InstanceId, kind = p.Kind, fingerprint = p.Fingerprint,
            targetProof = p.TargetProof, state = "Unknown", version = "", bindingCurrent = false, privateMayExist = true, replayed = true,
            destinationPublished = fact, sourceRemoved = fact, sourceRetained = fact }));
        var result = ContainerMoveReceipt.Parser.ParseFrom(Google.Protobuf.MessageExtensions.ToByteArray(ContainerMovePolicy.Receipt(document.RootElement, request)));
        Assert.Equal(fact.HasValue, result.HasDestinationPublished); Assert.Equal(fact.HasValue, result.HasSourceRemoved); Assert.Equal(fact.HasValue, result.HasSourceRetained);
        if (fact.HasValue) { Assert.Equal(fact.Value, result.DestinationPublished); Assert.Equal(fact.Value, result.SourceRemoved); Assert.Equal(fact.Value, result.SourceRetained); }
    }
    [Theory][InlineData("0")][InlineData("\"false\"")][InlineData("[]")]
    public void FactTypesCannotBeCoercedIntoFalse(string raw)
    {
        var request = Request(); var p = request.Plan;
        var node = JsonSerializer.SerializeToNode(new { operationId = request.OperationId, operationKind = "Move", containerId = p.Command.Source.ContainerId,
            instanceId = p.Command.Source.InstanceId, kind = p.Kind, fingerprint = p.Fingerprint, targetProof = p.TargetProof, state = "Unknown", version = "",
            bindingCurrent = false, privateMayExist = true, replayed = true, destinationPublished = (bool?)null, sourceRemoved = (bool?)null, sourceRetained = (bool?)null })!;
        node["sourceRetained"] = System.Text.Json.Nodes.JsonNode.Parse(raw);
        using var document = JsonDocument.Parse(node.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => ContainerMovePolicy.Receipt(document.RootElement, request));
    }
    [Fact]
    public void MoveKindAndNestedSourceParentAreRequired()
    {
        var plan = Plan(); plan.OperationKind = ""; Assert.Throws<ArgumentException>(() => ContainerMovePolicy.Plan(plan));
        plan = Plan(true); plan.TreePlan.SourceParent.Inode++; Assert.Throws<ArgumentException>(() => ContainerMovePolicy.Plan(plan));
    }
    [Theory][InlineData("plan")][InlineData("apply")][InlineData("inspect")][InlineData("discard")]
    public void ProcessArgumentsAreFixedAndStopBindsExactInvocation(string action)
    {
        var method = typeof(FileTransferRunner).GetMethod("Start", BindingFlags.Static | BindingFlags.NonPublic)!;
        const string id = "aa0a8865343e49bdb01a72680131bf39", invocation = "bb0a8865343e49bdb01a72680131bf39";
        var start = Assert.IsType<System.Diagnostics.ProcessStartInfo>(method.Invoke(null, ["container-move:" + action, id, null]));
        var stop = Assert.IsType<System.Diagnostics.ProcessStartInfo>(method.Invoke(null, ["container-move:" + action, id, invocation]));
        Assert.Equal(new[] { "-n", "/usr/local/libexec/whaledeck-privileged", "container-file-moves", action, id }, start.ArgumentList);
        Assert.Equal(new[] { "-n", "/usr/local/libexec/whaledeck-privileged", "container-file-moves-stop", id, invocation }, stop.ArgumentList);
        Assert.False(start.UseShellExecute); Assert.Equal(2, start.Environment.Count);
    }
    [Theory]
    [InlineData("CONTAINER_MOVE_NOT_FOUND", StatusCode.NotFound)][InlineData("CONTAINER_FILES_TARGET_MISSING", StatusCode.FailedPrecondition)]
    [InlineData("FILES_TREE_SIZE_LIMIT", StatusCode.ResourceExhausted)][InlineData("FILES_TREE_CANCELED", StatusCode.Cancelled)]
    [InlineData("CONTAINER_MOVE_VERSION_CONFLICT", StatusCode.Aborted)][InlineData("CONTAINER_UPLOAD_PUBLISH_READ_ONLY", StatusCode.PermissionDenied)]
    [InlineData("CONTAINER_MOVE_secret=/private", StatusCode.Unavailable)]
    public void ErrorMappingDoesNotInventNotFoundOrLeakUnknownText(string code, StatusCode status)
    {
        var error = ContainerMovePolicy.Failure(code); Assert.Equal(status, error.StatusCode);
        Assert.Equal(status == StatusCode.Unavailable ? "CONTAINER_MOVE_UNAVAILABLE" : code, error.Status.Detail);
    }
}
