using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.UnitTests.Application;

public sealed class FileRecoveryTests : IDisposable
{
    private readonly string _key = Path.Combine(Path.GetTempPath(), "wd-current-admin-recovery-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _material = RandomNumberGenerator.GetBytes(32);
    private readonly FilePlanProtector _protector;
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
    private static TerminalActor Actor => new("current-admin", null, [], new('B', 64), "recovery-test", null);
    public FileRecoveryTests()
    {
        File.WriteAllBytes(_key, _material);
        _protector = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = _key }).Build());
    }
    public void Dispose() { CryptographicOperations.ZeroMemory(_material); File.Delete(_key); }
    private static FileRecoveryPlanClaims Claims(string kind = FileRecoveryKinds.TreeMove)
    {
        var id = Guid.Parse("01234567-1234-4234-8234-0123456789ab");
        var execution = kind switch { FileRecoveryKinds.TreeMove => FileTreeMoveValidation.ExecutionId(id),
            FileRecoveryKinds.TreeCopy => FileTreeCopyValidation.ExecutionId(id), FileRecoveryKinds.TreePacking => FileTreePackingValidation.ExecutionId(id), FileRecoveryKinds.TreeUnpacking => FileTreeUnpackingValidation.ExecutionId(id), _ => id };
        return new(Guid.Parse("98765432-4321-4321-8321-9876543210ab"), Actor.Subject, Actor.LoginBinding,
            new(kind, id, execution, 17, new('D', 64)), new('E', 64), "Unknown", "AcknowledgeUnknown", "OriginalSessionLost", Now, Now.AddMinutes(5));
    }
    [Theory]
    [InlineData(FileRecoveryKinds.Upload)][InlineData(FileRecoveryKinds.Mutation)][InlineData(FileRecoveryKinds.Archive)]
    [InlineData(FileRecoveryKinds.TreeCopy)][InlineData(FileRecoveryKinds.TreeMove)]
    [InlineData(FileRecoveryKinds.TreePacking)]
    [InlineData(FileRecoveryKinds.TreeUnpacking)]
    public void AllKindsHaveIndependentCurrentAdminClaimsAndExactOriginalExecution(string kind)
    {
        var claims = Claims(kind); var token = _protector.SignRecovery(claims);
        Assert.Equal(claims, _protector.VerifyRecovery(token)); FileRecoveryValidation.RequireCurrentActor(claims, Actor, Now);
        Assert.Throws<ArgumentException>(() => _protector.SignRecovery(claims with { Target = claims.Target with { ExecutionJobId = Guid.NewGuid() } }));
    }
    [Theory]
    [InlineData("unknown")][InlineData("../tree-move")][InlineData("TreeMove")][InlineData("")][InlineData("tree-move\n")]
    public void NeitherKindPathsNorCaseAliasesAreAccepted(string kind) => Assert.Throws<ArgumentException>(() => FileRecoveryValidation.Kind(kind));
    [Fact]
    public void OriginalLoginOrAnotherCurrentLoginNeverInheritsAConfirmation()
    {
        var claims = Claims();
        Assert.Throws<UnauthorizedAccessException>(() => FileRecoveryValidation.RequireCurrentActor(claims, Actor with { Subject = "original-actor" }, Now));
        Assert.Throws<UnauthorizedAccessException>(() => FileRecoveryValidation.RequireCurrentActor(claims, Actor with { LoginBinding = new('A', 64) }, Now));
        Assert.Throws<InvalidOperationException>(() => FileRecoveryValidation.RequireCurrentActor(claims, Actor, Now.AddMinutes(5)));
        Assert.Throws<InvalidOperationException>(() => FileRecoveryValidation.RequireCurrentActor(claims, Actor, Now.AddMinutes(-1)));
    }
    [Theory]
    [InlineData("Missing")][InlineData("Aborted")][InlineData("Unknown")]
    public void AcknowledgmentOfUncertaintyCannotBeRecastAsCompletion(string state)
    {
        var claims = Claims() with { RootState = state }; FileRecoveryValidation.Claims(claims);
        Assert.Throws<ArgumentException>(() => _protector.SignRecovery(claims with { Disposition = "ReconcileCompleted" }));
        Assert.Throws<ArgumentException>(() => _protector.SignRecovery(claims with { RootState = "Completed" }));
    }
    [Fact]
    public void VerifiedCompletionIsASeparateDispositionNotUnknownAcceptance()
    {
        var claims = Claims() with { RootState = "Completed", Disposition = "ReconcileCompleted" };
        Assert.Equal(claims, _protector.VerifyRecovery(_protector.SignRecovery(claims)));
        Assert.Throws<ArgumentException>(() => _protector.SignRecovery(claims with { RootState = "Applying" }));
        Assert.Throws<ArgumentException>(() => _protector.SignRecovery(claims with { Disposition = "Apply" }));
    }
    [Theory]
    [InlineData("Actor")][InlineData("LoginBinding")][InlineData("ObservationHash")][InlineData("ReasonCode")][InlineData("RootState")]
    public void ChangedClaimPayloadCannotReuseTheSameMac(string name)
    {
        var token = _protector.SignRecovery(Claims()); var parts = token.Split('.');
        var node = JsonNode.Parse(Convert.FromBase64String(parts[0]))!; node[name] = "different";
        var changed = Convert.ToBase64String(Encoding.UTF8.GetBytes(node.ToJsonString())) + "." + parts[1];
        Assert.Throws<ArgumentException>(() => _protector.VerifyRecovery(changed));
    }
    [Fact]
    public void OldCopyAndMoveTokensAndMacDomainsCannotAuthorizeAdministratorRecovery()
    {
        var claims = Claims(); var copy = _protector.SignConfirmation(new FileTreeCopyConfirmation(claims.Id, claims.Actor, claims.LoginBinding, claims.ObservationHash, claims.ExpiresAtUtc));
        var move = _protector.SignConfirmation(new FileTreeMoveConfirmation(claims.Id, claims.Actor, claims.LoginBinding, claims.ObservationHash, claims.ExpiresAtUtc));
        Assert.Throws<ArgumentException>(() => _protector.VerifyRecovery(copy)); Assert.Throws<ArgumentException>(() => _protector.VerifyRecovery(move));
        var recovery = _protector.SignRecovery(claims);
        Assert.Throws<ArgumentException>(() => _protector.VerifyConfirmation(recovery)); Assert.Throws<ArgumentException>(() => _protector.VerifyMoveConfirmation(recovery));
    }
    [Fact]
    public void StrictSignedPayloadRejectsUnknownMembersAndLongOrControlBearingReasons()
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(Claims()))!; node["OriginalLogin"] = "not-an-authorization";
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(node.ToJsonString()));
        var mac = Convert.ToHexString(HMACSHA256.HashData(_material, Encoding.UTF8.GetBytes("whaledeck-current-administrator-file-recovery-v1\n" + payload)));
        Assert.Throws<ArgumentException>(() => _protector.VerifyRecovery(payload + "." + mac));
        Assert.Throws<ArgumentException>(() => _protector.SignRecovery(Claims() with { ReasonCode = "reason\nwith-control" }));
        Assert.Throws<ArgumentException>(() => _protector.SignRecovery(Claims() with { ReasonCode = new('x', 8192) }));
        Assert.Throws<ArgumentException>(() => _protector.SignRecovery(Claims() with { ExpiresAtUtc = Now.AddMinutes(6) }));
        Assert.Throws<ArgumentException>(() => _protector.VerifyRecovery(new('x', 8193)));
    }
    [Fact]
    public void EvenCorrectlySignedDuplicateOrMissingVersionFieldsAreRejected()
    {
        string Seal(string json)
        {
            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            return payload + "." + Convert.ToHexString(HMACSHA256.HashData(_material, Encoding.UTF8.GetBytes("whaledeck-current-administrator-file-recovery-v1\n" + payload)));
        }
        var json = JsonSerializer.Serialize(Claims());
        var duplicate = json.Replace("\"OriginalVersion\":17", "\"OriginalVersion\":17,\"OriginalVersion\":0", StringComparison.Ordinal);
        Assert.NotEqual(json, duplicate); Assert.Throws<ArgumentException>(() => _protector.VerifyRecovery(Seal(duplicate)));
        var missing = json.Replace("\"OriginalVersion\":17,", "", StringComparison.Ordinal);
        Assert.NotEqual(json, missing); Assert.Throws<ArgumentException>(() => _protector.VerifyRecovery(Seal(missing)));
    }
    [Fact]
    public void RecoveryRegistrationAndExecutionHaveTheirOwnDomainsAndCanonicalKeys()
    {
        var key = Guid.NewGuid().ToString("D"); var id = FileRecoveryValidation.RegistrationId(Actor, key);
        Assert.NotEqual(FileTreeCopyValidation.IntentId(Actor, key), id); Assert.NotEqual(FileTreeMoveValidation.IntentId(Actor, key), id);
        Assert.NotEqual(FileRecoveryValidation.ExecutionId(id), FileTreeMoveValidation.ExecutionId(id));
        Assert.NotEqual(id, FileRecoveryValidation.RegistrationId(Actor with { LoginBinding = new('C', 64) }, key));
        Assert.Throws<ArgumentException>(() => FileRecoveryValidation.RegistrationId(Actor, "not-a-uuid"));
        Assert.Throws<ArgumentException>(() => FileRecoveryValidation.ExecutionId(Guid.Empty));
    }
    [Theory]
    [InlineData(FileRecoveryKinds.Upload)][InlineData(FileRecoveryKinds.Mutation)][InlineData(FileRecoveryKinds.Archive)]
    [InlineData(FileRecoveryKinds.TreeCopy)][InlineData(FileRecoveryKinds.TreeMove)]
    [InlineData(FileRecoveryKinds.TreePacking)]
    [InlineData(FileRecoveryKinds.TreeUnpacking)]
    public void CursorPreservesAllTimestampTicksAndSameTimestampKindAndGuid(string kind)
    {
        var cursor = new FileRecoveryCursor(Now.AddTicks(1234567), kind, Guid.NewGuid()); var encoded = FileRecoveryValidation.EncodeCursor(cursor);
        Assert.Equal(cursor, FileRecoveryValidation.DecodeCursor(encoded)); Assert.True(encoded.Length < 160);
        Assert.Throws<ArgumentException>(() => FileRecoveryValidation.DecodeCursor(encoded + "="));
    }
    [Theory]
    [InlineData("../escape")][InlineData("%%%")][InlineData("AAAA")][InlineData("line\nbreak")]
    public void BadCursorNeverReachesSql(string value) => Assert.Throws<ArgumentException>(() => FileRecoveryValidation.DecodeCursor(value));
    [Fact]
    public void PublicInventoryDoesNotSerializeOriginalOwnershipSecretsOrPlans()
    {
        var row = new FileRecoveryCandidateDto("tree-move", Guid.NewGuid(), Guid.NewGuid(), 2, Now, null, true, "InvalidBinding", null, null, null);
        var value = JsonSerializer.SerializeToElement(row);
        Assert.DoesNotContain(value.EnumerateObject(), property => new[] { "OwnerHash", "LoginBinding", "PlanJson", "PlanHash", "RequestHash", "CommandJson" }.Contains(property.Name));
        Assert.Equal(JsonValueKind.Null, value.GetProperty("HasRetainedObjects").ValueKind);
    }
    [Fact]
    public async Task CurrentAuthorityIsCheckedBeforeAndAfterInventoryReadNotOriginalOwnership()
    {
        var identity = new Identity(); var inventory = new Inventory();
        var service = new FileRecoveryInventoryService(new(null!, null!, null!, identity, TimeProvider.System), inventory);
        var page = await service.ListAsync(Actor, null, 25, default); Assert.Empty(page.Entries); Assert.Equal(2, identity.Calls);
        identity.Calls = 0; identity.Admin = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync(Actor, null, 25, default)); Assert.Equal(1, inventory.Reads);
        identity.Admin = true; identity.Calls = 0; identity.RevokeOnSecond = true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync(Actor, null, 25, default)); Assert.Equal(2, inventory.Reads);
        identity.RevokeOnSecond = false;
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync(Actor, null, 51, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync(Actor, "../escape", 25, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(Actor, "upload", Guid.Empty, default));
    }
    private sealed class Inventory : IFileRecoveryInventory
    {
        public int Reads { get; private set; }
        public Task<FileRecoveryPageDto> ListAsync(FileRecoveryCursor? after, int take, CancellationToken cancellationToken) { Reads++; return Task.FromResult(new FileRecoveryPageDto([], null)); }
        public Task<FileRecoveryCandidateDto?> GetAsync(string kind, Guid id, CancellationToken cancellationToken) => Task.FromResult<FileRecoveryCandidateDto?>(null);
    }
    private sealed class Identity : IIdentityDirectory
    {
        public int Calls { get; set; } public bool Admin { get; set; } = true; public bool RevokeOnSecond { get; set; }
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> claimGroups, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new CurrentUserDto(subject, null, [], Admin && !(RevokeOnSecond && Calls == 2), DateTimeOffset.UtcNow.AddHours(2))); }
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
