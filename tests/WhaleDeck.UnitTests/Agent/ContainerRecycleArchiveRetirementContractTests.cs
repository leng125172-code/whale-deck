using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerRecycleArchiveRetirementContractTests
{
    private static readonly string[] RetirementMethods = ["Plan", "Reserve", "Apply", "Inspect", "Discard"];

    internal static ContainerRecycleArchiveRetirementRequest Request()
    {
        var archive = ContainerRecycleArchivePolicyTests.Request(); var ap = archive.Plan;
        var published = ap.DeletedIdentity.Clone(); published.Inode += 1000;
        var plan = new ContainerRecycleArchiveRetirementPlan
        {
            SchemaVersion = 1, Command = new() { Archive = new() { OperationId = archive.OperationId, Owner = archive.Owner, Plan = ap.Clone() }, RetainDays = 30, Mode = "ArchiveData" },
            ArchiveRecordDigest = new('A', 64), SourceRecordDigest = ap.SourceRecordDigest, PurgeRecordDigest = ap.PurgeRecordDigest, PurgeMarkerDigest = ap.PurgeMarkerDigest,
            ClaimDigest = new('B', 64), HistoryMetadataDigest = ap.HistoryMetadataDigest, HistoryCreatedAtUnixMs = ap.Command.Purge.Plan.HistoryCreatedAtUnixMs,
            RetentionElapsed = false, BindingCurrent = true, RetirementEligible = false,
            StorageHash = ap.StorageHash, StoreIdentity = ap.StoreIdentity.Clone(), ArchiveIdentity = ap.ArchiveIdentity.Clone(), ArchiveChangedAtUnixNs = ap.ArchiveChangedAtUnixNs,
            ArchiveBytes = ap.ArchiveBytes, ArchiveSha256 = ap.ArchiveSha256, PublishedIdentity = published, PublishedChangedAtUnixNs = 1793925351911000007
        };
        plan.NotBeforeUnixMs = plan.HistoryCreatedAtUnixMs + 30L * 86400000;
        plan.Fingerprint = ContainerRecycleArchiveRetirementWire.Fingerprint(plan);
        return new() { OperationId = "ee0a8865343e49bdb01a72680131bf39", Owner = new('C', 64), Plan = plan, Context = archive.Context.Clone() };
    }

    [Fact]
    public async Task NativeCanonicalFingerprintMatchesIncludingNanosecondsAndBooleans()
    {
        var p = Request().Plan;
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../deploy/host/whaledeck-container-recycle-archive-retention.py"));
        var start = new ProcessStartInfo("python3") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-I", "-B", "-c", "import importlib.util,json,sys; s=importlib.util.spec_from_file_location('r',sys.argv[1]); m=importlib.util.module_from_spec(s); s.loader.exec_module(m); print(m.ContainerRecycleArchiveRetentionPreflight.digest(json.load(sys.stdin)))", path }) start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!;
        await child.StandardInput.WriteAsync(JsonSerializer.Serialize(ContainerRecycleArchiveRetirementWire.Plan(p))); child.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await child.WaitForExitAsync(timeout.Token); } catch { child.Kill(entireProcessTree: true); throw; }
        Assert.Equal(0, child.ExitCode); Assert.Equal("", await child.StandardError.ReadToEndAsync()); Assert.Equal(p.Fingerprint, (await child.StandardOutput.ReadToEndAsync()).Trim());
    }

    [Theory]
    [InlineData("days")][InlineData("due")][InlineData("claim")][InlineData("elapsed")][InlineData("eligible")][InlineData("published")][InlineData("published-time")][InlineData("source")][InlineData("actor")][InlineData("trace")][InlineData("job")][InlineData("operation")][InlineData("owner")]
    public void FullMessageHashBindsRetirementFactsAndContext(string field)
    {
        var q = Request(); var changed = q.Clone();
        switch (field)
        {
            case "days": changed.Plan.Command.RetainDays++; break;
            case "due": changed.Plan.NotBeforeUnixMs++; break;
            case "claim": changed.Plan.ClaimDigest = new('F', 64); break;
            case "elapsed": changed.Plan.RetentionElapsed = true; break;
            case "eligible": changed.Plan.RetirementEligible = true; break;
            case "published": changed.Plan.PublishedIdentity.Inode++; break;
            case "published-time": changed.Plan.PublishedChangedAtUnixNs++; break;
            case "source": changed.Plan.Command.Archive.Plan.Command.Purge.Plan.Command.Source.Plan.Command.Location.Path += "-changed"; break;
            case "actor": changed.Context.ActorSubject = "other"; break;
            case "trace": changed.Context.TraceId = "other"; break;
            case "job": changed.Context.JobId = Guid.NewGuid().ToString("N"); break;
            case "operation": changed.OperationId = Guid.NewGuid().ToString("N"); break;
            case "owner": changed.Owner = new('F', 64); break;
        }
        Assert.NotEqual(ContainerRecycleArchiveRetirementBinding.Hash(q), ContainerRecycleArchiveRetirementBinding.Hash(changed));
        Assert.NotEqual(ContainerRecycleArchiveBinding.Hash(q), ContainerRecycleArchiveRetirementBinding.Hash(q));
    }

    [Fact]
    public void HmacDomainsDoNotAuthorizeLegacyCopyOrOtherMethods()
    {
        var q = Request(); var hash = ContainerRecycleArchiveRetirementBinding.Hash(q); var key = new byte[32]; RandomNumberGenerator.Fill(key);
        string Sign(string method, string payloadHash) => Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(ContainerRecycleArchiveRetirementBinding.Payload("123", "nonce", method, payloadHash))));
        var method = ContainerRecycleArchiveRetirementBinding.Service + "Apply";
        Assert.NotEqual(Sign(method, hash), Sign(ContainerRecycleArchiveRetirementBinding.Service + "Reserve", hash));
        Assert.NotEqual(Sign(method, hash), Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(ContainerRecycleArchiveBinding.Payload("123", "nonce", method, hash)))));
        CryptographicOperations.ZeroMemory(key);
    }

    [Theory]
    [InlineData(1U, 59U, true, false)][InlineData(1U, 60U, false, false)][InlineData(2U, 60U, true, false)][InlineData(1U, 60U, true, true)]
    public void CapabilityRequiresNewProtocolAndDedicatedOptIn(uint major, uint minor, bool advertised, bool expected)
    {
        var caps = new CapabilitiesResponse { ProtocolMajor = major, ProtocolMinor = minor };
        if (advertised) caps.Capabilities.Add(ContainerRecycleArchiveRetirementBinding.Capability);
        Assert.Equal(expected, ContainerRecycleArchiveRetirementBinding.IsSupported(caps));
    }

    private static ContainerRecycleArchiveRetirementReceipt Receipt(ContainerRecycleArchiveRetirementRequest q, string state) => new()
    {
        OperationId = q.OperationId, SourceOperationId = q.Plan.Command.Archive.Plan.Command.Purge.Plan.Command.Source.OperationId,
        ArchiveOperationId = q.Plan.Command.Archive.OperationId, Fingerprint = q.Plan.Fingerprint, State = state,
        SourceFenced = true, OwnedFence = state is "Reserved" or "Completed", NotBeforeUnixMs = q.Plan.NotBeforeUnixMs
    };

    [Theory][InlineData(false)][InlineData(true)]
    public void UnknownKeepsAbsentAndFalseEvidenceDistinct(bool present)
    {
        var q = Request(); var r = Receipt(q, "Unknown"); if (present) r.RetirementApplied = false;
        ContainerRecycleArchiveRetirementWire.Receipt(q, r); Assert.Equal(present, r.HasRetirementApplied);
        r.RetirementApplied = true; Assert.Throws<InvalidOperationException>(() => ContainerRecycleArchiveRetirementWire.Receipt(q, r));
    }

    [Fact]
    public void CompletedCannotBeAbsentFalseForeignOrBoundToAnotherSource()
    {
        var q = Request(); var r = Receipt(q, "Completed");
        Assert.Throws<InvalidOperationException>(() => ContainerRecycleArchiveRetirementWire.Receipt(q, r));
        r.RetirementApplied = false; Assert.Throws<InvalidOperationException>(() => ContainerRecycleArchiveRetirementWire.Receipt(q, r));
        r.RetirementApplied = true; ContainerRecycleArchiveRetirementWire.Receipt(q, r);
        r.OwnedFence = false; Assert.Throws<InvalidOperationException>(() => ContainerRecycleArchiveRetirementWire.Receipt(q, r));
        r.OwnedFence = true; r.SourceOperationId = Guid.NewGuid().ToString("N"); Assert.Throws<InvalidOperationException>(() => ContainerRecycleArchiveRetirementWire.Receipt(q, r));
    }

    [Fact]
    public async Task PreparedAdapterDoesNotAdvertiseRetirement()
    {
        var caps = await new AgentGrpcService(null!).GetCapabilities(new(), null!);
        Assert.True(caps.ProtocolMinor >= 59U); Assert.DoesNotContain(ContainerRecycleArchiveRetirementBinding.Capability, caps.Capabilities);
        var descriptor = Assert.Single(AgentReflection.Descriptor.Services, s => s.Name == "ContainerRecycleArchiveRetirementService");
        Assert.Equal(RetirementMethods, descriptor.Methods.Select(m => m.Name));
        Assert.All(descriptor.Methods, m => { Assert.False(m.IsClientStreaming); Assert.False(m.IsServerStreaming); });
    }

    [Fact]
    public void UnknownFieldsCannotHideOutsideCanonicalProjection()
    {
        var q = Request();
        var bytes = q.Plan.ToByteArray().Concat(new byte[] { 248, 7, 1 }).ToArray();
        var unknown = ContainerRecycleArchiveRetirementPlan.Parser.ParseFrom(bytes);
        Assert.Throws<ArgumentException>(() => ContainerRecycleArchiveRetirementWire.Fingerprint(unknown));
        var nested = q.Clone(); nested.Plan = unknown;
        Assert.NotEqual(ContainerRecycleArchiveRetirementBinding.Hash(q), ContainerRecycleArchiveRetirementBinding.Hash(nested));
    }

    [Theory][InlineData("Reserved")][InlineData("Aborted")]
    public void SchedulingNeverClaimsRetirementApplied(string state)
    {
        var q = Request(); var r = Receipt(q, state);
        Assert.Throws<InvalidOperationException>(() => ContainerRecycleArchiveRetirementWire.Receipt(q, r));
        r.RetirementApplied = false; ContainerRecycleArchiveRetirementWire.Receipt(q, r);
        r.RetirementApplied = true; Assert.Throws<InvalidOperationException>(() => ContainerRecycleArchiveRetirementWire.Receipt(q, r));
    }
}
