using System.Collections.Concurrent;
using System.Formats.Tar;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Development;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;
using WhaleDeck.Infrastructure.Agent;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests.Security;

public sealed partial class ContainerUploadHttpTests
{
    internal static async Task ImportWorkspaceGitInPostgres(DbContextOptions<PlatformDbContext> options)
    {
        await using var db = new PlatformDbContext(options);
        var workspaceId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO development_workspaces (id, slug, owner_subject, storage_root_id, state, version, cpu_millis, memory_mi_b,
                disk_mi_b, environment_json, environment_sha256, registration_key, request_sha256, created_at_utc,
                storage_plan_sha256, storage_allocation_started_at_utc, storage_allocated_at_utc)
            SELECT {workspaceId}, 'git-native-fixture', owner_subject, storage_root_id, state, version, cpu_millis, memory_mi_b,
                disk_mi_b, environment_json, environment_sha256, {Guid.NewGuid()}, request_sha256, created_at_utc,
                storage_plan_sha256, storage_allocation_started_at_utc, storage_allocated_at_utc
            FROM development_workspaces WHERE owner_subject = 'developer-subject' AND state = 'StorageAllocated'
            """);
        var workspace = await db.DevelopmentWorkspaces.AsNoTracking().SingleAsync(row => row.Id == workspaceId);
        var identities = new WorkspaceReportHttpFixture();
        var clock = new WorkspaceRunClock();
        var directory = System.IO.Directory.CreateTempSubdirectory("wd-git-wire-");
        try
        {
            var key = RandomNumberGenerator.GetBytes(32);
            var keyPath = Path.Combine(directory.FullName, "key");
            await File.WriteAllBytesAsync(keyPath, key);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var socket = Path.Combine(directory.FullName, "agent.sock");
            var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:SocketPath"] = socket, ["Agent:CapabilityKeyFile"] = keyPath,
                ["DevelopmentWorkspaces:StorageRootIds:0"] = workspace.StorageRootId,
                ["DevelopmentWorkspaces:CreateGroupIds:0"] = identities.Group.ToString("D"),
                ["DevelopmentWorkspaces:GitRepositories:qa-repo:Subjects:0"] = "developer-subject",
                ["DevelopmentWorkspaces:GitRepositories:qa-repo:CredentialRef"] = "qa-private-ref",
                ["DevelopmentWorkspaces:GitRepositories:other-repo:Subjects:0"] = "another-subject"
            }).Build();
            var policy = settings.GetSection("DevelopmentWorkspaces").Get<WorkspaceRegistrationPolicy>()!;
            DevelopmentWorkspaces.ValidatePolicy(policy);
            using var gateway = new AgentGateway(settings);
            var host = new WorkspaceSourceAgentFixture { GitGateway = gateway };
            var upstream = new WorkspaceGitWireFixture(key);
            var updateHost = new WorkspaceUpdateWireFixture(key, upstream.Bytes);
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenUnixSocket(socket, listen => listen.Protocols = HttpProtocols.Http2));
            builder.Services.AddGrpc();
            builder.Services.AddSingleton(upstream);
            builder.Services.AddSingleton(updateHost);
            await using var wire = builder.Build();
            wire.MapGrpcService<WorkspaceGitWireFixture>();
            wire.MapGrpcService<WorkspaceUpdateWireFixture>();
            wire.MapGrpcService<WorkspaceGitCapabilitiesFixture>();
            await wire.StartAsync();

            await using var server = await Fixture.Start(services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton(policy);
                services.AddSingleton<IPrivilegedIdentityDirectory>(identities);
                services.AddSingleton<IWorkspaceSourceGateway>(host);
                services.AddSingleton<IWorkspaceSourceDownloadGateway>(gateway);
                services.AddSingleton<IWorkspaceSourceDiffGateway>(gateway);
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
                services.AddScoped<WorkspaceSourceDownloads>();
                services.AddScoped<WorkspaceSourceDiffs>();
                services.AddScoped<WorkspaceSourceDispatcher>();
            });
            using var owner = await server.Client("developer-subject", false, TimeSpan.FromHours(2));
            var csrf = await Csrf(owner);
            var collection = $"/api/v1/development/workspaces/{workspaceId:D}/source-imports";
            var source = new WorkspaceGitInput("qa-repo", "refs/tags/qa");
            Assert.Equal("qa-repo", Assert.Single((await owner.GetFromJsonAsync<string[]>(collection + "/git-repositories"))!));
            var canceled = Guid.NewGuid();
            await Expect(HttpMethod.Put, Url(canceled) + "/git", JsonContent.Create(source), HttpStatusCode.BadRequest, owner, null);
            await Expect(HttpMethod.Put, Url(canceled) + "/git", JsonContent.Create(source with { RepositoryId = "other-repo" }),
                HttpStatusCode.Forbidden, owner, csrf);
            await Expect(HttpMethod.Put, Url(canceled) + "/git",
                JsonContent.Create(new { repositoryId = "qa-repo", @ref = "HEAD", credentialRef = "client-cannot-set" }),
                HttpStatusCode.BadRequest, owner, csrf);
            await Begin(canceled);
            Assert.Equal(WorkspaceSources.Acquiring, (await Read(canceled)).State);
            Assert.DoesNotContain("qa-private-ref", await owner.GetStringAsync(Url(canceled)), StringComparison.Ordinal);
            using (var content = new ByteArrayContent([42]))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                await Expect(HttpMethod.Put, Url(canceled) + "/chunks/0", content, HttpStatusCode.Conflict, owner, csrf);
            }
            await Expect(HttpMethod.Post, Url(canceled) + "/cancel", null, HttpStatusCode.Accepted, owner, csrf);
            Assert.Equal(WorkspaceSources.Canceled, (await Read(canceled)).State);
            Assert.Empty(upstream.Requests);
            Assert.Equal(0, host.AbortCount);

            var inflight = Guid.NewGuid();
            await Begin(inflight);
            upstream.Next = "hold";
            var active = Tick();
            await upstream.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Expect(HttpMethod.Post, Url(inflight) + "/cancel", null, HttpStatusCode.Accepted, owner, csrf);
            await upstream.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await active.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(WorkspaceSources.Canceled, (await Read(inflight)).State);
            Assert.Equal(0, host.AbortCount);

            var failed = Guid.NewGuid();
            await Begin(failed);
            upstream.Next = "corrupt";
            await Tick();
            Assert.Equal("WORKSPACE_SOURCE_GIT_INTEGRITY", (await Read(failed)).Code);
            Assert.Equal(WorkspaceSources.Failed, (await Read(failed)).State);
            Assert.Empty(await db.WorkspaceSourceChunks.AsNoTracking().ToArrayAsync());

            var id = Guid.NewGuid();
            await Task.WhenAll(Begin(id), Begin(id));
            await Expect(HttpMethod.Put, Url(id) + "/git", JsonContent.Create(source with { Ref = "HEAD" }),
                HttpStatusCode.Conflict, owner, csrf);
            var invalid = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE development_workspace_source_imports SET git_commit = 'malformed' WHERE id = {id}"));
            Assert.Equal("ck_workspace_source_git", invalid.ConstraintName);
            WorkspaceSourceClaim old;
            await using (var killed = server.Scope())
            {
                var store = killed.ServiceProvider.GetRequiredService<IWorkspaceSourceStore>();
                old = Assert.IsType<WorkspaceSourceClaim>(await store.ClaimAsync(CancellationToken.None));
                upstream.Next = "disconnect";
                var interrupted = await Assert.ThrowsAsync<WorkspaceSourceException>(() => gateway.AcquireGitAsync(old,
                    (proof, token) => store.BindGitAsync(old, proof, token),
                    (offset, body, token) => store.AppendGitAsync(old, offset, body, token), CancellationToken.None));
                Assert.Equal(WorkspaceSourceFailure.Retryable, interrupted.Failure);
            }
            var partial = await Read(id);
            Assert.Equal(WorkspaceSources.Acquiring, partial.State);
            Assert.Equal(source.Ref, partial.Git!.Ref);
            Assert.Equal(upstream.Commit, partial.Git.Snapshot!.Commit);
            Assert.Equal(WorkspaceSources.ChunkBytes, partial.ReceivedBytes);
            var encrypted = await db.WorkspaceSourceChunks.AsNoTracking().SingleAsync();
            Assert.Equal(WorkspaceSources.ChunkBytes + 29, encrypted.SealedContent.Length);
            Assert.False(encrypted.SealedContent.AsSpan(29).SequenceEqual(upstream.Bytes.AsSpan(0, WorkspaceSources.ChunkBytes)));
            await Tick();
            Assert.Equal(WorkspaceSources.Acquiring, (await Read(id)).State);
            clock.Advance(141);
            await using (var stale = server.Scope())
                await Assert.ThrowsAsync<WorkspaceSourceException>(() => stale.ServiceProvider.GetRequiredService<IWorkspaceSourceStore>()
                    .AppendGitAsync(old, 0, upstream.Bytes[..WorkspaceSources.ChunkBytes], CancellationToken.None));
            await Task.WhenAll(Tick(), Tick());
            Assert.Contains((await Read(id)).State, new[] { WorkspaceSources.Ready, WorkspaceSources.Staged });
            Assert.Equal(new[] { source.Ref, upstream.Tag }, upstream.Requests.Where(request => request.ImportId == id.ToString("N"))
                .Select(request => request.Ref));
            Assert.All(upstream.Requests, request => Assert.Equal("qa-private-ref", request.CredentialRef));
            await Tick();
            var staged = await Read(id);
            Assert.Equal(WorkspaceSources.Staged, staged.State);
            Assert.Equal(1, host.StageCount);
            Assert.Equal(upstream.Bytes, host.LastArchive);
            Assert.Empty(await db.WorkspaceSourceChunks.AsNoTracking().ToArrayAsync());
            Assert.Equal(0, host.PublishCount);
            await Expect(HttpMethod.Post, Url(id) + "/publish",
                JsonContent.Create(new WorkspaceSourceConfirmation(staged.Plan!.PlanSha256)), HttpStatusCode.Accepted, owner, csrf);
            await Tick();
            Assert.Equal(WorkspaceSources.Published, (await Read(id)).State);
            Assert.Equal(1, host.PublishCount);
            Assert.Equal(1, await db.AuditEvents.CountAsync(row =>
                row.Action == "development.workspace.source.git-pin" && row.TargetId == id.ToString("D")));
            var history = collection + "/history";
            var recent = Assert.IsType<WorkspaceSourceHistoryPage>(
                await owner.GetFromJsonAsync<WorkspaceSourceHistoryPage>(history + "?take=2"));
            Assert.Equal(id, recent.Items[0].ImportId);
            Assert.Equal(2, recent.Items.Length);
            Assert.Equal(recent.Items[1].ImportId, recent.NextBefore);
            var older = Assert.IsType<WorkspaceSourceHistoryPage>(
                await owner.GetFromJsonAsync<WorkspaceSourceHistoryPage>(history + $"?take=2&before={recent.NextBefore:D}"));
            Assert.Null(older.NextBefore);
            var entries = recent.Items.Concat(older.Items).ToArray();
            Assert.Equal(new[] { canceled, inflight, failed, id }.Order(), entries.Select(item => item.ImportId).Order());
            Assert.All(entries, entry => Assert.NotNull(entry.FinishedAtUtc));
            using (var wrongCursor = await owner.GetAsync(history + $"?before={Guid.NewGuid():D}"))
                Assert.Equal(HttpStatusCode.NotFound, wrongCursor.StatusCode);
            Console.WriteLine("INBOX_PHASE=workspace-git-real-http-uds-grpc-postgres-pinned-lease-recovery-encrypted-and-confirmed");

            var gitDowngrade = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
                db.GetService<IMigrator>().MigrateAsync("20261109150125_DevelopmentWorkspaceSourceImports"));
            Assert.Equal("workspace_source_git_history_present", gitDowngrade.MessageText);
            await db.Database.MigrateAsync();
            Assert.Equal(id, (await PublishedHead()).ImportId);
            var wrongBase = Guid.NewGuid();
            await Expect(HttpMethod.Put, Url(wrongBase) + "/git",
                JsonContent.Create(source with { PreviousImportId = canceled }), HttpStatusCode.Conflict, owner, csrf);
            var staleUpdate = Guid.NewGuid();
            var updatePlan = await BeginUpdate(staleUpdate);
            var diffUrl = Url(staleUpdate) + "/diff?limit=1&planSha256=" + updatePlan.PlanSha256;
            using (var anonymousDiff = await server.Client("anonymous", false))
                await Expect(HttpMethod.Get, diffUrl, null, HttpStatusCode.Unauthorized, anonymousDiff, null);
            using (var otherDiff = await server.Client("admin-subject", true))
                await Expect(HttpMethod.Get, diffUrl, null, HttpStatusCode.NotFound, otherDiff, null);
            await Expect(HttpMethod.Get, Url(staleUpdate) + "/diff?planSha256=" + new string('b', 64),
                null, HttpStatusCode.Conflict, owner, null);
            var diffPage = (await owner.GetFromJsonAsync<WorkspaceSourceDiffPageView>(diffUrl))!;
            Assert.Equal(1, diffPage.NextOffset);
            Assert.Equal(1, diffPage.RedactedEntries);
            Assert.Equal("src/源码.cjs", Assert.Single(diffPage.Entries).Path);
            var lastDiff = (await owner.GetFromJsonAsync<WorkspaceSourceDiffPageView>(diffUrl + "&offset=1"))!;
            Assert.Null(lastDiff.NextOffset);
            Assert.Equal("src/末尾.cjs", Assert.Single(lastDiff.Entries).Path);
            updateHost.AfterDiff = () => identities.LiveMembership = false;
            await Expect(HttpMethod.Get, diffUrl, null, HttpStatusCode.Forbidden, owner, null);
            updateHost.AfterDiff = null;
            identities.LiveMembership = true;
            updateHost.InvalidDiff = true;
            await Expect(HttpMethod.Get, diffUrl, null, HttpStatusCode.BadGateway, owner, null);
            updateHost.InvalidDiff = false;
            await ConfirmUpdate(staleUpdate, updatePlan.PlanSha256, null, HttpStatusCode.BadRequest);
            await ConfirmUpdate(staleUpdate, updatePlan.PlanSha256, WorkspaceSources.CleanOnly, HttpStatusCode.Conflict);
            await ConfirmUpdate(staleUpdate, updatePlan.PlanSha256, WorkspaceSources.RetainAndReplace, HttpStatusCode.Accepted);
            await Expect(HttpMethod.Get, diffUrl, null, HttpStatusCode.Conflict, owner, null);
            Console.WriteLine("INBOX_PHASE=workspace-source-diff-real-http-uds-postgres-paging-scope-and-live-revocation");
            updateHost.RejectLateWrite = true;
            await Tick();
            await Tick();
            Assert.Equal(WorkspaceSources.Failed, (await Read(staleUpdate)).State);
            Assert.Equal("WORKSPACE_SOURCE_WORK_CHANGED", (await Read(staleUpdate)).Code);
            Assert.Equal(id, (await PublishedHead()).ImportId);

            var replacement = Guid.NewGuid();
            updatePlan = await BeginUpdate(replacement);
            await ConfirmUpdate(replacement, updatePlan.PlanSha256, WorkspaceSources.RetainAndReplace, HttpStatusCode.Accepted);
            await ConfirmUpdate(replacement, updatePlan.PlanSha256, WorkspaceSources.CleanOnly, HttpStatusCode.Conflict);
            await ConfirmUpdate(replacement, updatePlan.PlanSha256, WorkspaceSources.RetainAndReplace, HttpStatusCode.Accepted);
            await Expect(HttpMethod.Post, Url(replacement) + "/cancel", null, HttpStatusCode.Conflict, owner, csrf);
            updateHost.LoseExchangeReply = true;
            await Tick();
            var pendingUpdate = await Read(replacement);
            Assert.Equal(WorkspaceSources.Publishing, pendingUpdate.State);
            Assert.Equal(WorkspaceSources.RetainAndReplace, pendingUpdate.ConflictResolution);
            Assert.Equal(id, (await PublishedHead()).ImportId);
            await Expect(HttpMethod.Get, Url(replacement) + "/previous", null, HttpStatusCode.NotFound, owner, null);
            clock.Advance(6);
            await Tick();
            var publishedUpdate = await PublishedHead();
            Assert.Equal(replacement, publishedUpdate.ImportId);
            Assert.Equal(id, publishedUpdate.PreviousImportId);
            Assert.Equal(updatePlan, publishedUpdate.Plan);
            Assert.Equal(WorkspaceSources.RetainAndReplace, publishedUpdate.ConflictResolution);
            Assert.Equal(1, updateHost.Exchanges);
            Assert.Equal(2, updateHost.PublishRequests.Count(request => request.ImportId == replacement.ToString("N")));
            Assert.All(updateHost.PublishRequests, request => Assert.Equal(WorkspaceSourceConflictResolution.RetainAndReplace, request.ConflictResolution));
            await Expect(HttpMethod.Put, Url(Guid.NewGuid()) + "/git", JsonContent.Create(source with { PreviousImportId = id }),
                HttpStatusCode.Conflict, owner, csrf);
            await Expect(HttpMethod.Put, Url(replacement) + "/git", JsonContent.Create(source with { PreviousImportId = id }),
                HttpStatusCode.Accepted, owner, csrf);
            Assert.Empty(await db.WorkspaceSourceChunks.AsNoTracking().ToArrayAsync());
            Console.WriteLine("INBOX_PHASE=workspace-git-update-real-http-uds-grpc-postgres-confirmed-choice-fence-and-recovery");

            var download = Url(replacement) + "/previous";
            using var anonymous = await server.Client("anonymous", false);
            using var other = await server.Client("admin-subject", true);
            await Expect(HttpMethod.Get, download, null, HttpStatusCode.Unauthorized, anonymous, null);
            using (var forbidden = await other.GetAsync(download))
                Assert.Contains(forbidden.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound });
            await Expect(HttpMethod.Get, Url(id) + "/previous", null, HttpStatusCode.NotFound, owner, null);
            using (var range = new HttpRequestMessage(HttpMethod.Get, download))
            {
                range.Headers.Range = new RangeHeaderValue(0, 32);
                using var rejected = await owner.SendAsync(range);
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            }
            Assert.Equal(0, updateHost.Exports);
            using (var attachment = await owner.GetAsync(download))
            {
                Assert.Equal(HttpStatusCode.OK, attachment.StatusCode);
                Assert.Equal("application/x-tar", attachment.Content.Headers.ContentType?.MediaType);
                Assert.Equal($"workspace-previous-{replacement:N}.tar", attachment.Content.Headers.ContentDisposition?.FileName);
                Assert.True(attachment.Headers.CacheControl?.NoStore);
                Assert.Equal("nosniff", Assert.Single(attachment.Headers.GetValues("X-Content-Type-Options")));
                Assert.Equal(upstream.Bytes, await attachment.Content.ReadAsByteArrayAsync());
            }
            updateHost.ExportMode = "bad-offset";
            await Expect(HttpMethod.Get, download, null, HttpStatusCode.BadGateway, owner, null);
            await Incomplete("bad-digest");
            await Incomplete("failed-after-footer");
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            updateHost.ExportMode = null;
            updateHost.ExportFooterGate = release.Task;
            using (var revoked = await owner.GetAsync(download, HttpCompletionOption.ResponseHeadersRead).WaitAsync(TimeSpan.FromSeconds(10)))
            {
                Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
                identities.LiveMembership = false;
                release.SetResult();
                await Assert.ThrowsAnyAsync<HttpRequestException>(() => revoked.Content.ReadAsByteArrayAsync());
                identities.LiveMembership = true;
            }
            updateHost.ExportFooterGate = null;
            Assert.Equal(upstream.Bytes, await owner.GetByteArrayAsync(download));
            var exportAudits = await db.AuditEvents.AsNoTracking().Where(row =>
                row.Action == "development.workspace.source.download-previous-authorized" && row.TargetId == replacement.ToString("D")).ToArrayAsync();
            Assert.Equal(6, exportAudits.Length);
            Assert.All(exportAudits, row => Assert.Equal("developer-subject", row.ActorSubject));
            Console.WriteLine("INBOX_PHASE=workspace-git-previous-real-http-uds-postgres-owner-audit-verified-completion-and-revocation");

            async Task Incomplete(string mode)
            {
                updateHost.ExportMode = mode;
                using var response = await owner.GetAsync(download, HttpCompletionOption.ResponseHeadersRead);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                await Assert.ThrowsAnyAsync<HttpRequestException>(() => response.Content.ReadAsByteArrayAsync());
            }

            string Url(Guid item) => $"{collection}/{item:D}";
            async Task<WorkspaceSourceView> Read(Guid item) => (await owner.GetFromJsonAsync<WorkspaceSourceView>(Url(item)))!;
            Task Begin(Guid item) => Expect(HttpMethod.Put, Url(item) + "/git", JsonContent.Create(source), HttpStatusCode.Accepted, owner, csrf);
            async Task<WorkspaceSourceView> PublishedHead() =>
                (await owner.GetFromJsonAsync<WorkspaceSourceView>(collection + "/published"))!;
            async Task<WorkspaceSourcePlan> BeginUpdate(Guid item)
            {
                await Expect(HttpMethod.Put, Url(item) + "/git", JsonContent.Create(source with { PreviousImportId = id }),
                    HttpStatusCode.Accepted, owner, csrf);
                await Tick();
                await Tick();
                var update = await Read(item);
                Assert.Equal(WorkspaceSources.Staged, update.State);
                Assert.Equal(id, update.PreviousImportId);
                Assert.True(update.Plan?.Update?.Dirty);
                Assert.Equal(staged.Plan?.ContentSha256, update.Plan!.Update!.PreviousContentSha256);
                return update.Plan;
            }
            Task ConfirmUpdate(Guid item, string digest, string? choice, HttpStatusCode status) => Expect(
                HttpMethod.Post, Url(item) + "/publish", JsonContent.Create(new WorkspaceSourceConfirmation(digest, choice)), status, owner, csrf);
            async Task Tick()
            {
                await using var scope = server.Scope();
                await scope.ServiceProvider.GetRequiredService<WorkspaceSourceDispatcher>().TickAsync(CancellationToken.None);
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class WorkspaceGitCapabilitiesFixture : AgentService.AgentServiceBase
    {
        public override Task<CapabilitiesResponse> GetCapabilities(Empty request, ServerCallContext context) => Task.FromResult(
            new CapabilitiesResponse
            {
                ProtocolMajor = 1, ProtocolMinor = DevelopmentSourceBinding.DiffProtocolMinor,
                Capabilities = { DevelopmentGitBinding.Capability, DevelopmentSourceBinding.Capability, DevelopmentSourceBinding.UpdateCapability,
                    DevelopmentSourceBinding.PreviousCapability, DevelopmentSourceBinding.DiffCapability }
            });
    }

    private sealed class WorkspaceUpdateWireFixture(byte[] key, byte[] expectedArchive) : DevelopmentSourceService.DevelopmentSourceServiceBase
    {
        private readonly ConcurrentDictionary<string, WorkspaceSourceReceipt> _operations = new(StringComparer.Ordinal);
        public ConcurrentQueue<WorkspaceSourceRequest> PublishRequests { get; } = new();
        public bool RejectLateWrite { get; set; }
        public bool LoseExchangeReply { get; set; }
        public int Exchanges { get; private set; }
        public int Exports { get; private set; }
        public string? ExportMode { get; set; }
        public Task? ExportFooterGate { get; set; }
        public Action? AfterDiff { get; set; }
        public bool InvalidDiff { get; set; }

        public override Task<WorkspaceSourceDiffPage> ReadDiff(WorkspaceSourceDiffRequest request, ServerCallContext context)
        {
            Assert.True(DevelopmentSourceDiffBinding.IsValid(request));
            Authorize(DevelopmentSourceDiffBinding.Hash(request), context);
            var receipt = Find(request.Source);
            Assert.Equal("Staged", receipt.State);
            Assert.Equal(receipt.PlanSha256, request.Source.PlanSha256);
            var page = new WorkspaceSourceDiffPage
            {
                WorkspaceId = receipt.WorkspaceId, ImportId = receipt.ImportId, PreviousImportId = receipt.PreviousImportId,
                PlanSha256 = receipt.PlanSha256, Offset = request.Offset, VisibleEntries = 2, RedactedEntries = InvalidDiff ? 2U : 1U,
                Entries = { new WorkspaceSourceDiffEntry
                {
                    Path = request.Offset == 0 ? "src/源码.cjs" : "src/末尾.cjs", Change = WorkspaceSourceDiffChange.Changed,
                    Before = new() { Kind = WorkspaceSourceDiffKind.File, SizeBytes = 10 },
                    After = new() { Kind = WorkspaceSourceDiffKind.File, SizeBytes = 12 }
                } }
            };
            if (request.Offset == 0) page.NextOffset = 1;
            AfterDiff?.Invoke();
            return Task.FromResult(page);
        }

        public override async Task ExportPrevious(WorkspaceSourceRequest request,
            IServerStreamWriter<WorkspaceSourceExportFrame> response, ServerCallContext context)
        {
            Authorize(request, context);
            Exports++;
            var receipt = Find(request);
            Assert.Equal("Published", receipt.State);
            Assert.Equal(receipt.PreviousImportId, request.PreviousImportId);
            Assert.Equal(receipt.PlanSha256, request.PlanSha256);
            Assert.Equal(WorkspaceSourceConflictResolution.RetainAndReplace, request.ConflictResolution);
            var mode = ExportMode;
            ulong offset = mode == "bad-offset" ? 1UL : 0UL;
            foreach (var chunk in expectedArchive.Chunk(DevelopmentSourceBinding.ChunkBytes))
            {
                await response.WriteAsync(new() { Offset = offset, Content = ByteString.CopyFrom(chunk) });
                offset += (ulong)chunk.Length;
                if (mode == "bad-offset") return;
            }
            if (ExportFooterGate is { } gate) await gate.WaitAsync(context.CancellationToken);
            var hash = mode == "bad-digest" ? new string('0', 64) : Convert.ToHexStringLower(SHA256.HashData(expectedArchive));
            await response.WriteAsync(new() { Offset = offset, ArchiveSha256 = hash });
            if (mode == "failed-after-footer") throw new RpcException(new Status(StatusCode.Unavailable, "controlled export interruption"));
        }

        public override async Task<WorkspaceSourceReceipt> StageUpdate(IAsyncStreamReader<WorkspaceSourceFrame> requestStream, ServerCallContext context)
        {
            Assert.True(await requestStream.MoveNext(context.CancellationToken));
            var request = requestStream.Current.Header;
            Authorize(request, context);
            Assert.Equal(DevelopmentSourceBinding.Hash(request), context.RequestHeaders.GetValue(DevelopmentSourceBinding.Header));
            using var body = new MemoryStream();
            while (await requestStream.MoveNext(context.CancellationToken))
                body.Write(requestStream.Current.Content.Span);
            Assert.Equal(expectedArchive, body.ToArray());
            using var payload = System.Text.Json.JsonDocument.Parse(DevelopmentSourceBinding.HostInput(request));
            var storage = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload.RootElement.GetProperty("storage"));
            var receipt = new WorkspaceSourceReceipt
            {
                WorkspaceId = request.Storage.WorkspaceId, StorageSha256 = Convert.ToHexStringLower(SHA256.HashData(storage)),
                ImportId = request.ImportId, PreviousImportId = request.PreviousImportId, ArchiveBytes = request.ArchiveBytes,
                ArchiveFormat = "tar", ArchiveSha256 = request.ArchiveSha256, PlanSha256 = new string('a', 64),
                ContentSha256 = new string('d', 64), FileCount = 1, TotalBytes = WorkspaceSources.ChunkBytes + 317,
                State = "Staged", Update = new()
                {
                    PreviousContentSha256 = new string('2', 64), WorkContentSha256 = new string('c', 64),
                    Dirty = true, ChangedEntries = 3
                }
            };
            Assert.True(_operations.TryAdd(request.ImportId, receipt));
            return receipt.Clone();
        }

        public override Task<WorkspaceSourceReceipt> InspectUpdate(WorkspaceSourceRequest request, ServerCallContext context)
        {
            Authorize(request, context);
            return Task.FromResult(Find(request).Clone());
        }

        public override Task<WorkspaceSourceReceipt> PublishUpdate(WorkspaceSourceRequest request, ServerCallContext context)
        {
            Authorize(request, context);
            PublishRequests.Enqueue(request);
            Assert.Equal(WorkspaceSourceConflictResolution.RetainAndReplace, request.ConflictResolution);
            var receipt = Find(request);
            Assert.Equal(receipt.PlanSha256, request.PlanSha256);
            if (RejectLateWrite)
            {
                RejectLateWrite = false;
                throw new RpcException(new Status(StatusCode.Aborted, "WORKSPACE_SOURCE_WORK_CHANGED"));
            }
            if (receipt.State == "Staged")
            {
                Exchanges++;
                receipt.State = "PublishUnconfirmed";
                if (LoseExchangeReply)
                {
                    LoseExchangeReply = false;
                    throw new RpcException(new Status(StatusCode.Unavailable, "controlled lost reply"));
                }
            }
            Assert.Equal("PublishUnconfirmed", receipt.State);
            receipt.State = "Published";
            return Task.FromResult(receipt.Clone());
        }

        public override Task<WorkspaceSourceReceipt> DiscardUpdate(WorkspaceSourceRequest request, ServerCallContext context)
        {
            Authorize(request, context);
            var receipt = Find(request);
            Assert.Equal("Staged", receipt.State);
            Assert.Equal(receipt.PlanSha256, request.PlanSha256);
            receipt.State = "Discarded";
            return Task.FromResult(receipt.Clone());
        }

        private WorkspaceSourceReceipt Find(WorkspaceSourceRequest request) => _operations.TryGetValue(request.ImportId, out var receipt)
            ? receipt : throw new RpcException(new Status(StatusCode.NotFound, "WORKSPACE_SOURCE_NOT_FOUND"));

        private void Authorize(WorkspaceSourceRequest request, ServerCallContext context)
        {
            Assert.True(DevelopmentSourceBinding.IsValid(request, context.Method));
            Authorize(DevelopmentSourceBinding.Hash(request), context);
        }

        private void Authorize(string hash, ServerCallContext context)
        {
            var parts = context.RequestHeaders.GetValue("x-whaledeck-capability")?.Split('.');
            Assert.NotNull(parts);
            Assert.Equal(3, parts.Length);
            var signed = DevelopmentSourceBinding.Payload(parts[0], parts[1], context.Method, hash);
            Assert.Equal(Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signed))), parts[2]);
        }
    }

    private sealed class WorkspaceGitWireFixture(byte[] key) : DevelopmentGitService.DevelopmentGitServiceBase
    {
        private const int PayloadSize = WorkspaceSources.ChunkBytes + 317;
        public byte[] Bytes { get; } = Archive();
        public string Tag { get; } = new('a', 40);
        public string Commit { get; } = new('b', 40);
        public string? Next { get; set; }
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<WorkspaceGitRequest> Requests { get; } = new();

        public override async Task Acquire(WorkspaceGitRequest request, IServerStreamWriter<WorkspaceGitFrame> responseStream, ServerCallContext context)
        {
            var parts = context.RequestHeaders.GetValue("x-whaledeck-capability")?.Split('.');
            Assert.NotNull(parts);
            Assert.Equal(3, parts.Length);
            var signed = DevelopmentGitBinding.Payload(parts[0], parts[1], context.Method, DevelopmentGitBinding.Hash(request));
            Assert.Equal(Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signed))), parts[2]);
            Assert.True(DevelopmentGitBinding.IsValid(request));
            Requests.Enqueue(request);
            var mode = Next;
            Next = null;
            if (mode == "hold")
            {
                Waiting.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, context.CancellationToken); }
                finally { Canceled.TrySetResult(); }
            }
            await responseStream.WriteAsync(new() { Snapshot = new()
            {
                WorkspaceId = request.Storage.WorkspaceId, ImportId = request.ImportId, RepositoryId = request.RepositoryId,
                RequestedRef = request.Ref, ResolvedObject = Tag, Commit = Commit, ArchiveBytes = (uint)Bytes.Length,
                ArchiveSha256 = mode == "corrupt" ? new string('0', 64) : Convert.ToHexStringLower(SHA256.HashData(Bytes)),
                FileCount = 1, TotalBytes = PayloadSize
            }});
            for (var offset = 0; offset < Bytes.Length; offset += DevelopmentSourceBinding.ChunkBytes)
            {
                if (mode == "disconnect" && offset == WorkspaceSources.ChunkBytes)
                    throw new RpcException(new Status(StatusCode.Unavailable, "isolated stream interruption"));
                await responseStream.WriteAsync(new()
                {
                    Content = ByteString.CopyFrom(Bytes, offset, Math.Min(DevelopmentSourceBinding.ChunkBytes, Bytes.Length - offset))
                });
            }
        }

        private static byte[] Archive()
        {
            using var output = new MemoryStream();
            using (var writer = new TarWriter(output, leaveOpen: true))
            using (var body = new MemoryStream(RandomNumberGenerator.GetBytes(PayloadSize)))
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "sample.bin") { DataStream = body });
            return output.ToArray();
        }
    }
}
