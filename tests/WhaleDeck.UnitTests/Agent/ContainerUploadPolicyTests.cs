using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerUploadPolicyTests
{
    private static readonly string Id = new('a', 64);
    private const string Instance = "777:2026-10-22T01:02:03.123456789Z";
    private static readonly string Hash = new('A', 64);
    private static ContainerUploadHeader Header() => new()
    {
        Location = new() { ContainerId = Id, InstanceId = Instance, Path = "目录/a.bin" },
        OperationId = "aa0a8865343e49bdb01a72680131bf39", Owner = Hash, SizeBytes = 0,
        Sha256 = Convert.ToHexString(SHA256.HashData([])), ExpectedVersion = "ABSENT", ExpectedParentVersion = Hash,
        ExpectedTargetProof = Hash, Context = new() { ActorSubject = "fixture", TraceId = "fixture-trace",
            JobId = "bb0a8865343e49bdb01a72680131bf39", IdempotencyKey = "cc0a8865343e49bdb01a72680131bf39" }
    };
    private static ContainerUploadReference Reference() => new() { ContainerId = Id, InstanceId = Instance,
        OperationId = Header().OperationId, Owner = Hash, ExpectedTargetProof = Hash, Context = Header().Context };

    [Fact]
    public void HeaderProjectsOnlyFixedRootFieldsAndKeepsRawInstanceAndUnicode()
    {
        using var json = JsonDocument.Parse(ContainerUploadPolicy.Open(new() { Header = Header() }));
        Assert.Equal(10, json.RootElement.EnumerateObject().Count());
        Assert.Equal(Instance, json.RootElement.GetProperty("instanceId").GetString());
        Assert.Equal("目录/a.bin", json.RootElement.GetProperty("path").GetString());
        Assert.Equal(Hash, json.RootElement.GetProperty("expectedTargetProof").GetString());
        Assert.False(json.RootElement.TryGetProperty("context", out _));
        Assert.False(json.RootElement.TryGetProperty("rootId", out _));
    }

    [Theory]
    [InlineData("data")][InlineData("offset")][InlineData("commit")][InlineData("location")][InlineData("context")]
    [InlineData("capability")][InlineData("job")][InlineData("idempotency")][InlineData("proof")][InlineData("parent")]
    [InlineData("size")][InlineData("operation")][InlineData("owner")]
    public void InvalidHeaderCannotReachHelper(string variant)
    {
        var frame = new ContainerUploadFrame { Header = Header() };
        switch (variant)
        {
            case "data": frame.Data = ByteString.CopyFrom([1]); break;
            case "offset": frame.Offset = 1; break;
            case "commit": frame.Commit = true; break;
            case "location": frame.Header.Location = null; break;
            case "context": frame.Header.Context = null; break;
            case "capability": frame.Header.Context.Capability = ByteString.CopyFrom([1]); break;
            case "job": frame.Header.Context.JobId = ""; break;
            case "idempotency": frame.Header.Context.IdempotencyKey = ""; break;
            case "proof": frame.Header.ExpectedTargetProof = ""; break;
            case "parent": frame.Header.ExpectedParentVersion = ""; break;
            case "size": frame.Header.SizeBytes = 1073741825; break;
            case "operation": frame.Header.OperationId = Guid.Empty.ToString("N"); break;
            case "owner": frame.Header.Owner = new('a', 64); break;
        }
        Assert.Throws<ArgumentException>(() => ContainerUploadPolicy.Open(frame));
    }

    [Theory]
    [InlineData("header")][InlineData("empty")][InlineData("offset")][InlineData("overflow")][InlineData("large")][InlineData("early-commit")]
    public void InvalidChunkOrEarlyCommitCannotAdvanceReceiveOffset(string variant)
    {
        var frame = new ContainerUploadFrame { Data = ByteString.CopyFrom([42]) };
        switch (variant)
        {
            case "header": frame.Header = Header(); break;
            case "empty": frame.Data = ByteString.Empty; break;
            case "offset": frame.Offset = -1; break;
            case "overflow": frame.Data = ByteString.CopyFrom([42, 43]); break;
            case "large": frame.Data = ByteString.CopyFrom(new byte[32769]); break;
            case "early-commit": frame.Data = ByteString.Empty; frame.Commit = true; break;
        }
        long offset = 0;
        Assert.Throws<ArgumentException>(() => ContainerUploadPolicy.Input(frame, ref offset, 1));
        Assert.Equal(0, offset);
    }

    [Fact]
    public void EmptyAndExactSizedCommitsHaveExplicitEndFrames()
    {
        long offset = 0;
        Assert.Equal("{\"type\":\"commit\"}", ContainerUploadPolicy.Input(new() { Commit = true }, ref offset, 0));
        var chunk = ContainerUploadPolicy.Input(new() { Data = ByteString.CopyFrom([42]) }, ref offset, 1);
        using var json = JsonDocument.Parse(chunk);
        Assert.Equal(0, json.RootElement.GetProperty("offset").GetInt64());
        Assert.Equal(1, offset);
        Assert.Equal("{\"type\":\"commit\"}", ContainerUploadPolicy.Input(new() { Commit = true }, ref offset, 1));
    }

    [Theory]
    [InlineData("target")][InlineData("stage")][InlineData("publish")][InlineData("inspect")][InlineData("discard")]
    public void NewActionsUseTheirOwnFixedHelperFamilyAndExactInvocationStop(string action)
    {
        var start = typeof(FileTransferRunner).GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Static)!;
        var id = new string('a', 32); var invocation = new string('b', 32);
        var process = Assert.IsType<ProcessStartInfo>(start.Invoke(null, ["container-write:" + action, id, null]));
        Assert.Equal("/usr/bin/sudo", process.FileName); Assert.False(process.UseShellExecute);
        Assert.Equal(new[] { "-n", "/usr/local/libexec/whaledeck-privileged", "container-file-write", action, id }, process.ArgumentList);
        Assert.Equal(2, process.Environment.Count);
        Assert.Contains("PATH", process.Environment.Keys); Assert.Contains("LANG", process.Environment.Keys);
        var stop = Assert.IsType<ProcessStartInfo>(start.Invoke(null, ["container-write:" + action, id, invocation]));
        Assert.Equal(new[] { "-n", "/usr/local/libexec/whaledeck-privileged", "container-file-write-stop", id, invocation }, stop.ArgumentList);
    }

    [Fact]
    public void ValidStageReceiptIsBoundToContainerInstanceOperationAndPlannedProof()
    {
        var reference = Reference();
        var value = JsonSerializer.SerializeToElement(new { containerId = Id, instanceId = Instance, operationId = reference.OperationId,
            targetProof = Hash, state = "Staged", sizeBytes = 0, sha256 = Header().Sha256, replayed = false });
        var receipt = ContainerUploadPolicy.Receipt(value, reference, Header());
        Assert.Equal("Staged", receipt.Upload.State); Assert.Equal(Hash, receipt.TargetProof); Assert.Null(receipt.Backup);
        Assert.False(receipt.Upload.BindingCurrent);
        var wrong = reference.Clone(); wrong.ExpectedTargetProof = new('F', 64);
        Assert.Throws<InvalidOperationException>(() => ContainerUploadPolicy.Receipt(value, wrong));
    }

    [Fact]
    public void PublishedReceiptPreservesContainerBoundHddOriginalProof()
    {
        var reference = Reference();
        var value = JsonSerializer.SerializeToElement(new { containerId = Id, instanceId = Instance, operationId = reference.OperationId,
            targetProof = Hash, state = "Published", sizeBytes = 2097159, sha256 = Hash, version = new string('B', 64),
            previousRetained = true, bindingCurrent = true, replayed = true,
            backupCapture = new { operationId = reference.OperationId, storageId = "database-platform.backup.hdd", sourceVersion = new string('C', 64),
                sizeBytes = 2097159, sha256 = new string('D', 64), createdAtUnixMs = 1792990471366L, differentSourceDevice = false,
                resourceType = "ContainerFile", containerId = Id, instanceId = Instance, targetProof = Hash } });
        var receipt = ContainerUploadPolicy.Receipt(value, reference);
        Assert.Equal("Published", receipt.Upload.State); Assert.True(receipt.Upload.Replayed);
        Assert.True(receipt.Upload.PreviousRetained); Assert.True(receipt.Upload.BindingCurrent);
        Assert.NotNull(receipt.Backup); Assert.Null(receipt.Upload.HddBackup);
        Assert.Equal(reference.OperationId, receipt.Backup.Proof.OperationId);
        Assert.Equal(new string('C', 64), receipt.Backup.Proof.SourceVersion);
        Assert.Equal(2097159, receipt.Backup.Proof.SizeBytes);
    }

    [Theory]
    [InlineData("state")][InlineData("hash")][InlineData("published")][InlineData("binding")][InlineData("size")]
    public void UnsafeHelperReceiptsNeverBecomeSuccess(string variant)
    {
        var reference = Reference();
        var value = JsonSerializer.SerializeToElement(new { containerId = Id, instanceId = Instance, operationId = reference.OperationId,
            targetProof = Hash, state = variant == "state" ? "Succeeded" : variant == "published" ? "Published" : "Staged",
            sizeBytes = variant == "size" ? -1 : 0, sha256 = variant == "hash" ? "private text" : Header().Sha256,
            bindingCurrent = variant == "binding", replayed = false });
        var error = Assert.Throws<InvalidOperationException>(() => ContainerUploadPolicy.Receipt(value, reference));
        Assert.Equal("CONTAINER_UPLOAD_OUTPUT_INVALID", error.Message);
    }

    [Theory]
    [InlineData("CONTAINER_UPLOAD_NOT_FOUND", StatusCode.NotFound)]
    [InlineData("CONTAINER_UPLOAD_SCOPE_MISMATCH", StatusCode.PermissionDenied)]
    [InlineData("CONTAINER_UPLOAD_VERSION_CONFLICT", StatusCode.Aborted)]
    [InlineData("FILES_BACKUP_MOUNT_MISMATCH", StatusCode.FailedPrecondition)]
    [InlineData("FILES_BACKUP_INCOMPLETE", StatusCode.FailedPrecondition)]
    [InlineData("CONTAINER_UPLOAD_BUSY", StatusCode.ResourceExhausted)]
    [InlineData("CONTAINER_UPLOAD_PUBLISH_TIMEOUT", StatusCode.DeadlineExceeded)]
    public void KnownFixedFailuresKeepTypedSemantics(string code, StatusCode status) => Assert.Equal(status, ContainerUploadPolicy.Failure(code).StatusCode);

    [Fact]
    public void UnknownErrorNeverLeaksPrivateDiagnostics()
    {
        var error = ContainerUploadPolicy.Failure("password /host/private/key");
        Assert.Equal(StatusCode.Unavailable, error.StatusCode); Assert.Equal("CONTAINER_UPLOAD_UNAVAILABLE", error.Status.Detail);
    }
}
