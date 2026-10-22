using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.UnitTests.Application;

public sealed class ContainerFileReadTests
{
    private static readonly TerminalActor Actor = new("admin", null, [], new('A', 64), "trace", null);
    private static ContainerFileLocationDto Location => new(new('a', 64), "777:2026-10-22T01:02:03.123456789Z", "中文.bin");
    private static FileEntryDto Entry => new("中文.bin", "File", 3, 0, new('B', 64), "0640", false);
    private static readonly byte[] Bytes = [1, 2, 3];
    private static string Digest => Convert.ToHexString(SHA256.HashData(Bytes));

    [Theory]
    [InlineData(false, false, false)][InlineData(true, true, false)][InlineData(true, false, true)]
    public async Task AllReadsRefuseBeforeGatewayOrAuditWhenNotCurrentAdministrator(bool admin, bool wrong, bool expired)
    {
        var setup = new Setup { Identity = { Admin = admin, Wrong = wrong, Expired = expired } };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => setup.Service.TargetAsync(Actor, Location.ContainerId, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => setup.Service.ListAsync(Actor, Location with { Path = "" }, "", 100, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => setup.Service.InspectAsync(Actor, Location, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Consume(setup.Service.DownloadAsync(Actor, Location, Entry.Version, _ => Task.CompletedTask, default)));
        Assert.Equal(0, setup.Gateway.Calls); Assert.Empty(setup.Audit.Rows);
    }

    [Theory]
    [InlineData("../outside")][InlineData("/etc")][InlineData("a//b")][InlineData("a\\b")][InlineData("a/..")][InlineData("a\n")]
    public async Task InvalidPathsRefuseBeforeGateway(string path)
    {
        var setup = new Setup();
        await Assert.ThrowsAsync<ArgumentException>(() => setup.Service.ListAsync(Actor, Location with { Path = path }, "", 100, default));
        Assert.Equal(0, setup.Gateway.Calls);
    }

    [Theory]
    [InlineData("2:2026-02-30T01:02:03Z")][InlineData("1:2026-10-22T01:02:03Z")][InlineData("0777:2026-10-22T01:02:03Z")]
    public void InstanceCannotNormalizeInvalidPidOrCalendar(string instance) =>
        Assert.Throws<ArgumentException>(() => ContainerFileValidation.Location(Location with { InstanceId = instance }));

    [Fact]
    public void PathsKeepValidSurrogatesButRejectUnpairedSurrogateOrOversizedSegment()
    {
        ContainerFileValidation.Location(Location with { Path = "🫧/中文" });
        Assert.Throws<ArgumentException>(() => ContainerFileValidation.Path("\ud800", false));
        Assert.Throws<ArgumentException>(() => ContainerFileValidation.Path(new string('中', 86), false));
    }

    [Fact]
    public async Task MetadataReadsRecheckAuthorityAndAuditBeforeReturningExactInstances()
    {
        var setup = new Setup();
        Assert.Equal(Location.InstanceId, (await setup.Service.TargetAsync(Actor, Location.ContainerId, default)).InstanceId);
        Assert.Equal(Entry, await setup.Service.InspectAsync(Actor, Location, default));
        Assert.Same(setup.Gateway.Page, await setup.Service.ListAsync(Actor, Location with { Path = "" }, "", 100, default));
        Assert.Equal(6, setup.Identity.Calls); Assert.Equal(["target", "inspect", "list"], setup.Audit.Rows.Select(row => row.Action));
        Assert.All(setup.Audit.Rows, row => Assert.Equal("Validated", row.Result));
        setup.Gateway.AfterCall = () => setup.Identity.Admin = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => setup.Service.InspectAsync(Actor, Location, default));
        Assert.Equal(3, setup.Audit.Rows.Count);
    }

    [Theory]
    [InlineData("wrong-name")][InlineData("invented-cursor")][InlineData("out-of-order")][InlineData("too-many")]
    public async Task ChangedOrMalformedMetadataIsNotDeliveredOrAuditedAsValidated(string mode)
    {
        var setup = new Setup();
        if (mode == "wrong-name")
        {
            setup.Gateway.Entry = Entry with { Name = "other.bin" };
            await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.InspectAsync(Actor, Location, default));
        }
        else
        {
            setup.Gateway.Page = mode switch
            {
                "invented-cursor" => new([Entry], "invented", new('C', 64)),
                "out-of-order" => new([Entry, Entry with { Name = "a" }], "", new('C', 64)),
                _ => new([Entry, Entry], "", new('C', 64))
            };
            await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.ListAsync(Actor, Location with { Path = "" }, "", mode == "too-many" ? 1 : 100, default));
        }
        Assert.Empty(setup.Audit.Rows);
    }

    [Fact]
    public async Task CompleteStreamHashesEveryByteAndAuditsBeforeEmittingEnd()
    {
        var setup = new Setup(); var validatedLogin = 0; var seen = new List<FileDownloadPart>();
        await foreach (var part in setup.Service.DownloadAsync(Actor, Location, Entry.Version, _ => { validatedLogin++; return Task.CompletedTask; }, default))
        {
            if (part.Kind == "end") Assert.Equal("Validated", setup.Audit.Rows[^1].Result);
            seen.Add(part);
        }
        Assert.Equal(["metadata", "chunk", "end"], seen.Select(part => part.Kind));
        Assert.Equal(Digest, seen[^1].Sha256); Assert.True(validatedLogin >= 3);
        Assert.Equal(["Started", "Validated"], setup.Audit.Rows.Select(row => row.Result));
        Assert.Equal(3, setup.Audit.Rows[^1].Size); Assert.Equal(Digest, setup.Audit.Rows[^1].Hash);
    }

    [Theory]
    [InlineData("wrong-name")][InlineData("wrong-version")][InlineData("protected")][InlineData("wrong-offset")]
    [InlineData("wrong-digest")][InlineData("truncated")][InlineData("tail")][InlineData("rpc-fault-after-end")]
    public async Task IncompleteOrChangedStreamsNeverEmitSuccessfulEnd(string mode)
    {
        var setup = new Setup(); setup.Gateway.StreamMode = mode; var seen = new List<FileDownloadPart>();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var part in setup.Service.DownloadAsync(Actor, Location, Entry.Version, _ => Task.CompletedTask, default)) seen.Add(part);
        });
        Assert.DoesNotContain(seen, part => part.Kind == "end");
        Assert.Equal(["Started", "Incomplete"], setup.Audit.Rows.Select(row => row.Result));
    }

    [Theory]
    [InlineData("audit")][InlineData("permission")][InlineData("login")]
    public async Task FinalAuditOrCurrentAuthorityFailureWithholdsEnd(string failure)
    {
        var setup = new Setup(); var seen = new List<FileDownloadPart>();
        setup.Audit.FailValidated = failure == "audit";
        var loginCalls = 0;
        Task Login(CancellationToken _)
        {
            loginCalls++;
            if (loginCalls >= 3 && failure == "login") throw new InvalidOperationException("LOGIN_ENDED");
            if (loginCalls >= 3 && failure == "permission") setup.Identity.Admin = false;
            return Task.CompletedTask;
        }
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await foreach (var part in setup.Service.DownloadAsync(Actor, Location, Entry.Version, Login, default)) seen.Add(part);
        });
        Assert.DoesNotContain(seen, part => part.Kind == "end");
        Assert.Equal("Incomplete", setup.Audit.Rows[^1].Result);
    }

    [Fact]
    public async Task DisposingConsumerMarksIncompleteAndPassesCancellationToGateway()
    {
        var setup = new Setup(); using var canceled = new CancellationTokenSource();
        await using (var iterator = setup.Service.DownloadAsync(Actor, Location, Entry.Version, _ => Task.CompletedTask, canceled.Token).GetAsyncEnumerator())
        { Assert.True(await iterator.MoveNextAsync()); }
        Assert.Equal(canceled.Token, setup.Gateway.Cancellation);
        Assert.Equal(["Started", "Incomplete"], setup.Audit.Rows.Select(row => row.Result));
    }

    private static async Task Consume(IAsyncEnumerable<FileDownloadPart> parts) { await foreach (var _ in parts) { } }
    private sealed class Setup
    {
        public Identity Identity { get; } = new();
        public Gateway Gateway { get; } = new();
        public Audit Audit { get; } = new();
        public ContainerFileReadService Service => new(new(null!, null!, null!, Identity, TimeProvider.System), Gateway, Audit, TimeProvider.System);
    }
    private sealed class Identity : IIdentityDirectory
    {
        public bool Admin { get; set; } = true;
        public bool Wrong { get; set; }
        public bool Expired { get; set; }
        public int Calls { get; private set; }
        public Task<CurrentUserDto> ResolveCurrentAsync(string subject, string? name, IReadOnlyCollection<string> groups, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new CurrentUserDto(Wrong ? "other" : subject, name, groups, Admin, DateTimeOffset.UtcNow.AddHours(Expired ? -1 : 2))); }
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ManagedResourceDto>> ListSsoApplicationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed record AuditRow(string Action, string Result, long? Size, string? Hash);
    private sealed class Audit : IContainerFileReadAudit
    {
        public List<AuditRow> Rows { get; } = [];
        public bool FailValidated { get; set; }
        public Task RecordAsync(TerminalActor actor, ContainerFileLocationDto location, string action, string result, string? version, long? sizeBytes, string? sha256, CancellationToken cancellationToken)
        {
            if (FailValidated && result == "Validated") throw new InvalidOperationException("AUDIT_FAILED");
            Rows.Add(new(action, result, sizeBytes, sha256)); return Task.CompletedTask;
        }
    }
    private sealed class Gateway : IContainerFileGateway
    {
        public int Calls { get; private set; }
        public Action? AfterCall { get; set; }
        public string StreamMode { get; set; } = "normal";
        public FileEntryDto Entry { get; set; } = ContainerFileReadTests.Entry;
        public FilePageDto Page { get; set; } = new([ContainerFileReadTests.Entry], "", new('C', 64));
        public CancellationToken Cancellation { get; private set; }
        public Task<ContainerFileTargetDto> InspectContainerFileTargetAsync(string containerId, CancellationToken cancellationToken)
        { Calls++; AfterCall?.Invoke(); return Task.FromResult(new ContainerFileTargetDto(containerId, "ordinary", Location.InstanceId, "sha256:" + new string('b', 64), "1000:1000")); }
        public Task<FilePageDto> ListContainerFilesAsync(ContainerFileLocationDto location, string after, int take, CancellationToken cancellationToken)
        { Calls++; AfterCall?.Invoke(); return Task.FromResult(Page); }
        public Task<FileEntryDto?> InspectContainerFileAsync(ContainerFileLocationDto location, CancellationToken cancellationToken)
        { Calls++; AfterCall?.Invoke(); return Task.FromResult<FileEntryDto?>(Entry); }
        public async IAsyncEnumerable<FileDownloadPart> DownloadContainerFileAsync(ContainerFileLocationDto location, string expectedVersion, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Calls++; Cancellation = cancellationToken; await Task.Yield(); cancellationToken.ThrowIfCancellationRequested();
            var entry = StreamMode switch { "wrong-name" => Entry with { Name = "other" }, "wrong-version" => Entry with { Version = new('F', 64) }, "protected" => Entry with { IsProtected = true }, _ => Entry };
            yield return new("metadata", entry, default, 0, "", "", 0);
            yield return new("chunk", null, Bytes, StreamMode == "wrong-offset" ? 1 : 0, "", "", 0);
            if (StreamMode == "truncated") yield break;
            yield return new("end", null, default, 0, StreamMode == "wrong-digest" ? new('F', 64) : Digest, Entry.Version, 3);
            if (StreamMode == "tail") yield return new("unexpected", null, default, 0, "", "", 0);
            if (StreamMode == "rpc-fault-after-end") throw new InvalidOperationException("RPC_FAILED");
        }
    }
}
