using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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

/// <summary>Pure projections and in-memory HTTP/gRPC framing, never a real network, UDS or helper.</summary>
public sealed class PurgingGatewayContractTests
{
    private static readonly Guid Intent = Guid.Parse("073e1d38-e73f-450a-8226-40746147f35c");
    private static readonly Guid Capture = Guid.Parse("846516d5-6a48-4ba5-9468-cd61f55728ed");
    private static Guid Execution => FileTreePurgingValidation.ExecutionId(Intent);
    private const string Service = "/whaledeck.agent.v1.FileTreePurgingService/";

    private static App.FileTreePurgingCommand Command(string scope = "All") => new(new("test.shared", "代码/历史目录"),
        new('A', 64), Capture, new('B', 64), "Overwrite", new('C', 64), scope, new('D', 64));

    private static App.FileTreePurgingPlan Plan(string scope = "All") => new(Command(scope), new('1', 64),
        scope == "Archive" ? null : new(ulong.MaxValue, 9_007_199_254_740_993UL), new('2', 64),
        new(9_007_199_254_740_993UL, ulong.MaxValue), new('3', 64), new('4', 64), new('5', 64),
        new(ulong.MaxValue, ulong.MaxValue), FileTreePurgingValidation.MaximumArchiveBytes, new('6', 64), new('7', 64),
        FileTreePurgingValidation.MaximumEntries, FileTreePurgingValidation.MaximumBytes,
        scope == "Archive" ? null : new(9_007_199_254_740_993UL, ulong.MaxValue),
        scope == "Archive" ? null : new(ulong.MaxValue, 9_007_199_254_740_993UL),
        scope == "Archive" ? null : new('8', 64), new('9', 64));

    private static FileTreePurgingOperation Row(App.FileTreePurgingPlan? plan = null, string? owner = null) => new()
    {
        Id = Intent, CaptureId = Capture, ActorSubject = "synthetic-purge-actor", LoginBinding = new('A', 64),
        IdempotencyKey = "73d78374-c834-4e3c-ab4f-b5f8114cdfd5", OwnerHash = owner ?? new('B', 64),
        RequestHash = new('C', 64), SourceHash = new('D', 64), CommandJson = JsonSerializer.Serialize((plan ?? Plan()).Command),
        PlanJson = JsonSerializer.Serialize(plan ?? Plan()), ExecutionJobId = Execution
    };

    private static Rpc.FileTreePurgingPlan RpcPlan(string scope = "All")
    {
        var plan = Plan(scope);
        var result = new Rpc.FileTreePurgingPlan
        {
            Command = Invoke<Rpc.FileTreePurgingCommand>("PurgingCommand", plan.Command), SourcePolicyHash = plan.SourcePolicyHash,
            SourceParent = Identity(plan.SourceParent), StorageHash = plan.StorageHash, StoreIdentity = Identity(plan.StoreIdentity),
            CaptureMetadataHash = plan.CaptureMetadataHash, OriginReceiptHash = plan.OriginReceiptHash,
            ManifestFileSha256 = plan.ManifestFileSha256, ArchiveIdentity = Identity(plan.ArchiveIdentity), ArchiveBytes = plan.ArchiveBytes,
            ArchiveSha256 = plan.ArchiveSha256, ContentSha256 = plan.ContentSha256, EntryCount = (uint)plan.EntryCount,
            TotalBytes = plan.TotalBytes, RetainedIdentity = Identity(plan.RetainedIdentity), StagingIdentity = Identity(plan.StagingIdentity),
            Fingerprint = plan.Fingerprint
        };
        if (plan.RetainedTreeSha256 is not null) result.RetainedTreeSha256 = plan.RetainedTreeSha256;
        return result;
    }

