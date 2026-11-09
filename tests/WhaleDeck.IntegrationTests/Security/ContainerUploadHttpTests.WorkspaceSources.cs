using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Development;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests.Security;

public sealed partial class ContainerUploadHttpTests
{
    internal static async Task ImportWorkspaceSourcesInPostgres(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options);
        var workspace = await db.DevelopmentWorkspaces.AsNoTracking().SingleAsync(row =>
            row.OwnerSubject == "developer-subject" && row.State == WorkspaceStorageAllocation.Allocated);
        var directory = new WorkspaceReportHttpFixture();
        var clock = new WorkspaceRunClock();
        var host = new WorkspaceSourceAgentFixture();
        var policy = new WorkspaceRegistrationPolicy { StorageRootIds = [workspace.StorageRootId], CreateGroupIds = [directory.Group] };
        var key = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(key, RandomNumberGenerator.GetBytes(32));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(key, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Agent:CapabilityKeyFile"] = key }).Build();
            void Configure(IServiceCollection services)
            {
                services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton(policy);
                services.AddSingleton<IPrivilegedIdentityDirectory>(directory);
                services.AddSingleton<IWorkspaceSourceGateway>(host);
                services.AddSingleton(_ => new WorkspaceSourceProtector(settings));
                services.AddScoped(_ => new PlatformDbContext(options));
                services.AddScoped<IDevelopmentWorkspaceRegistry, DevelopmentWorkspaceRegistry>();
                services.AddScoped<IWorkspaceReportAccess, WorkspaceReportAccessRepository>();
                services.AddScoped<WorkspaceReportAuthorization>();
                services.AddScoped<DevelopmentWorkspaces>();
                services.AddScoped<IWorkspaceRunStore, WorkspaceRunRepository>();
                services.AddScoped<WorkspaceRuns>();
                services.AddScoped<IWorkspaceSourceStore, WorkspaceSourceRepository>();
                services.AddScoped<WorkspaceSources>();
                services.AddScoped<WorkspaceSourceDispatcher>();
            }

            await using var server = await Fixture.Start(Configure);
            using var owner = await server.Client("developer-subject", false, TimeSpan.FromHours(2));
            using var admin = await server.Client("admin-subject", true, TimeSpan.FromHours(2));
            using var anonymous = await server.Client("anonymous", false);
            using var otherLogin = await server.Client("developer-subject", false, TimeSpan.FromHours(2));
            var csrf = await Csrf(owner);
            var adminCsrf = await Csrf(admin);
            var otherCsrf = await Csrf(otherLogin);
            using var archiveBody = new MemoryStream();
            using (var zip = new ZipArchive(archiveBody, ZipArchiveMode.Create, leaveOpen: true))
            using (var entry = zip.CreateEntry("sample.bin", CompressionLevel.NoCompression).Open())
                await entry.WriteAsync(RandomNumberGenerator.GetBytes(WorkspaceSources.ChunkBytes + 317));
            var body = archiveBody.ToArray();
            var archive = new WorkspaceSourceInput("zip", body.Length, Convert.ToHexStringLower(SHA256.HashData(body)));
            var id = Guid.NewGuid();
            var first = body[..WorkspaceSources.ChunkBytes];
            var last = body[WorkspaceSources.ChunkBytes..];

            using (var response = await anonymous.GetAsync(Url(id))) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            await Expect(HttpMethod.Put, Url(id), JsonContent.Create(archive), HttpStatusCode.BadRequest, owner, null);
            await Expect(HttpMethod.Put, Url(id), JsonContent.Create(archive), HttpStatusCode.NotFound, admin, adminCsrf);
            using (var foreign = Write(owner, HttpMethod.Put, Url(id), csrf, JsonContent.Create(archive)))
            {
                foreign.Headers.Remove("Origin");
                foreign.Headers.Add("Origin", "http://unapproved.invalid");
                using var rejected = await owner.SendAsync(foreign);
                Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
            }
            var canceled = Guid.NewGuid();
            await Begin(canceled);
            await Chunk(canceled, first, 0, HttpStatusCode.OK, owner, csrf);
            await Expect(HttpMethod.Post, Url(canceled) + "/cancel", null, HttpStatusCode.Accepted, otherLogin, otherCsrf);
            await Tick();
            Assert.Equal(WorkspaceSources.Canceled, (await Read(canceled)).State);
            Assert.Equal(1, host.AbortCount);
            Assert.Empty(await db.WorkspaceSourceChunks.ToArrayAsync());

            var revoked = Guid.NewGuid();
            await Begin(revoked);
            await Chunk(revoked, first, 0, HttpStatusCode.OK, owner, csrf);
            await Chunk(revoked, last, first.Length, HttpStatusCode.OK, owner, csrf);
            await Tick();
            var plan = (await Read(revoked)).Plan!;
            await Confirm(revoked, plan.PlanSha256, HttpStatusCode.Accepted, owner, csrf);
            directory.LiveMembership = false;
            await Tick();
            await Tick();
            using (var denied = await owner.GetAsync(Url(revoked))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            directory.LiveMembership = true;
            Assert.Equal(WorkspaceSources.Canceled, (await Read(revoked)).State);
            Assert.Equal("WORKSPACE_SOURCE_AUTHORIZATION_ENDED", (await Read(revoked)).Code);
            Assert.Equal(0, host.PublishCount);
            Assert.Equal(1, host.DiscardCount);
            Assert.Empty(await db.WorkspaceSourceChunks.ToArrayAsync());

            await Task.WhenAll(Begin(id), Begin(id));
            await Expect(HttpMethod.Put, Url(Guid.NewGuid()), JsonContent.Create(archive), HttpStatusCode.Conflict, owner, csrf);
            await Expect(HttpMethod.Put, Url(id), JsonContent.Create(archive with { ArchiveSha256 = new string('0', 64) }), HttpStatusCode.Conflict, owner, csrf);
            await Chunk(id, first, 0, HttpStatusCode.Forbidden, otherLogin, otherCsrf);
            await Chunk(id, last, first.Length, HttpStatusCode.Conflict, owner, csrf);
            await Task.WhenAll(Chunk(id, first, 0, HttpStatusCode.OK, owner, csrf), Chunk(id, first, 0, HttpStatusCode.OK, owner, csrf));
            var encrypted = await db.WorkspaceSourceChunks.AsNoTracking().SingleAsync();
            Assert.Equal(first.Length + 29, encrypted.SealedContent.Length);
            Assert.False(encrypted.SealedContent.AsSpan(29).SequenceEqual(first));
            var changed = first.ToArray();
            changed[0] ^= 1;
            await Chunk(id, changed, 0, HttpStatusCode.Conflict, owner, csrf);
            Assert.Equal(first.Length, (await Read(id)).ReceivedBytes);
            Assert.Equal(WorkspaceSources.Uploading, (await Read(id)).State);
            await Chunk(id, last, first.Length, HttpStatusCode.OK, owner, csrf);
            await Chunk(id, last, first.Length, HttpStatusCode.OK, owner, csrf);
            Assert.Equal(WorkspaceSources.Ready, (await Read(id)).State);

            // Simulate a Worker dying after the Agent has staged bytes and before
            // the SQL row is advanced. A new scope must observe and adopt it.
            await using (var killed = server.Scope())
            {
                var store = killed.ServiceProvider.GetRequiredService<IWorkspaceSourceStore>();
                var claim = Assert.IsType<WorkspaceSourceClaim>(await store.ClaimAsync(CancellationToken.None));
                _ = await host.StageSourceAsync(claim, store.ReadChunksAsync(claim, CancellationToken.None), CancellationToken.None);
            }
            Assert.Equal(body, host.LastArchive);
            Assert.Equal(WorkspaceSources.Ready, (await Read(id)).State);
            await Tick();
            Assert.Equal(WorkspaceSources.Ready, (await Read(id)).State);
            clock.Advance(141);
            await Task.WhenAll(Tick(), Tick());
            Assert.Equal(2, host.StageCount);
            var staged = await Read(id);
            Assert.Equal(WorkspaceSources.Staged, staged.State);
            Assert.NotNull(staged.Plan);
            Assert.Empty(await db.WorkspaceSourceChunks.ToArrayAsync());
            await Confirm(id, new string('0', 64), HttpStatusCode.Conflict, owner, csrf);
            await Confirm(id, staged.Plan!.PlanSha256, HttpStatusCode.Forbidden, otherLogin, otherCsrf);
            await Confirm(id, staged.Plan.PlanSha256, HttpStatusCode.Accepted, owner, csrf);
            await Expect(HttpMethod.Post, Url(id) + "/cancel", null, HttpStatusCode.Conflict, owner, csrf);
            host.LosePublishReply = true;
            await Tick();
            Assert.Equal(WorkspaceSources.Publishing, (await Read(id)).State);
            clock.Advance(6);
            await Tick();
            Assert.Equal(WorkspaceSources.Published, (await Read(id)).State);
            Assert.Equal(1, host.PublishCount);
            await Confirm(id, staged.Plan.PlanSha256, HttpStatusCode.Accepted, owner, csrf);

            await Expect(HttpMethod.Put, Url(Guid.NewGuid()), JsonContent.Create(archive), HttpStatusCode.Conflict, owner, csrf);
            Assert.All(await db.WorkspaceSourceIntents.AsNoTracking().ToArrayAsync(), row => Assert.NotNull(row.FinishedAtUtc));
            Assert.Equal(3, await db.AuditEvents.CountAsync(row => row.Action == "development.workspace.source.begin"));
            Console.WriteLine("INBOX_PHASE=workspace-source-real-http-postgres-encrypted-resume-stage-publish-recovery-revocation-and-cancel");

            string Url(Guid item) => $"/api/v1/development/workspaces/{workspace.Id:D}/source-imports/{item:D}";
            async Task<WorkspaceSourceView> Read(Guid item) => (await owner.GetFromJsonAsync<WorkspaceSourceView>(Url(item)))!;
            Task Begin(Guid item) => Expect(HttpMethod.Put, Url(item), JsonContent.Create(archive), HttpStatusCode.Accepted, owner, csrf);
            Task Confirm(Guid item, string digest, HttpStatusCode status, HttpClient client, string header) =>
                Expect(HttpMethod.Post, Url(item) + "/publish", JsonContent.Create(new WorkspaceSourceConfirmation(digest)), status, client, header);
            async Task Chunk(Guid item, byte[] data, int offset, HttpStatusCode status, HttpClient client, string header)
            {
                using var content = new ByteArrayContent(data);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                await Expect(HttpMethod.Put, Url(item) + "/chunks/" + offset, content, status, client, header);
            }
            async Task Tick()
            {
                await using var scope = server.Scope();
                await scope.ServiceProvider.GetRequiredService<WorkspaceSourceDispatcher>().TickAsync(CancellationToken.None);
            }
        }
        finally { File.Delete(key); }
    }

