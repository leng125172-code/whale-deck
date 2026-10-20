using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.UnitTests.Application;

/// <summary>Pure purge material/signature rules; no Agent, durable jobs, root helper or deletion.</summary>
public sealed class FileTreePurgingBindingTests : IDisposable
{
    private readonly string _keyPath = Path.Combine(Path.GetTempPath(), "wd-purge-binding-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly FilePlanProtector _protector;
    public FilePlanProtector Protector => _protector;

    public FileTreePurgingBindingTests()
    {
        File.WriteAllBytes(_keyPath, _key);
        _protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Agent:CapabilityKeyFile"] = _keyPath }).Build());
    }
    public void Dispose() { File.Delete(_keyPath); CryptographicOperations.ZeroMemory(_key); }

    public static FileTreePurgingPlan Plan(string scope = "All", string kind = "Trash") => new(
        new(new("test.shared", "历史/目录"), new('A', 64), Guid.Parse("846516d5-6a48-4ba5-9468-cd61f55728ed"),
            new('B', 64), kind, new('C', 64), scope, new('7', 64)),
        new('D', 64), scope == "Archive" ? null : new(ulong.MaxValue, 9007199254740993UL),
        new('E', 64), new(9007199254740993UL, ulong.MaxValue), new('F', 64), new('1', 64), new('2', 64),
        new(ulong.MaxValue, 9007199254740993UL), FileTreePurgingValidation.MaximumArchiveBytes,
        new('3', 64), new('4', 64), 10000, FileTreePurgingValidation.MaximumBytes,
        scope == "Archive" ? null : new(9007199254740993UL, ulong.MaxValue),
        scope == "Archive" ? null : new(ulong.MaxValue, ulong.MaxValue), scope == "Archive" ? null : new('6', 64), new('5', 64));

    public FileTreePurgingOperation Operation(bool execution = true, string scope = "All", string kind = "Trash")
    {
        var plan = Plan(scope, kind); var actor = new TerminalActor("purge-unit-admin", null, [], new('A', 64), "purge-unit", null);
        var key = Guid.NewGuid().ToString("D"); var id = FileTreePurgingValidation.IntentId(actor, key);
        var now = FileTreePurgingValidation.Timestamp(DateTimeOffset.UtcNow); var expiry = now.AddMinutes(15); var ready = now.AddMinutes(5);
        return new() {
            Id = id, CaptureId = plan.Command.CaptureId, ActorSubject = actor.Subject, LoginBinding = actor.LoginBinding, IdempotencyKey = key,
            OwnerHash = FileManagementService.Hash("file-owner-v1:" + actor.Subject + ":" + actor.LoginBinding), SourceHash = new FilePathScope(plan.Command.Source).Hash,
            CommandJson = JsonSerializer.Serialize(plan.Command), CreatedAtUtc = now, PlanningExpiresAtUtc = expiry,
            RequestHash = _protector.FingerprintRequest(new FileTreePurgingRequestBinding(id, actor.Subject, actor.LoginBinding, plan.Command, now, expiry)),
            PlanJson = JsonSerializer.Serialize(plan), PlanHash = _protector.FingerprintPlan(new FileTreePurgingPlanBinding(id, actor.Subject, actor.LoginBinding, plan, ready)),
            ReadyExpiresAtUtc = ready, ExecutionJobId = execution ? FileTreePurgingValidation.ExecutionId(id) : null,
            ExecutionExpiresAtUtc = execution ? ready.AddMinutes(15) : null, State = execution ? "PurgingQueued" : "Ready" };
    }

    private static FileTreePurgingReceipt Receipt(FileTreePurgingOperation row, string state)
    {
        var plan = FileTreePurgingValidation.ReadPlan(row);
        return new(row.ExecutionJobId!.Value, "DirectoryPurge", state, plan.Fingerprint, row.CaptureId, plan.Command.Scope,
            plan.EntryCount, plan.TotalBytes, plan.ArchiveBytes, state != "Aborted",
            state == "Completed" && FileTreePurgingValidation.Selects(plan.Command.Scope, "Archive"),
            state == "Completed" && FileTreePurgingValidation.Selects(plan.Command.Scope, "Originals"));
    }
    private void AttachReceipt(FileTreePurgingOperation row, string state)
    {
        var receipt = Receipt(row, state);
        row.State = state == "Completed" ? "Succeeded" : state == "Aborted" ? "Canceled" : "Unknown";
        row.RootStartedAtUtc = receipt.DeletionStarted ? row.CreatedAtUtc.AddSeconds(1) : null;
        row.DeletionStarted = receipt.DeletionStarted; row.ArchiveRemoved = receipt.ArchiveRemoved; row.OriginalsRemoved = receipt.OriginalsRemoved;
        row.ReceiptJson = JsonSerializer.Serialize(receipt); row.ReceiptObservedAtUtc = row.CreatedAtUtc.AddSeconds(2);
        row.ReceiptHash = _protector.FingerprintReceipt(FileTreePurgingValidation.ReceiptBinding(row));
    }
    private string Mac(string payload, string domain) => Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(domain + "\n" + payload)));

    [Theory]
    [InlineData("Archive", "Trash")][InlineData("Archive", "Overwrite")]
    [InlineData("Originals", "Trash")][InlineData("Originals", "Overwrite")]
    [InlineData("All", "Trash")][InlineData("All", "Overwrite")]
    public void EveryScopeAndCaptureKindRoundTripsEighteenFieldsAndCompleteUnsignedIdentities(string scope, string kind)
    {
        var plan = Plan(scope, kind); FileTreePurgingValidation.Plan(plan);
        var json = JsonSerializer.Serialize(plan); using var document = JsonDocument.Parse(json);
        Assert.Equal(18, document.RootElement.EnumerateObject().Count());
        Assert.Equal(plan, JsonSerializer.Deserialize<FileTreePurgingPlan>(json, FileTreePurgingValidation.JsonOptions));
        Assert.Equal(ulong.MaxValue, plan.StoreIdentity.Inode); Assert.Equal(9007199254740993UL, plan.StoreIdentity.Device);
        Assert.Equal(ulong.MaxValue, plan.ArchiveIdentity.Device); Assert.Equal(9007199254740993UL, plan.ArchiveIdentity.Inode);
        foreach (var field in new[] { "SourceParent", "RetainedIdentity", "StagingIdentity", "RetainedTreeSha256" })
            Assert.Equal(scope == "Archive", document.RootElement.GetProperty(field).ValueKind == JsonValueKind.Null);
        Assert.True(FileTreePurgingValidation.HasValidBinding(Operation(false, scope, kind), _protector));
        Assert.True(FileTreePurgingValidation.HasValidBinding(Operation(true, scope, kind), _protector));
    }

    [Theory]
    [InlineData("SourceParent")][InlineData("RetainedIdentity")][InlineData("StagingIdentity")][InlineData("RetainedTreeSha256")]
    public void ArchiveOnlyCannotAcquireOriginalProofAndOriginalsCannotLoseIt(string field)
    {
        var archive = Plan("Archive"); var originals = Plan("Originals");
        var archiveChanged = field switch {
            "SourceParent" => archive with { SourceParent = new(1, 2) }, "RetainedIdentity" => archive with { RetainedIdentity = new(1, 2) },
            "StagingIdentity" => archive with { StagingIdentity = new(1, 2) }, _ => archive with { RetainedTreeSha256 = "" } };
        var originalChanged = field switch {
            "SourceParent" => originals with { SourceParent = null }, "RetainedIdentity" => originals with { RetainedIdentity = null },
            "StagingIdentity" => originals with { StagingIdentity = null }, _ => originals with { RetainedTreeSha256 = null } };
        Assert.Throws<ArgumentException>(() => FileTreePurgingValidation.Plan(archiveChanged));
        Assert.Throws<ArgumentException>(() => FileTreePurgingValidation.Plan(originalChanged));
    }

    [Fact]
    public void ExpandedTreeAndArchiveUseIndependentSixteenAndSeventeenGiBLimits()
    {
        Assert.Equal(16L * 1024 * 1024 * 1024, FileTreePurgingValidation.MaximumBytes);
        Assert.Equal(17L * 1024 * 1024 * 1024, FileTreePurgingValidation.MaximumArchiveBytes);
        var plan = Plan(); FileTreePurgingValidation.Plan(plan); FileTreePurgingValidation.Plan(plan with { TotalBytes = 0 });
        foreach (var invalid in new[] { plan with { TotalBytes = plan.TotalBytes + 1 }, plan with { TotalBytes = -1 },
            plan with { ArchiveBytes = plan.ArchiveBytes + 1 }, plan with { ArchiveBytes = 0 },
            plan with { EntryCount = 10001 }, plan with { EntryCount = 0 }, plan with { StoreIdentity = new(1, 0) }, plan with { ArchiveIdentity = new(1, 0) } })
            Assert.Throws<ArgumentException>(() => FileTreePurgingValidation.Plan(invalid));
    }

    [Theory]
    [InlineData("captureOwner")][InlineData("source")][InlineData("expectedVersion")][InlineData("manifestSha256")]
    [InlineData("catalogueProofHash")][InlineData("ownerHash")][InlineData("planJson")][InlineData("hostPath")]
    public void HttpRequestContainsOnlyCaptureIdAndScopeAndNeverHistoricalAuthority(string field)
    {
        var request = new CreateFileTreePurgingCommand(Plan().Command.CaptureId, "All"); FileTreePurgingValidation.Request(request);
        var node = JsonSerializer.SerializeToNode(request)!; Assert.Equal(2, node.AsObject().Count); node[field] = "not-http-authority";
        Assert.Throws<JsonException>(() => node.Deserialize<CreateFileTreePurgingCommand>());
        Assert.DoesNotContain(typeof(FileTreePurgingCommandDto).GetProperties(), property => property.Name is "CaptureOwner" or "ExpectedVersion" or "CatalogueProofHash" or "ManifestSha256");
    }

    [Theory]
    [InlineData(null)][InlineData("")][InlineData("archive")][InlineData("Original")][InlineData("All\n")]
    public void InvalidScopesAndEmptyCaptureCannotAliasAValidRequest(string? scope)
    {
        Assert.False(FileTreePurgingValidation.Scope(scope));
        Assert.Throws<ArgumentException>(() => FileTreePurgingValidation.Request(new(Plan().Command.CaptureId, scope!)));
        Assert.Throws<ArgumentException>(() => FileTreePurgingValidation.Request(new(Guid.Empty, "All")));
    }

    [Fact]
    public void CaptureLockIsSharedButPurgeIntentAndExecutionDoNotReuseRestoreOrOverwriteDomains()
    {
        var row = Operation(); var actor = new TerminalActor(row.ActorSubject, null, [], row.LoginBinding, "unit", null);
        Assert.Equal(FileTreeRestoringValidation.CaptureToken(row.CaptureId), FileTreePurgingValidation.CaptureToken(row.CaptureId));
        Assert.Equal(FileTreeOverwriteRestoringValidation.CaptureToken(row.CaptureId), FileTreePurgingValidation.CaptureToken(row.CaptureId));
        Assert.NotEqual(FileTreeRestoringValidation.IntentId(actor, row.IdempotencyKey), row.Id);
        Assert.NotEqual(FileTreeOverwriteRestoringValidation.IntentId(actor, row.IdempotencyKey), row.Id);
        Assert.NotEqual(FileTreeRestoringValidation.ExecutionId(row.Id), row.ExecutionJobId);
        Assert.NotEqual(FileTreeOverwriteRestoringValidation.ExecutionId(row.Id), row.ExecutionJobId);
        Assert.NotEqual(row.Id, FileTreePurgingValidation.IntentId(actor with { LoginBinding = new('B', 64) }, row.IdempotencyKey));
        Assert.Throws<ArgumentException>(() => FileTreePurgingValidation.ExecutionId(Guid.Empty));
    }

    [Theory]
    [InlineData("Source")][InlineData("ExpectedVersion")][InlineData("CaptureId")][InlineData("CaptureOwner")]
    [InlineData("CaptureKind")][InlineData("ManifestSha256")][InlineData("Scope")][InlineData("CatalogueProofHash")]
    public void RequestMacBindsEveryCatalogueResolvedInputFact(string field)
    {
        var request = FileTreePurgingValidation.RequestBinding(Operation()); var command = request.Command;
        var changed = field switch {
            "Source" => command with { Source = new("test.shared", "different") }, "ExpectedVersion" => command with { ExpectedVersion = new('8', 64) },
            "CaptureId" => command with { CaptureId = Guid.NewGuid() }, "CaptureOwner" => command with { CaptureOwner = new('8', 64) },
            "CaptureKind" => command with { CaptureKind = "Overwrite" }, "ManifestSha256" => command with { ManifestSha256 = new('8', 64) },
            "Scope" => command with { Scope = "Archive" }, _ => command with { CatalogueProofHash = new('8', 64) } };
        Assert.NotEqual(_protector.FingerprintRequest(request), _protector.FingerprintRequest(request with { Command = changed }));
    }

    [Theory]
    [InlineData("Actor")][InlineData("LoginBinding")][InlineData("Id")][InlineData("CreatedAtUtc")][InlineData("PlanningExpiresAtUtc")]
    public void RequestMacBindsCurrentActorLoginIdentityAndBothTimes(string field)
    {
        var request = FileTreePurgingValidation.RequestBinding(Operation());
        var changed = field switch {
            "Actor" => request with { Actor = "other-admin" }, "LoginBinding" => request with { LoginBinding = new('B', 64) },
            "Id" => request with { Id = Guid.NewGuid() }, "CreatedAtUtc" => request with { CreatedAtUtc = request.CreatedAtUtc.AddMicroseconds(1) },
            _ => request with { PlanningExpiresAtUtc = request.PlanningExpiresAtUtc.AddMicroseconds(1) } };
        Assert.NotEqual(_protector.FingerprintRequest(request), _protector.FingerprintRequest(changed));
    }

    [Theory]
    [InlineData("ActorSubject")][InlineData("LoginBinding")][InlineData("CaptureId")][InlineData("CreatedAtUtc")]
    [InlineData("PlanningExpiresAtUtc")][InlineData("OwnerHash")][InlineData("SourceHash")][InlineData("RequestHash")]
    public void PersistedActorCaptureTimeAndRequestProofTamperingFailsBindingVerification(string field)
    {
        var original = Operation(false); var node = JsonSerializer.SerializeToNode(original)!;
        if (field == "ActorSubject") node[field] = "other-admin";
        else if (field == "CaptureId") node[field] = Guid.NewGuid();
        else if (field == "CreatedAtUtc") node[field] = original.CreatedAtUtc.AddMicroseconds(1);
        else if (field == "PlanningExpiresAtUtc") node[field] = original.PlanningExpiresAtUtc.AddMicroseconds(1);
        else node[field] = new string('8', 64);
        Assert.False(FileTreePurgingValidation.HasValidBinding(node.Deserialize<FileTreePurgingOperation>()!, _protector));
    }

    [Theory]
    [InlineData("SourcePolicyHash")][InlineData("SourceParent")][InlineData("StorageHash")][InlineData("StoreIdentity")]
    [InlineData("CaptureMetadataHash")][InlineData("OriginReceiptHash")][InlineData("ManifestFileSha256")][InlineData("ArchiveIdentity")]
    [InlineData("ArchiveBytes")][InlineData("ArchiveSha256")][InlineData("ContentSha256")][InlineData("EntryCount")][InlineData("TotalBytes")]
    [InlineData("RetainedIdentity")][InlineData("StagingIdentity")][InlineData("RetainedTreeSha256")][InlineData("Fingerprint")]
    public void ReadySealBindsEveryFrozenArchiveStorageAndPrivateOriginalProof(string field)
    {
        var row = Operation(false); var plan = Plan();
        var changed = field switch {
            "SourcePolicyHash" => plan with { SourcePolicyHash = new('8', 64) }, "SourceParent" => plan with { SourceParent = new(1, 2) },
            "StorageHash" => plan with { StorageHash = new('8', 64) }, "StoreIdentity" => plan with { StoreIdentity = new(1, 2) },
            "CaptureMetadataHash" => plan with { CaptureMetadataHash = new('8', 64) }, "OriginReceiptHash" => plan with { OriginReceiptHash = new('8', 64) },
            "ManifestFileSha256" => plan with { ManifestFileSha256 = new('8', 64) }, "ArchiveIdentity" => plan with { ArchiveIdentity = new(1, 2) },
            "ArchiveBytes" => plan with { ArchiveBytes = plan.ArchiveBytes - 1 }, "ArchiveSha256" => plan with { ArchiveSha256 = new('8', 64) },
            "ContentSha256" => plan with { ContentSha256 = new('8', 64) }, "EntryCount" => plan with { EntryCount = plan.EntryCount - 1 },
            "TotalBytes" => plan with { TotalBytes = plan.TotalBytes - 1 }, "RetainedIdentity" => plan with { RetainedIdentity = new(1, 2) },
            "StagingIdentity" => plan with { StagingIdentity = new(1, 2) }, "RetainedTreeSha256" => plan with { RetainedTreeSha256 = new('8', 64) },
            _ => plan with { Fingerprint = new('8', 64) } };
        FileTreePurgingValidation.Plan(changed); row.PlanJson = JsonSerializer.Serialize(changed);
        Assert.False(FileTreePurgingValidation.HasValidBinding(row, _protector));
    }

    [Fact]
    public void RequestReadyReceiptAndConfirmationHaveSeparateExplicitSigningPurposes()
    {
        var row = Operation(); AttachReceipt(row, "Unknown");
        var request = FileTreePurgingValidation.RequestBinding(row); var plan = FileTreePurgingValidation.PlanBinding(row);
        var receipt = FileTreePurgingValidation.ReceiptBinding(row);
        Assert.Equal(Mac(JsonSerializer.Serialize(request), "whaledeck-tree-purge-request-v1"), _protector.FingerprintRequest(request));
        Assert.Equal(Mac(JsonSerializer.Serialize(plan), "whaledeck-tree-purge-ready-v1"), _protector.FingerprintPlan(plan));
        Assert.Equal(Mac(JsonSerializer.Serialize(receipt), "whaledeck-tree-purge-receipt-v1"), _protector.FingerprintReceipt(receipt));
        Assert.NotEqual(Mac(JsonSerializer.Serialize(request), "whaledeck-tree-overwrite-restore-request-v1"), _protector.FingerprintRequest(request));
        Assert.NotEqual(Mac(JsonSerializer.Serialize(plan), "whaledeck-tree-purge-request-v1"), _protector.FingerprintPlan(plan));
        Assert.NotEqual(Mac(JsonSerializer.Serialize(receipt), "whaledeck-tree-purge-ready-v1"), _protector.FingerprintReceipt(receipt));
        var claims = Confirmation(row); var token = _protector.SignConfirmation(claims); var parts = token.Split('.');
        Assert.Equal(Mac(parts[0], "whaledeck-tree-purge-confirmation-v1"), parts[1]);
        foreach (var domain in new[] { "whaledeck-tree-restore-confirmation-v1", "whaledeck-tree-overwrite-restore-confirmation-v1", "whaledeck-tree-purge-receipt-v1" })
            Assert.Throws<ArgumentException>(() => _protector.VerifyPurgingConfirmation(parts[0] + "." + Mac(parts[0], domain)));
        Assert.Equal(claims, _protector.VerifyPurgingConfirmation(token));
    }

    private static FileTreePurgingConfirmation Confirmation(FileTreePurgingOperation row) => new(row.Id, row.ActorSubject, row.LoginBinding,
        row.CaptureId, FileTreePurgingValidation.ReadCommand(row).Scope, Plan().Fingerprint, row.ReadyExpiresAtUtc!.Value);

    [Theory]
    [InlineData("Id")][InlineData("Actor")][InlineData("LoginBinding")][InlineData("CaptureId")][InlineData("Scope")][InlineData("Fingerprint")][InlineData("ExpiresAtUtc")]
    public void ConfirmationPayloadTamperingCannotReuseTheSameSignature(string field)
    {
        var row = Operation(false); var token = _protector.SignConfirmation(Confirmation(row)); var parts = token.Split('.');
        var node = JsonNode.Parse(Convert.FromBase64String(parts[0]))!;
        if (field is "Id" or "CaptureId") node[field] = Guid.NewGuid();
        else if (field == "Scope") node[field] = "Archive";
        else if (field == "ExpiresAtUtc") node[field] = row.ReadyExpiresAtUtc!.Value.AddMicroseconds(1);
        else node[field] = field == "Actor" ? "other-admin" : new string('8', 64);
        var changed = Convert.ToBase64String(Encoding.UTF8.GetBytes(node.ToJsonString())) + "." + parts[1];
        Assert.Throws<ArgumentException>(() => _protector.VerifyPurgingConfirmation(changed));
    }

    [Fact]
    public void EvenAuthenticatedConfirmationCannotContainUnrecognisedAuthorityFields()
    {
        var node = JsonSerializer.SerializeToNode(Confirmation(Operation(false)))!; node["OriginalOwner"] = "not-a-grant";
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(node.ToJsonString()));
        Assert.Throws<ArgumentException>(() => _protector.VerifyPurgingConfirmation(payload + "." + Mac(payload, "whaledeck-tree-purge-confirmation-v1")));
    }

    [Theory]
    [InlineData("Archive", "Completed")][InlineData("Originals", "Completed")][InlineData("All", "Completed")]
    [InlineData("Archive", "Aborted")][InlineData("Originals", "Aborted")][InlineData("All", "Aborted")]
    [InlineData("Archive", "Unknown")][InlineData("Originals", "Unknown")][InlineData("All", "Unknown")]
    public void TerminalRootReceiptIsSealedWithExactScopeCountsAndSelectedEffects(string scope, string state)
    {
        var row = Operation(true, scope); AttachReceipt(row, state);
        var receipt = FileTreePurgingValidation.ReadReceipt(row);
        FileTreePurgingValidation.Receipt(row.ExecutionJobId!.Value, Plan(scope), receipt);
        Assert.True(FileTreePurgingValidation.HasValidBinding(row, _protector));
        FileTreePurgingValidation.MonotonicReceipt(receipt, receipt);
        Assert.Throws<FileTransferRejectedException>(() => FileTreePurgingValidation.Receipt(row.Id, Plan(scope), receipt));
        Assert.Throws<FileTransferRejectedException>(() => FileTreePurgingValidation.Receipt(row.ExecutionJobId.Value, Plan(scope), receipt with { ArchiveBytes = receipt.ArchiveBytes - 1 }));
    }

    [Theory]
    [InlineData("OperationId")][InlineData("Kind")][InlineData("State")][InlineData("Fingerprint")][InlineData("CaptureId")][InlineData("Scope")]
    [InlineData("EntryCount")][InlineData("TotalBytes")][InlineData("ArchiveBytes")][InlineData("DeletionStarted")][InlineData("ArchiveRemoved")][InlineData("OriginalsRemoved")]
    public void ObservedTerminalReceiptCannotAddLoseOrReplaceAnyFact(string field)
    {
        var row = Operation(); AttachReceipt(row, "Completed"); var previous = FileTreePurgingValidation.ReadReceipt(row);
        var next = field switch {
            "OperationId" => previous with { OperationId = Guid.NewGuid() }, "Kind" => previous with { Kind = "DirectoryRestore" },
            "State" => previous with { State = "Unknown" }, "Fingerprint" => previous with { Fingerprint = new('8', 64) },
            "CaptureId" => previous with { CaptureId = Guid.NewGuid() }, "Scope" => previous with { Scope = "Archive" },
            "EntryCount" => previous with { EntryCount = previous.EntryCount - 1 }, "TotalBytes" => previous with { TotalBytes = previous.TotalBytes - 1 },
            "ArchiveBytes" => previous with { ArchiveBytes = previous.ArchiveBytes - 1 }, "DeletionStarted" => previous with { DeletionStarted = false },
            "ArchiveRemoved" => previous with { ArchiveRemoved = false }, _ => previous with { OriginalsRemoved = false } };
        Assert.Throws<InvalidOperationException>(() => FileTreePurgingValidation.MonotonicReceipt(previous, next));
        Assert.Throws<InvalidOperationException>(() => FileTreePurgingValidation.MonotonicReceipt(next, previous));
        row.ReceiptJson = JsonSerializer.Serialize(next);
        Assert.False(FileTreePurgingValidation.HasValidBinding(row, _protector));
    }

    [Fact]
    public void ReceiptMacBindsObserverIdentityLoginAndObservationTime()
    {
        var row = Operation(); AttachReceipt(row, "Unknown"); var binding = FileTreePurgingValidation.ReceiptBinding(row);
        foreach (var changed in new[] { binding with { Id = Guid.NewGuid() }, binding with { Actor = "other-admin" },
            binding with { LoginBinding = new('B', 64) }, binding with { ObservedAtUtc = binding.ObservedAtUtc.AddMicroseconds(1) } })
            Assert.NotEqual(_protector.FingerprintReceipt(binding), _protector.FingerprintReceipt(changed));
        row.ReceiptObservedAtUtc = row.ReceiptObservedAtUtc!.Value.AddMicroseconds(1);
        Assert.False(FileTreePurgingValidation.HasValidBinding(row, _protector));
    }

    [Fact]
    public void UnknownAbortedAndCompletedCannotInventOrLoseSelectedDeletionFacts()
    {
        var row = Operation(); var plan = Plan(); var unknown = Receipt(row, "Unknown");
        foreach (var changed in new[] { unknown with { DeletionStarted = false }, Receipt(row, "Aborted") with { DeletionStarted = true },
            Receipt(row, "Completed") with { ArchiveRemoved = false }, Receipt(row, "Completed") with { OriginalsRemoved = false },
            unknown with { Scope = "Originals", ArchiveRemoved = true }, unknown with { Scope = "Archive", OriginalsRemoved = true } })
            Assert.Throws<FileTransferRejectedException>(() => FileTreePurgingValidation.Receipt(row.ExecutionJobId!.Value, plan, changed));
    }

    [Fact]
    public void PersistedDuplicateUnknownOrMalformedPlanMaterialIsRejectedBeforeUse()
    {
        var row = Operation(false); var original = row.PlanJson!;
        row.PlanJson = original[..^1] + ",\"Shell\":\"do-not-run\"}";
        Assert.False(FileTreePurgingValidation.HasValidBinding(row, _protector));
        row.PlanJson = original.Replace("\"Fingerprint\":", "\"Fingerprint\":\"" + Plan().Fingerprint + "\",\"Fingerprint\":", StringComparison.Ordinal);
        Assert.False(FileTreePurgingValidation.HasValidBinding(row, _protector));
        foreach (var malformed in new[] { "null", "{}", "[]", "not-json" })
        {
            row.PlanJson = malformed; Assert.False(FileTreePurgingValidation.HasValidBinding(row, _protector));
        }
    }

    [Theory]
    [InlineData("Completed", "Canceled")][InlineData("Completed", "Unknown")][InlineData("Completed", "Purging")]
    [InlineData("Unknown", "Canceled")][InlineData("Unknown", "Failed")][InlineData("Unknown", "Succeeded")]
    [InlineData("Aborted", "Succeeded")][InlineData("Aborted", "Unknown")][InlineData("Aborted", "PurgingQueued")]
    public void DatabaseStateCannotContradictAnAuthenticatedTerminalRootOutcome(string receiptState, string rowState)
    {
        var row = Operation(); AttachReceipt(row, receiptState); row.State = rowState;
        Assert.False(FileTreePurgingValidation.HasValidBinding(row, _protector));
    }
    [Theory]
    [InlineData("Canceled")][InlineData("Failed")][InlineData("Expired")][InlineData("Succeeded")][InlineData("Acknowledged")]
    public void MissingRootResponseCannotBeSignedOffAsATerminalOutcome(string state)
    {
        var row = Operation(); row.RootStartedAtUtc = row.CreatedAtUtc.AddSeconds(1); row.State = state;
        Assert.False(FileTreePurgingValidation.HasValidBinding(row, _protector));
    }
}