    private static Rpc.FileDirectoryIdentity? Identity(App.FileParentIdentity? value) => value is null ? null : new() { Device = value.Device, Inode = value.Inode };
    private static Rpc.FileTreePurgingReceipt Receipt(string scope = "All", string state = "Completed") => new()
    {
        OperationId = Execution.ToString("N"), CaptureId = Capture.ToString("N"), Kind = "DirectoryPurge", State = state,
        Fingerprint = Plan(scope).Fingerprint, Scope = Scope(scope), EntryCount = FileTreePurgingValidation.MaximumEntries,
        TotalBytes = FileTreePurgingValidation.MaximumBytes, ArchiveBytes = FileTreePurgingValidation.MaximumArchiveBytes,
        DeletionStarted = state != "Aborted", ArchiveRemoved = state == "Completed" && scope is "Archive" or "All",
        OriginalsRemoved = state == "Completed" && scope is "Originals" or "All"
    };
    private static Rpc.FileTreePurgeScope Scope(string value) => value switch
    {
        "Archive" => Rpc.FileTreePurgeScope.Archive, "Originals" => Rpc.FileTreePurgeScope.Originals,
        _ => Rpc.FileTreePurgeScope.All
    };

    [Theory]
    [InlineData(1U, 35U, true)][InlineData(1U, 0U, true)]
    [InlineData(2U, 36U, true)][InlineData(0U, 36U, true)][InlineData(1U, 36U, false)]
    public async Task ClientRejectsOldMinorWrongMajorAndMissingExplicitCapability(uint major, uint minor, bool capability)
    {
        var invoker = new CapabilitiesInvoker(Capabilities(major, minor, capability));
        using var gateway = Gateway(invoker);
        var error = await Assert.ThrowsAsync<RpcException>(() => Client(gateway));
        Assert.Equal(StatusCode.Unimplemented, error.StatusCode);
        Assert.Equal("FILES_TREE_CAPABILITY_UNAVAILABLE", error.Status.Detail); Assert.Equal(1, invoker.Calls);
    }

    [Theory]
    [InlineData(36U)][InlineData(37U)]
    public async Task ClientAcceptsOnlyNewExplicitCapabilityWithoutRequiringLegacyOnes(uint minor)
    {
        using var gateway = Gateway(new CapabilitiesInvoker(Capabilities(1, minor, true)));
        Assert.IsType<Rpc.FileTreePurgingService.FileTreePurgingServiceClient>(await Client(gateway));
    }

    [Theory]
    [InlineData("Archive")][InlineData("Originals")][InlineData("All")]
    public void AllEighteenPlanFieldsAndFullUInt64IdentitiesSurviveProjectionAndProtobuf(string scope)
    {
        var rpc = RpcPlan(scope); var decoded = Rpc.FileTreePurgingPlan.Parser.ParseFrom(rpc.ToByteArray());
        Assert.Equal(rpc, decoded); Assert.Equal(18, Rpc.FileTreePurgingPlan.Descriptor.Fields.InFieldNumberOrder().Count);
        var names = new[] { "command", "source_policy_hash", "source_parent", "storage_hash", "store_identity", "capture_metadata_hash",
            "origin_receipt_hash", "manifest_file_sha256", "archive_identity", "archive_bytes", "archive_sha256", "content_sha256",
            "entry_count", "total_bytes", "retained_identity", "staging_identity", "retained_tree_sha256", "fingerprint" };
        for (var index = 0; index < names.Length; index++) Assert.Equal(index + 1, Rpc.FileTreePurgingPlan.Descriptor.FindFieldByName(names[index]).FieldNumber);
        var mapped = Invoke<App.FileTreePurgingPlan>("PurgingPlan", decoded, Command(scope));
        Assert.Equal(Plan(scope), mapped); Assert.Equal(ulong.MaxValue, mapped.StoreIdentity.Inode);
        Assert.Equal(9_007_199_254_740_993UL, mapped.StoreIdentity.Device); Assert.Equal(ulong.MaxValue, mapped.ArchiveIdentity.Device);
        Assert.Equal(FileTreePurgingValidation.MaximumBytes, mapped.TotalBytes);
        Assert.Equal(FileTreePurgingValidation.MaximumArchiveBytes, mapped.ArchiveBytes);
        Assert.Equal(Command(scope).CatalogueProofHash, mapped.Command.CatalogueProofHash);
        Assert.Equal(Capture.ToString("N"), decoded.Command.CaptureId);
        Assert.Equal(scope != "Archive", decoded.HasRetainedTreeSha256);
        if (scope == "Archive")
        {
            Assert.Null(mapped.SourceParent); Assert.Null(mapped.RetainedIdentity); Assert.Null(mapped.StagingIdentity); Assert.Null(mapped.RetainedTreeSha256);
        }
    }

