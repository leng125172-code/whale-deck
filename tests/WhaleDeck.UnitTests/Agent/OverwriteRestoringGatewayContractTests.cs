using System.Reflection;
using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;
using App = WhaleDeck.Application.Models;
using Rpc = WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

/// <summary>Pure Application/RPC boundary projections; no real Agent, UDS or filesystem execution.</summary>
public sealed class OverwriteRestoringGatewayContractTests
{
    private static readonly Guid Intent = Guid.Parse("073e1d38-e73f-450a-8226-40746147f35c");
    private static readonly Guid Capture = Guid.Parse("846516d5-6a48-4ba5-9468-cd61f55728ed");
    private static Guid Execution => FileTreeOverwriteRestoringValidation.ExecutionId(Intent);

    private static App.FileTreeOverwriteRestoringCommand Command() => new(new("test.shared", "代码/原目录"), new('A', 64),
        new("test.shared", "代码/当前目录"), "tar.gz", Capture, new('B', 64), "Trash", new('C', 64), new('9', 64));

    private static App.FileTreeOverwriteRestoringPlan Plan() => new(Command(), new('1', 64),
        FileTreeOverwriteRestoringValidation.MaximumArchiveBytes, new('2', 64), 10_000,
        FileTreeOverwriteRestoringValidation.MaximumBytes, new(ulong.MaxValue, 9_007_199_254_740_993UL),
        new('D', 64), new('E', 64), new('F', 64), new('4', 64), new('5', 64), 10_000,
        FileTreeOverwriteRestoringValidation.MaximumBytes - 7, new(9_007_199_254_740_993UL, ulong.MaxValue), new('8', 64));

    private static FileTreeOverwriteRestoringOperation Row(App.FileTreeOverwriteRestoringPlan? plan = null) => new()
    {
        Id = Intent, CaptureId = Capture, ActorSubject = "synthetic-actor", LoginBinding = new('A', 64),
        IdempotencyKey = "73d78374-c834-4e3c-ab4f-b5f8114cdfd5", OwnerHash = new('B', 64), RequestHash = new('C', 64),
        SourceHash = new('D', 64), TargetHash = new('E', 64), CommandJson = JsonSerializer.Serialize(Command()),
        PlanJson = JsonSerializer.Serialize(plan ?? Plan()), ExecutionJobId = Execution
    };

    private static Rpc.FileTreeOverwriteRestoringReceipt Receipt() => new()
    {
        OperationId = Execution.ToString("N"), CaptureId = Capture.ToString("N"), OverwrittenCaptureId = Execution.ToString("N"),
        State = "Completed", Fingerprint = new('F', 64), Format = "tar.gz", EntryCount = 10_000,
        TotalBytes = FileTreeOverwriteRestoringValidation.MaximumBytes,
        ArchiveBytes = FileTreeOverwriteRestoringValidation.MaximumArchiveBytes, ArchiveSha256 = new('1', 64),
        Version = new('6', 64), Kind = "DirectoryOverwriteRestore", BackupMayBeRetained = true, BackupVerified = true,
        OriginalsRetained = true, TargetMayHaveChanged = true,
        BackupArchiveBytes = FileTreeOverwriteRestoringValidation.MaximumArchiveBytes - 7, BackupArchiveSha256 = new('7', 64)
    };

    private static Rpc.FileTreeOverwriteRestoringPlan RpcPlan()
    {
        var plan = Plan();
        return new()
        {
            Command = Invoke<Rpc.FileTreeOverwriteRestoringCommand>("OverwriteRestoringCommand", plan.Command),
            ArchiveSha256 = plan.ArchiveSha256, ArchiveBytes = plan.ArchiveBytes, ContentSha256 = plan.ContentSha256,
            EntryCount = (uint)plan.EntryCount, TotalBytes = plan.TotalBytes,
            TargetParent = new() { Device = plan.TargetParent.Device, Inode = plan.TargetParent.Inode },
            SourcePolicyHash = plan.SourcePolicyHash, TargetPolicyHash = plan.TargetPolicyHash, Fingerprint = plan.Fingerprint,
            TargetManifestSha256 = plan.TargetManifestSha256, TargetTreeSha256 = plan.TargetTreeSha256,
            TargetEntryCount = (uint)plan.TargetEntryCount, TargetTotalBytes = plan.TargetTotalBytes,
            TargetIdentity = new() { Device = plan.TargetIdentity.Device, Inode = plan.TargetIdentity.Inode },
            TargetContentSha256 = plan.TargetContentSha256
        };
    }