    private static async Task Expect(HttpMethod method, string path, HttpContent? content, HttpStatusCode status, HttpClient client, string? csrf)
    {
        using var request = Write(client, method, path, csrf, content);
        using var response = await client.SendAsync(request);
        Assert.Equal(status, response.StatusCode);
    }

    private sealed class WorkspaceSourceAgentFixture : IWorkspaceSourceGateway
    {
        private readonly ConcurrentDictionary<Guid, WorkspaceSourceObservation> _items = new();
        public byte[] LastArchive { get; private set; } = [];
        public int StageCount { get; private set; }
        public int PublishCount { get; private set; }
        public int DiscardCount { get; private set; }
        public int AbortCount { get; private set; }
        public bool LosePublishReply { get; set; }

        public async Task<WorkspaceSourceObservation> StageSourceAsync(WorkspaceSourceClaim claim,
            IAsyncEnumerable<ReadOnlyMemory<byte>> chunks, CancellationToken token)
        {
            StageCount++;
            using var copy = new MemoryStream();
            await foreach (var chunk in chunks.WithCancellation(token)) await copy.WriteAsync(chunk, token);
            LastArchive = copy.ToArray();
            Assert.Equal(claim.Import.Archive.ArchiveSha256, Convert.ToHexStringLower(SHA256.HashData(LastArchive)));
            var receipt = new WorkspaceSourceObservation("Staged", new(new string('1', 64), new string('2', 64), 1, 1));
            Assert.True(_items.TryAdd(claim.Import.ImportId, receipt));
            return receipt;
        }