    [Theory]
    [InlineData("sourceRoot")][InlineData("sourcePath")][InlineData("version")][InlineData("capture")]
    [InlineData("owner")][InlineData("kind")][InlineData("manifest")][InlineData("scope")]
    [InlineData("missingCommand")][InlineData("missingSource")]
    public void PlanCannotSubstituteAnyFrozenCommandFact(string field)
    {
        var rpc = RpcPlan();
        switch (field)
        {
            case "sourceRoot": rpc.Command.Source.RootId = "test.foreign"; break;
            case "sourcePath": rpc.Command.Source.Path = "foreign"; break;
            case "version": rpc.Command.ExpectedVersion = new('0', 64); break;
            case "capture": rpc.Command.CaptureId = Guid.NewGuid().ToString("N"); break;
            case "owner": rpc.Command.CaptureOwner = new('0', 64); break;
            case "kind": rpc.Command.CaptureKind = "Trash"; break;
            case "manifest": rpc.Command.ManifestSha256 = new('0', 64); break;
            case "scope": rpc.Command.Scope = Rpc.FileTreePurgeScope.Archive; break;
            case "missingCommand": rpc.Command = null; break;
            case "missingSource": rpc.Command.Source = null; break;
        }
        Rejected<App.FileTransferRejectedException>("PurgingPlan", rpc, Command());
    }

    [Theory]
    [InlineData("dashed")][InlineData("uppercase")][InlineData("zero")][InlineData("empty")][InlineData("invalid")]
    public void HistoricalCaptureIdMustBeExactCanonical(string form)
    {
        var rpc = RpcPlan(); rpc.Command.CaptureId = IdForm(Capture, form);
        Rejected<App.FileTransferRejectedException>("PurgingPlan", rpc, Command());
    }

    [Theory]
    [InlineData("SourceParent")][InlineData("RetainedIdentity")][InlineData("StagingIdentity")][InlineData("RetainedTreeSha256")]
    public void ArchiveScopeRejectsAnyOfTheFourUnexpectedOriginalsProofs(string field)
    {
        var rpc = RpcPlan("Archive");
        if (field == "RetainedTreeSha256") rpc.RetainedTreeSha256 = new('8', 64);
        else typeof(Rpc.FileTreePurgingPlan).GetProperty(field)!.SetValue(rpc, new Rpc.FileDirectoryIdentity { Device = 1, Inode = 2 });
        Rejected<ArgumentException>("PurgingPlan", rpc, Command("Archive"));
    }

    [Theory]
    [InlineData("SourceParent")][InlineData("RetainedIdentity")][InlineData("StagingIdentity")][InlineData("RetainedTreeSha256")]
    public void OriginalsScopeRequiresAllFourFrozenOriginalsProofs(string field)
    {
        var rpc = RpcPlan("Originals");
        if (field == "RetainedTreeSha256") rpc.ClearRetainedTreeSha256();
        else typeof(Rpc.FileTreePurgingPlan).GetProperty(field)!.SetValue(rpc, null);
        Rejected<ArgumentException>("PurgingPlan", rpc, Command("Originals"));
    }

    [Theory]
    [InlineData("StoreIdentity")][InlineData("ArchiveIdentity")]
    public void EveryScopeRequiresStoreAndArchiveObjectProofs(string field)
    {
        var rpc = RpcPlan("Archive"); typeof(Rpc.FileTreePurgingPlan).GetProperty(field)!.SetValue(rpc, null);
        Rejected<App.FileTransferRejectedException>("PurgingPlan", rpc, Command("Archive"));
    }

    [Theory]
    [InlineData("SourcePolicyHash")][InlineData("StorageHash")][InlineData("CaptureMetadataHash")][InlineData("OriginReceiptHash")]
    [InlineData("ManifestFileSha256")][InlineData("ArchiveSha256")][InlineData("ContentSha256")][InlineData("Fingerprint")]
    public void EachIndependentPlanHashIsRequiredRatherThanBorrowingAnotherProof(string field)
    {
        var rpc = RpcPlan(); typeof(Rpc.FileTreePurgingPlan).GetProperty(field)!.SetValue(rpc, "");
        Rejected<ArgumentException>("PurgingPlan", rpc, Command());
    }