    [Fact]
    public void CommandProjectionKeepsHistoricalAndCurrentTargetVersionsSeparateAndCanonicalizesCaptureId()
    {
        var command = Command(); var mapped = Invoke<Rpc.FileTreeOverwriteRestoringCommand>("OverwriteRestoringCommand", command);
        Assert.Equal(command.Source.RootId, mapped.Source.RootId); Assert.Equal(command.Source.Path, mapped.Source.Path);
        Assert.Equal(command.Target.RootId, mapped.Target.RootId); Assert.Equal(command.Target.Path, mapped.Target.Path);
        Assert.Equal(command.ExpectedVersion, mapped.ExpectedVersion); Assert.Equal(command.TargetExpectedVersion, mapped.TargetExpectedVersion);
        Assert.NotEqual(mapped.ExpectedVersion, mapped.TargetExpectedVersion);
        Assert.Equal(Capture.ToString("N"), mapped.CaptureId); Assert.Equal(command.CaptureOwner, mapped.CaptureOwner);
        Assert.Equal(command.CaptureKind, mapped.CaptureKind); Assert.Equal(command.ManifestSha256, mapped.ManifestSha256);
    }

    [Fact]
    public void PlanProjectionPreservesFullUInt64TargetIdentitiesAndIndependentWholeDirectorySizes()
    {
        var mapped = Invoke<App.FileTreeOverwriteRestoringPlan>("OverwriteRestoringPlan", RpcPlan(), Command());
        Assert.Equal(Plan(), mapped);
        Assert.Equal(ulong.MaxValue, mapped.TargetParent.Device); Assert.Equal(9_007_199_254_740_993UL, mapped.TargetParent.Inode);
        Assert.Equal(9_007_199_254_740_993UL, mapped.TargetIdentity.Device); Assert.Equal(ulong.MaxValue, mapped.TargetIdentity.Inode);
        Assert.Equal(FileTreeOverwriteRestoringValidation.MaximumBytes, mapped.TotalBytes);
        Assert.Equal(FileTreeOverwriteRestoringValidation.MaximumBytes - 7, mapped.TargetTotalBytes);
        Assert.Equal(FileTreeOverwriteRestoringValidation.MaximumArchiveBytes, mapped.ArchiveBytes);
        Assert.NotEqual(mapped.Command.ExpectedVersion, mapped.Command.TargetExpectedVersion);
        Assert.NotEqual(mapped.TargetManifestSha256, mapped.Command.ManifestSha256);
        Assert.Equal(Plan().TargetContentSha256, mapped.TargetContentSha256);
        Assert.NotEqual(mapped.TargetTreeSha256, mapped.TargetContentSha256);
    }

    [Fact]
    public void ProtobufFieldSixteenPreservesTargetContentRatherThanSubstitutingTargetTreeProof()
    {
        var original = RpcPlan();
        var decoded = Rpc.FileTreeOverwriteRestoringPlan.Parser.ParseFrom(original.ToByteArray());
        Assert.Equal(original, decoded);
        Assert.Equal(16, Rpc.FileTreeOverwriteRestoringPlan.Descriptor.FindFieldByName("target_content_sha256").FieldNumber);
        Assert.Equal(Plan().TargetContentSha256, decoded.TargetContentSha256);
        Assert.NotEqual(decoded.TargetTreeSha256, decoded.TargetContentSha256);
        Assert.Equal(Plan(), Invoke<App.FileTreeOverwriteRestoringPlan>("OverwriteRestoringPlan", decoded, Command()));
    }