        public Task<WorkspaceSourceObservation> InspectSourceAsync(WorkspaceSourceClaim claim, CancellationToken token) =>
            _items.TryGetValue(claim.Import.ImportId, out var receipt) ? Task.FromResult(receipt) :
                Task.FromException<WorkspaceSourceObservation>(new WorkspaceSourceException(WorkspaceSourceFailure.Missing));

        public Task<WorkspaceSourceObservation> PublishSourceAsync(WorkspaceSourceClaim claim, WorkspaceSourcePlan plan, CancellationToken token)
        {
            PublishCount++;
            var receipt = Set(claim, plan, "Published");
            if (LosePublishReply)
            {
                LosePublishReply = false;
                throw new WorkspaceSourceException(WorkspaceSourceFailure.Retryable);
            }
            return Task.FromResult(receipt);
        }

        public Task<WorkspaceSourceObservation> DiscardSourceAsync(WorkspaceSourceClaim claim, WorkspaceSourcePlan plan, CancellationToken token)
        {
            DiscardCount++;
            return Task.FromResult(Set(claim, plan, "Discarded"));
        }

        public Task AbortSourceAsync(WorkspaceSourceClaim claim, CancellationToken token)
        {
            AbortCount++;
            Assert.False(_items.ContainsKey(claim.Import.ImportId));
            return Task.CompletedTask;
        }

        private WorkspaceSourceObservation Set(WorkspaceSourceClaim claim, WorkspaceSourcePlan plan, string state) =>
            _items.AddOrUpdate(claim.Import.ImportId, _ => throw new InvalidOperationException(), (_, prior) =>
            {
                Assert.Equal(prior.Plan, plan);
                return prior with { State = state };
            });
    }
}