    [Theory]
    [InlineData("StoreIdentity")][InlineData("ArchiveIdentity")][InlineData("SourceParent")][InlineData("RetainedIdentity")][InlineData("StagingIdentity")]
    public void ZeroInodeInAnyRequiredProofIsRejected(string field)
    {
        var rpc = RpcPlan(); ((Rpc.FileDirectoryIdentity)typeof(Rpc.FileTreePurgingPlan).GetProperty(field)!.GetValue(rpc)!).Inode = 0;
        Rejected<ArgumentException>("PurgingPlan", rpc, Command());
    }

    [Theory]
    [InlineData("entriesZero")][InlineData("entriesTooMany")][InlineData("bytesNegative")][InlineData("bytesTooMany")]
    [InlineData("archiveZero")][InlineData("archiveTooMany")]
    public void PlanNumericLimitsAreNotTruncatedOrClamped(string field)
    {
        var rpc = RpcPlan();
        switch (field)
        {
            case "entriesZero": rpc.EntryCount = 0; break;
            case "entriesTooMany": rpc.EntryCount = FileTreePurgingValidation.MaximumEntries + 1; break;
            case "bytesNegative": rpc.TotalBytes = -1; break;
            case "bytesTooMany": rpc.TotalBytes = FileTreePurgingValidation.MaximumBytes + 1; break;
            case "archiveZero": rpc.ArchiveBytes = 0; break;
            case "archiveTooMany": rpc.ArchiveBytes = FileTreePurgingValidation.MaximumArchiveBytes + 1; break;
        }
        Rejected<ArgumentException>("PurgingPlan", rpc, Command());
    }

    [Fact]
    public void UInt32EntryOverflowIsRejectedWhileValidLowerBoundsRemainExact()
    {
        var rpc = RpcPlan(); rpc.EntryCount = uint.MaxValue;
        Rejected<OverflowException>("PurgingPlan", rpc, Command());
        rpc = RpcPlan(); rpc.EntryCount = 1; rpc.TotalBytes = 0; rpc.ArchiveBytes = 1;
        var result = Invoke<App.FileTreePurgingPlan>("PurgingPlan", rpc, Command());
        Assert.Equal(1, result.EntryCount); Assert.Equal(0L, result.TotalBytes); Assert.Equal(1L, result.ArchiveBytes);
    }

    [Theory]
    [InlineData(false, "All")][InlineData(true, "All")][InlineData(false, "Archive")][InlineData(true, "Archive")]
    public async Task ApplyAndDiscardSendExactExecutionOwnerContextAndAllPlanFieldsWithDistinctSignatures(bool discard, string scope)
    {
        using var harness = new Harness(discard ? "Discard" : "Apply", Receipt(scope));
        var row = Row(Plan(scope));
        var result = discard ? await harness.Gateway.DiscardTreePurgingAsync(row, CancellationToken.None)
            : await harness.Gateway.ApplyTreePurgingAsync(row, CancellationToken.None);
        var sent = Assert.IsType<Rpc.FileTreePurgingApplyRequest>(harness.Handler.Request);
        Assert.Equal(Execution.ToString("N"), sent.OperationId); Assert.NotEqual(Capture.ToString("N"), sent.OperationId);
        Assert.Equal(row.OwnerHash, sent.Owner); Assert.Equal(row.ActorSubject, sent.Context.ActorSubject);
        Assert.Equal(Intent.ToString("D"), sent.Context.TraceId); Assert.Equal(RpcPlan(scope), sent.Plan);
        Assert.Equal("Completed", result.State); Assert.Equal(Execution, result.OperationId); Assert.Equal(1, harness.Handler.Calls);
        harness.AssertSignature(discard ? "Discard" : "Apply", discard ? "Apply" : "Discard");
    }