    [Theory]
    [InlineData("")][InlineData("bad")][InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void MissingOrMalformedTargetContentShaCannotBorrowValidTargetTreeSha(string content)
    {
        var rpc = RpcPlan(); rpc.TargetContentSha256 = content;
        AssertRejected<ArgumentException>("OverwriteRestoringPlan", rpc, Command());
        AssertRejected<ArgumentException>("OverwriteRestoringReceipt", Row(Plan() with { TargetContentSha256 = content }), Receipt());
    }

    [Theory]
    [InlineData(1U, 34U, true, true)]
    [InlineData(1U, 35U, true, false)]
    [InlineData(1U, 35U, false, true)]
    [InlineData(1U, 35U, false, false)]
    [InlineData(2U, 35U, true, true)]
    public async Task OverwriteClientRejectsLegacyMinorOrEitherMissingIndependentCapability(uint major, uint minor, bool intents, bool content)
    {
        var invoker = new CapabilitiesInvoker(Capabilities(major, minor, intents, content));
        using var gateway = Gateway(invoker);
        var error = await Assert.ThrowsAsync<RpcException>(() => Client<Rpc.FileTreeOverwriteRestoringService.FileTreeOverwriteRestoringServiceClient>(gateway, "OverwriteRestoringClient"));
        Assert.Equal(StatusCode.Unimplemented, error.StatusCode);
        Assert.Equal("FILES_TREE_CAPABILITY_UNAVAILABLE", error.Status.Detail);
        Assert.Equal(1, invoker.Calls);
    }

    [Theory]
    [InlineData(35U)][InlineData(36U)]
    public async Task OverwriteClientAcceptsOnlyNewMinorWithBothExplicitCapabilities(uint minor)
    {
        var invoker = new CapabilitiesInvoker(Capabilities(1, minor, true, true));
        using var gateway = Gateway(invoker);
        Assert.IsType<Rpc.FileTreeOverwriteRestoringService.FileTreeOverwriteRestoringServiceClient>(
            await Client<Rpc.FileTreeOverwriteRestoringService.FileTreeOverwriteRestoringServiceClient>(gateway, "OverwriteRestoringClient"));
        Assert.Equal(1, invoker.Calls);
    }

    [Fact]
    public async Task AbsentTargetRestoreStillUsesItsOwnOlderThresholdAndDoesNotRequireOverwriteProofCapability()
    {
        var capabilities = new Rpc.CapabilitiesResponse { ProtocolMajor = 1, ProtocolMinor = 33 };
        capabilities.Capabilities.Add("files.tree-restore-intents");
        using var gateway = Gateway(new CapabilitiesInvoker(capabilities));
        Assert.IsType<Rpc.FileTreeRestoringService.FileTreeRestoringServiceClient>(
            await Client<Rpc.FileTreeRestoringService.FileTreeRestoringServiceClient>(gateway, "RestoringClient"));
    }

    [Fact]
    public async Task ApplicationApplySendsExactIndependentTargetContentInTheOutboundProtobuf()
    {
        var keyFile = Path.Combine(Path.GetTempPath(), "wd-overwrite-gateway-contract-" + Guid.NewGuid().ToString("N"));
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(keyFile, key);
        try
        {
            var invoker = new CapabilitiesInvoker(Capabilities(1, 35, true, true));
            using var gateway = Gateway(invoker, keyFile);
            using var handler = new ApplyHandler();
            var channel = GrpcChannel.ForAddress("http://127.0.0.1", new GrpcChannelOptions { HttpHandler = handler });
            var field = typeof(AgentGateway).GetField("_channel", BindingFlags.NonPublic | BindingFlags.Instance)!;
            ((GrpcChannel)field.GetValue(gateway)!).Dispose();
            field.SetValue(gateway, channel);
            var result = await gateway.ApplyTreeOverwriteRestoringAsync(Row(), CancellationToken.None);
            Assert.Equal(Execution, result.OperationId);
            var sent = Assert.IsType<Rpc.FileTreeOverwriteRestoringApplyRequest>(handler.Request);
            Assert.Equal(Plan().TargetContentSha256, sent.Plan.TargetContentSha256);
            Assert.NotEqual(sent.Plan.TargetTreeSha256, sent.Plan.TargetContentSha256);
            Assert.Equal(RpcPlan(), sent.Plan);
            Assert.Equal(Execution.ToString("N"), sent.OperationId);
            Assert.Equal(1, handler.Calls);
        }
        finally { CryptographicOperations.ZeroMemory(key); File.Delete(keyFile); }
    }

    [Theory]
    [InlineData("sourceRoot")][InlineData("sourcePath")][InlineData("sourceVersion")][InlineData("targetRoot")]
    [InlineData("targetPath")][InlineData("targetVersion")][InlineData("capture")][InlineData("captureOwner")]
    [InlineData("captureKind")][InlineData("manifest")]
    public void RpcPlanCannotSubstituteAnyHistoricalSourceOrCurrentTargetCommandFact(string field)
    {
        var rpc = RpcPlan();
        switch (field)
        {
            case "sourceRoot": rpc.Command.Source.RootId = "test.other"; break;
            case "sourcePath": rpc.Command.Source.Path = "foreign/source"; break;
            case "sourceVersion": rpc.Command.ExpectedVersion = new('8', 64); break;
            case "targetRoot": rpc.Command.Target.RootId = "test.other"; break;
            case "targetPath": rpc.Command.Target.Path = "foreign/target"; break;
            case "targetVersion": rpc.Command.TargetExpectedVersion = new('8', 64); break;
            case "capture": rpc.Command.CaptureId = Guid.NewGuid().ToString("N"); break;
            case "captureOwner": rpc.Command.CaptureOwner = new('8', 64); break;
            case "captureKind": rpc.Command.CaptureKind = "Overwrite"; break;
            case "manifest": rpc.Command.ManifestSha256 = new('8', 64); break;
        }
        AssertRejected<App.FileTransferRejectedException>("OverwriteRestoringPlan", rpc, Command());
    }

    [Theory]
    [InlineData("uppercase")][InlineData("dashed")][InlineData("braced")][InlineData("empty")][InlineData("zero")][InlineData("invalid")]
    public void RpcPlanRequiresExactCanonicalHistoricalCaptureId(string form)
    {
        var rpc = RpcPlan();
        rpc.Command.CaptureId = form switch
        {
            "uppercase" => Capture.ToString("N").ToUpperInvariant(), "dashed" => Capture.ToString("D"), "braced" => Capture.ToString("B"),
            "empty" => "", "zero" => Guid.Empty.ToString("N"), _ => "not-a-capture"
        };
        if (form == "zero") AssertRejected<ArgumentException>("OverwriteRestoringPlan", rpc, Command());
        else AssertRejected<App.FileTransferRejectedException>("OverwriteRestoringPlan", rpc, Command());
    }

    [Theory]
    [InlineData("command")][InlineData("source")][InlineData("target")][InlineData("parent")][InlineData("targetIdentity")]
    public void RpcPlanMissingRequiredTargetOrHistoricalProofObjectsIsRejected(string field)
    {
        var rpc = RpcPlan();
        switch (field)
        {
            case "command": rpc.Command = null; break;
            case "source": rpc.Command.Source = null; break;
            case "target": rpc.Command.Target = null; break;
            case "parent": rpc.TargetParent = null; break;
            case "targetIdentity": rpc.TargetIdentity = null; break;
        }
        AssertRejected<App.FileTransferRejectedException>("OverwriteRestoringPlan", rpc, Command());
    }

    [Theory]
    [InlineData("parentInode")][InlineData("targetInode")][InlineData("targetManifest")][InlineData("targetTree")]
    [InlineData("targetEntries")][InlineData("targetBytes")][InlineData("targetVersion")][InlineData("targetContent")]
    public void RpcPlanInvalidTargetProofCannotCrossTheApplicationBoundary(string field)
    {
        var rpc = RpcPlan();
        switch (field)
        {
            case "parentInode": rpc.TargetParent.Inode = 0; break;
            case "targetInode": rpc.TargetIdentity.Inode = 0; break;
            case "targetManifest": rpc.TargetManifestSha256 = ""; break;
            case "targetTree": rpc.TargetTreeSha256 = ""; break;
            case "targetEntries": rpc.TargetEntryCount = 0; break;
            case "targetBytes": rpc.TargetTotalBytes = FileTreeOverwriteRestoringValidation.MaximumBytes + 1; break;
            case "targetVersion": rpc.Command.TargetExpectedVersion = ""; break;
            case "targetContent": rpc.TargetContentSha256 = ""; break;
        }
        AssertRejected<ArgumentException>("OverwriteRestoringPlan", rpc, Command());
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void RpcPlanCountsCannotOverflowApplicationInt32(bool target)
    {
        var rpc = RpcPlan();
        if (target) rpc.TargetEntryCount = uint.MaxValue;
        else rpc.EntryCount = uint.MaxValue;
        AssertRejected<OverflowException>("OverwriteRestoringPlan", rpc, Command());
    }

    [Fact]
    public void CompletedReceiptProjectionPreservesEveryProofAndFullInt64ByteCount()
    {
        var rpc = Receipt(); var mapped = Invoke<App.FileTreeOverwriteRestoringReceipt>("OverwriteRestoringReceipt", Row(), rpc);
        Assert.Equal(Execution, mapped.OperationId); Assert.Equal(Capture, mapped.CaptureId); Assert.Equal(Execution, mapped.OverwrittenCaptureId);
        Assert.Equal(rpc.State, mapped.State); Assert.Equal(rpc.Fingerprint, mapped.Fingerprint); Assert.Equal(rpc.Format, mapped.Format);
        Assert.Equal(10_000, mapped.EntryCount); Assert.Equal(FileTreeOverwriteRestoringValidation.MaximumBytes, mapped.TotalBytes);
        Assert.Equal(FileTreeOverwriteRestoringValidation.MaximumArchiveBytes, mapped.ArchiveBytes); Assert.Equal(rpc.ArchiveSha256, mapped.ArchiveSha256);
        Assert.Equal(rpc.Version, mapped.Version); Assert.Equal("DirectoryOverwriteRestore", mapped.Kind); Assert.False(mapped.PartialRetained);
        Assert.True(mapped.BackupMayBeRetained && mapped.BackupVerified && mapped.OriginalsRetained && mapped.TargetMayHaveChanged);
        Assert.Equal(FileTreeOverwriteRestoringValidation.MaximumArchiveBytes - 7, mapped.BackupArchiveBytes);
        Assert.Equal(rpc.BackupArchiveSha256, mapped.BackupArchiveSha256);
    }

    [Theory]
    [InlineData("OperationId", "uppercase")][InlineData("OperationId", "dashed")][InlineData("OperationId", "braced")]
    [InlineData("OperationId", "empty")][InlineData("OperationId", "zero")][InlineData("OperationId", "invalid")]
    [InlineData("CaptureId", "uppercase")][InlineData("CaptureId", "dashed")][InlineData("CaptureId", "braced")]
    [InlineData("CaptureId", "empty")][InlineData("CaptureId", "zero")][InlineData("CaptureId", "invalid")]
    [InlineData("OverwrittenCaptureId", "uppercase")][InlineData("OverwrittenCaptureId", "dashed")][InlineData("OverwrittenCaptureId", "braced")]
    [InlineData("OverwrittenCaptureId", "empty")][InlineData("OverwrittenCaptureId", "zero")][InlineData("OverwrittenCaptureId", "invalid")]
    public void RootReceiptIdsMustBeExactCanonicalNonemptyResourceIdentities(string property, string form)
    {
        var rpc = Receipt(); var id = property == "CaptureId" ? Capture : Execution;
        var value = form switch
        {
            "uppercase" => id.ToString("N").ToUpperInvariant(), "dashed" => id.ToString("D"), "braced" => id.ToString("B"),
            "empty" => "", "zero" => Guid.Empty.ToString("N"), _ => "not-an-operation-id"
        };
        typeof(Rpc.FileTreeOverwriteRestoringReceipt).GetProperty(property)!.SetValue(rpc, value);
        AssertRejected<App.FileTransferRejectedException>("OverwriteRestoringReceipt", Row(), rpc);
    }

    [Theory]
    [InlineData("operation")][InlineData("capture")][InlineData("overwrittenCapture")][InlineData("fingerprint")]
    [InlineData("format")][InlineData("entries")][InlineData("bytes")][InlineData("archiveBytes")][InlineData("archiveHash")]
    [InlineData("state")][InlineData("kind")][InlineData("version")][InlineData("partial")][InlineData("backupMayBeRetained")]
    [InlineData("backupVerified")][InlineData("originalsRetained")][InlineData("targetMayHaveChanged")]
    [InlineData("backupBytes")][InlineData("backupHash")]
    public void CompletedRootReceiptCannotChangeBoundPlanFactsOrDropSwapBackupEvidence(string field)
    {
        var rpc = Receipt();
        switch (field)
        {
            case "operation": rpc.OperationId = Intent.ToString("N"); break;
            case "capture": rpc.CaptureId = Guid.NewGuid().ToString("N"); break;
            case "overwrittenCapture": rpc.OverwrittenCaptureId = Capture.ToString("N"); break;
            case "fingerprint": rpc.Fingerprint = new('8', 64); break;
            case "format": rpc.Format = "zip"; break;
            case "entries": rpc.EntryCount--; break;
            case "bytes": rpc.TotalBytes--; break;
            case "archiveBytes": rpc.ArchiveBytes--; break;
            case "archiveHash": rpc.ArchiveSha256 = new('8', 64); break;
            case "state": rpc.State = "Ready"; break;
            case "kind": rpc.Kind = "DirectoryRestore"; break;
            case "version": rpc.Version = ""; break;
            case "partial": rpc.PartialRetained = true; break;
            case "backupMayBeRetained": rpc.BackupMayBeRetained = false; break;
            case "backupVerified": rpc.BackupVerified = false; break;
            case "originalsRetained": rpc.OriginalsRetained = false; break;
            case "targetMayHaveChanged": rpc.TargetMayHaveChanged = false; break;
            case "backupBytes": rpc.BackupArchiveBytes = 0; break;
            case "backupHash": rpc.BackupArchiveSha256 = "bad"; break;
        }
        AssertRejected<App.FileTransferRejectedException>("OverwriteRestoringReceipt", Row(), rpc);
    }

    [Theory]
    [InlineData("Aborted")][InlineData("Unknown")]
    public void UnverifiedTerminalReceiptDoesNotInventBackupOrRestorationProof(string state)
    {
        var rpc = Receipt(); rpc.State = state; rpc.Version = ""; rpc.OverwrittenCaptureId = "";
        rpc.BackupVerified = false; rpc.OriginalsRetained = false; rpc.TargetMayHaveChanged = false;
        rpc.BackupArchiveBytes = 0; rpc.BackupArchiveSha256 = ""; rpc.BackupMayBeRetained = false;
        var mapped = Invoke<App.FileTreeOverwriteRestoringReceipt>("OverwriteRestoringReceipt", Row(), rpc);
        Assert.Equal(state, mapped.State); Assert.Null(mapped.OverwrittenCaptureId); Assert.False(mapped.BackupVerified);
        Assert.Equal(0, mapped.BackupArchiveBytes); Assert.Equal("", mapped.BackupArchiveSha256);
        rpc.BackupArchiveBytes = 1;
        AssertRejected<App.FileTransferRejectedException>("OverwriteRestoringReceipt", Row(), rpc);
    }

    [Theory]
    [InlineData("parent")][InlineData("target")][InlineData("targetManifest")][InlineData("targetTree")]
    [InlineData("targetEntries")][InlineData("targetBytes")][InlineData("targetVersion")][InlineData("targetContent")]
    public void RootReceiptIsRejectedWhenPersistedPlanHasLostTargetProof(string field)
    {
        var plan = Plan();
        plan = field switch
        {
            "parent" => plan with { TargetParent = new(ulong.MaxValue, 0) },
            "target" => plan with { TargetIdentity = new(ulong.MaxValue, 0) },
            "targetManifest" => plan with { TargetManifestSha256 = "" },
            "targetTree" => plan with { TargetTreeSha256 = "" },
            "targetEntries" => plan with { TargetEntryCount = 0 },
            "targetBytes" => plan with { TargetTotalBytes = FileTreeOverwriteRestoringValidation.MaximumBytes + 1 },
            "targetContent" => plan with { TargetContentSha256 = "" },
            _ => plan with { Command = plan.Command with { TargetExpectedVersion = "" } }
        };
        AssertRejected<ArgumentException>("OverwriteRestoringReceipt", Row(plan), Receipt());
    }

    [Fact]
    public void ReceiptCannotUseIntentIdOrForeignExecutionJobAsRootOperationIdentity()
    {
        var row = Row(); row.ExecutionJobId = Intent;
        AssertRejected<App.FileTransferRejectedException>("OverwriteRestoringReceipt", row, Receipt());
        row.ExecutionJobId = Guid.NewGuid();
        AssertRejected<App.FileTransferRejectedException>("OverwriteRestoringReceipt", row, Receipt());
        row.ExecutionJobId = null;
        AssertRejected<App.FileTransferRejectedException>("OverwriteRestoringReceipt", row, Receipt());
    }

    [Fact]
    public void RootEntryCountOverflowIsRejectedInsteadOfTruncated()
    {
        var rpc = Receipt(); rpc.EntryCount = uint.MaxValue;
        AssertRejected<OverflowException>("OverwriteRestoringReceipt", Row(), rpc);
    }

    private static T Invoke<T>(string name, params object[] values) => Assert.IsType<T>(Method(name).Invoke(null, values));
    private static void AssertRejected<T>(string name, params object[] values) where T : Exception
    {
        var wrapped = Assert.Throws<TargetInvocationException>(() => Method(name).Invoke(null, values));
        Assert.IsType<T>(wrapped.InnerException);
    }
    private static MethodInfo Method(string name) => typeof(AgentGateway).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Required pure gateway mapping is missing: " + name);

    private static Rpc.CapabilitiesResponse Capabilities(uint major, uint minor, bool intents, bool content)
    {
        var result = new Rpc.CapabilitiesResponse { ProtocolMajor = major, ProtocolMinor = minor };
        if (intents) result.Capabilities.Add("files.tree-overwrite-restore-intents");
        if (content) result.Capabilities.Add("files.tree-overwrite-restore-content-proof");
        return result;
    }
    private static AgentGateway Gateway(CapabilitiesInvoker invoker, string? keyFile = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Agent:SocketPath"] = Path.Combine(Path.GetTempPath(), "not-a-workstation-agent-" + Guid.NewGuid().ToString("N")),
            ["Agent:CapabilityKeyFile"] = keyFile ?? Path.Combine(Path.GetTempPath(), "not-a-workstation-key-" + Guid.NewGuid().ToString("N")) }).Build();
        var gateway = new AgentGateway(configuration);
        typeof(AgentGateway).GetField("_agent", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(gateway, new Rpc.AgentService.AgentServiceClient(invoker));
        return gateway;
    }
    private static Task<T> Client<T>(AgentGateway gateway, string method) => (Task<T>)(typeof(AgentGateway)
        .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
        .Invoke(gateway, [CancellationToken.None]) ?? throw new InvalidOperationException("Missing client result"));

    private sealed class CapabilitiesInvoker(Rpc.CapabilitiesResponse response) : CallInvoker
    {
        public int Calls { get; private set; }
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            Assert.Equal("/whaledeck.agent.v1.AgentService/GetCapabilities", method.FullName);
            Calls++;
            return new(Task.FromResult((TResponse)(object)response), Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess, () => [], () => { });
        }
        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }

    private sealed class ApplyHandler : HttpMessageHandler
    {
        public Rpc.FileTreeOverwriteRestoringApplyRequest? Request { get; private set; }
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/whaledeck.agent.v1.FileTreeOverwriteRestoringService/Apply", request.RequestUri!.AbsolutePath);
            Assert.True(request.Headers.Contains("x-whaledeck-capability"));
            var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            Assert.Equal(0, body[0]); Assert.Equal(body.Length - 5, BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(1, 4)));
            Request = Rpc.FileTreeOverwriteRestoringApplyRequest.Parser.ParseFrom(body.AsSpan(5)); Calls++;
            var payload = Receipt().ToByteArray(); var frame = new byte[payload.Length + 5];
            BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1, 4), payload.Length); payload.CopyTo(frame, 5);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Version = HttpVersion.Version20, Content = new ByteArrayContent(frame) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            response.TrailingHeaders.Add("grpc-status", "0");
            return response;
        }
    }
}