    [Fact]
    public async Task PublicPlanSendsExactCommandAndKeepsServerOnlyCatalogueProof()
    {
        using var harness = new Harness("Plan", RpcPlan());
        var actor = new App.TerminalActor("synthetic-plan-actor", null, [], new('A', 64), "synthetic-trace", null);
        var result = await harness.Gateway.PlanTreePurgingAsync(actor, Command(), CancellationToken.None);
        var sent = Assert.IsType<Rpc.FileTreePurgingPlanRequest>(harness.Handler.Request);
        Assert.Equal(Invoke<Rpc.FileTreePurgingCommand>("PurgingCommand", Command()), sent.Command);
        Assert.Equal(actor.Subject, sent.Context.ActorSubject); Assert.Equal(actor.TraceId, sent.Context.TraceId);
        Assert.Equal(Plan(), result); Assert.Equal(Command().CatalogueProofHash, result.Command.CatalogueProofHash);
        harness.AssertSignature("Plan", "Apply");
    }

    [Theory]
    [InlineData("missing")][InlineData("capture")][InlineData("intent")][InlineData("foreign")][InlineData("owner")]
    public async Task InvalidExecutionOrOwnerIsRejectedBeforeSendingAnyRequest(string field)
    {
        using var harness = new Harness("Apply", Receipt());
        var row = Row(owner: field == "owner" ? "bad" : null);
        row.ExecutionJobId = field switch { "missing" => null, "capture" => Capture, "intent" => Intent,
            "foreign" => Guid.NewGuid(), _ => Execution };
        await Assert.ThrowsAsync<App.FileTransferRejectedException>(() => harness.Gateway.ApplyTreePurgingAsync(row, CancellationToken.None));
        Assert.Equal(0, harness.Handler.Calls);
    }

    [Fact]
    public async Task InspectMapsOnlyNotFoundToNullAndSendsCanonicalLookup()
    {
        using var harness = new Harness("Inspect", Receipt(), StatusCode.NotFound);
        Assert.Null(await harness.Gateway.InspectTreePurgingAsync(Row(), CancellationToken.None));
        var request = Assert.IsType<Rpc.FileTreePurgingLookup>(harness.Handler.Request);
        Assert.Equal(Execution.ToString("N"), request.OperationId); Assert.Equal(Row().OwnerHash, request.Owner);
        harness.AssertSignature("Inspect", "Apply");
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)][InlineData(StatusCode.PermissionDenied)][InlineData(StatusCode.InvalidArgument)]
    public async Task InspectDoesNotConcealOtherRpcFailuresAsMissing(StatusCode code)
    {
        using var harness = new Harness("Inspect", Receipt(), code);
        var error = await Assert.ThrowsAsync<RpcException>(() => harness.Gateway.InspectTreePurgingAsync(Row(), CancellationToken.None));
        Assert.Equal(code, error.StatusCode);
    }

    [Fact]
    public async Task UnknownRemainsUnknownWithoutInventingCompletedRemoval()
    {
        using var harness = new Harness("Inspect", Receipt(state: "Unknown"));
        var result = Assert.IsType<App.FileTreePurgingReceipt>(await harness.Gateway.InspectTreePurgingAsync(Row(), CancellationToken.None));
        Assert.Equal("Unknown", result.State); Assert.True(result.DeletionStarted);
        Assert.False(result.ArchiveRemoved); Assert.False(result.OriginalsRemoved);
    }

    [Theory]
    [InlineData("OperationId", "dashed")][InlineData("OperationId", "uppercase")][InlineData("OperationId", "zero")]
    [InlineData("CaptureId", "dashed")][InlineData("CaptureId", "uppercase")][InlineData("CaptureId", "empty")]
    public void ReceiptIdsMustBeExactCanonicalResourceIdentities(string property, string form)
    {
        var rpc = Receipt(); typeof(Rpc.FileTreePurgingReceipt).GetProperty(property)!.SetValue(rpc, IdForm(property == "CaptureId" ? Capture : Execution, form));
        Rejected<App.FileTransferRejectedException>("PurgingReceipt", Row(), rpc);
    }

    [Theory]
    [InlineData("operation")][InlineData("capture")][InlineData("kind")][InlineData("state")][InlineData("scope")]
    [InlineData("fingerprint")][InlineData("entries")][InlineData("bytes")][InlineData("archiveBytes")]
    [InlineData("notStarted")][InlineData("archiveNotRemoved")][InlineData("originalsNotRemoved")][InlineData("abortedStarted")]
    public void ReceiptCannotReplacePlanFactsOrInventAnInvalidOutcome(string field)
    {
        var rpc = Receipt();
        switch (field)
        {
            case "operation": rpc.OperationId = Guid.NewGuid().ToString("N"); break;
            case "capture": rpc.CaptureId = Guid.NewGuid().ToString("N"); break;
            case "kind": rpc.Kind = "DirectoryRestore"; break;
            case "state": rpc.State = "Running"; break;
            case "scope": rpc.Scope = Rpc.FileTreePurgeScope.Unspecified; break;
            case "fingerprint": rpc.Fingerprint = new('0', 64); break;
            case "entries": rpc.EntryCount--; break;
            case "bytes": rpc.TotalBytes--; break;
            case "archiveBytes": rpc.ArchiveBytes--; break;
            case "notStarted": rpc.DeletionStarted = false; break;
            case "archiveNotRemoved": rpc.ArchiveRemoved = false; break;
            case "originalsNotRemoved": rpc.OriginalsRemoved = false; break;
            case "abortedStarted": rpc.State = "Aborted"; break;
        }
        Rejected<App.FileTransferRejectedException>("PurgingReceipt", Row(), rpc);
    }

    [Theory]
    [InlineData("Archive")][InlineData("Originals")]
    public void ReceiptCannotReportRemovalOfAnUnselectedComponent(string scope)
    {
        var rpc = Receipt(scope);
        if (scope == "Archive") rpc.OriginalsRemoved = true; else rpc.ArchiveRemoved = true;
        Rejected<App.FileTransferRejectedException>("PurgingReceipt", Row(Plan(scope)), rpc);
    }

    [Fact]
    public void ReceiptEntryOverflowAndChangingPreviouslyObservedTerminalOutcomeAreRejected()
    {
        var rpc = Receipt(); rpc.EntryCount = uint.MaxValue;
        Rejected<OverflowException>("PurgingReceipt", Row(), rpc);
        var row = Row(); var unknown = Invoke<App.FileTreePurgingReceipt>("PurgingReceipt", row, Receipt(state: "Unknown"));
        row.ReceiptJson = JsonSerializer.Serialize(unknown);
        Assert.Equal(unknown, Invoke<App.FileTreePurgingReceipt>("PurgingReceipt", row, Receipt(state: "Unknown")));
        Rejected<InvalidOperationException>("PurgingReceipt", row, Receipt());
    }

    private static string IdForm(Guid id, string form) => form switch
    {
        "dashed" => id.ToString("D"), "uppercase" => id.ToString("N").ToUpperInvariant(), "zero" => Guid.Empty.ToString("N"),
        "empty" => "", _ => "not-an-id"
    };
    private static T Invoke<T>(string method, params object[] values) => Assert.IsType<T>(Method(method).Invoke(null, values));
    private static void Rejected<T>(string method, params object[] values) where T : Exception =>
        Assert.IsType<T>(Assert.Throws<TargetInvocationException>(() => Method(method).Invoke(null, values)).InnerException);
    private static MethodInfo Method(string name) => typeof(AgentGateway).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
        .Single(method => method.Name == name);
    private static Rpc.CapabilitiesResponse Capabilities(uint major = 1, uint minor = 36, bool capability = true)
    {
        var result = new Rpc.CapabilitiesResponse { ProtocolMajor = major, ProtocolMinor = minor };
        if (capability) result.Capabilities.Add("files.tree-purge-intents"); return result;
    }
    private static AgentGateway Gateway(CapabilitiesInvoker invoker, string? keyFile = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Agent:SocketPath"] = Path.Combine(Path.GetTempPath(), "not-a-real-agent-" + Guid.NewGuid().ToString("N")),
            ["Agent:CapabilityKeyFile"] = keyFile ?? Path.Combine(Path.GetTempPath(), "not-a-real-key-" + Guid.NewGuid().ToString("N")) }).Build();
        var gateway = new AgentGateway(configuration);
        typeof(AgentGateway).GetField("_agent", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(gateway, new Rpc.AgentService.AgentServiceClient(invoker)); return gateway;
    }
    private static Task<Rpc.FileTreePurgingService.FileTreePurgingServiceClient> Client(AgentGateway gateway) =>
        (Task<Rpc.FileTreePurgingService.FileTreePurgingServiceClient>)typeof(AgentGateway)
            .GetMethod("PurgingClient", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(gateway, [CancellationToken.None])!;

    private sealed class Harness : IDisposable
    {
        private readonly string _keyFile = Path.Combine(Path.GetTempPath(), "wd-purge-contract-" + Guid.NewGuid().ToString("N"));
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
        public AgentGateway Gateway { get; }
        public ResponseHandler Handler { get; }
        public Harness(string method, IMessage response, StatusCode status = StatusCode.OK)
        {
            File.WriteAllBytes(_keyFile, _key);
            Gateway = PurgingGatewayContractTests.Gateway(new CapabilitiesInvoker(Capabilities()), _keyFile);
            Handler = new(method, response, status);
            var channel = GrpcChannel.ForAddress("http://127.0.0.1", new GrpcChannelOptions { HttpHandler = Handler });
            var field = typeof(AgentGateway).GetField("_channel", BindingFlags.NonPublic | BindingFlags.Instance)!;
            ((GrpcChannel)field.GetValue(Gateway)!).Dispose(); field.SetValue(Gateway, channel);
        }
        public void AssertSignature(string selected, string other)
        {
            var parts = Assert.IsType<string>(Handler.Capability).Split('.'); Assert.Equal(3, parts.Length);
            Assert.True(long.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture) >= DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            Assert.True(Guid.TryParseExact(parts[1], "N", out _));
            string Sign(string method) => Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(parts[0] + "." + parts[1] + "." + Service + method)));
            Assert.Equal(Sign(selected), parts[2]); Assert.NotEqual(Sign(other), parts[2]);
        }
        public void Dispose()
        {
            Gateway.Dispose();
            if (typeof(AgentGateway).GetField("_capabilityKey", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Gateway) is byte[] copy)
                CryptographicOperations.ZeroMemory(copy);
            CryptographicOperations.ZeroMemory(_key); File.Delete(_keyFile);
        }
    }

    private sealed class ResponseHandler(string method, IMessage result, StatusCode status) : HttpMessageHandler
    {
        public IMessage? Request { get; private set; }
        public string? Capability { get; private set; }
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(Service + method, request.RequestUri!.AbsolutePath);
            Capability = Assert.Single(request.Headers.GetValues("x-whaledeck-capability"));
            var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            Assert.Equal(0, body[0]); Assert.Equal(body.Length - 5, BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(1, 4)));
            Request = method switch { "Plan" => Rpc.FileTreePurgingPlanRequest.Parser.ParseFrom(body.AsSpan(5)),
                "Inspect" => Rpc.FileTreePurgingLookup.Parser.ParseFrom(body.AsSpan(5)),
                _ => Rpc.FileTreePurgingApplyRequest.Parser.ParseFrom(body.AsSpan(5)) };
            Calls++;
            var payload = status == StatusCode.OK ? result.ToByteArray() : [];
            var frame = status == StatusCode.OK ? new byte[payload.Length + 5] : [];
            if (status == StatusCode.OK) { BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1, 4), payload.Length); payload.CopyTo(frame, 5); }
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Version = HttpVersion.Version20, Content = new ByteArrayContent(frame) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            response.TrailingHeaders.Add("grpc-status", ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (status != StatusCode.OK) response.TrailingHeaders.Add("grpc-message", "FILES_SYNTHETIC_FAILURE");
            return response;
        }
    }

    private sealed class CapabilitiesInvoker(Rpc.CapabilitiesResponse response) : CallInvoker
    {
        public int Calls { get; private set; }
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            Assert.Equal("/whaledeck.agent.v1.AgentService/GetCapabilities", method.FullName); Calls++;
            return new(Task.FromResult((TResponse)(object)response), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
        }
        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }
}
