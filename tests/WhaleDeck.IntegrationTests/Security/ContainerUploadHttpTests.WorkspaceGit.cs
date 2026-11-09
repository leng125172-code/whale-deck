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
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenUnixSocket(socket, listen => listen.Protocols = HttpProtocols.Http2));
            builder.Services.AddGrpc();
            builder.Services.AddSingleton(upstream);
            await using var wire = builder.Build();
            wire.MapGrpcService<WorkspaceGitWireFixture>();
            wire.MapGrpcService<WorkspaceGitCapabilitiesFixture>();
            await wire.StartAsync();

            await using var server = await Fixture.Start(services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton(policy);
                services.AddSingleton<IPrivilegedIdentityDirectory>(identities);
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
            Console.WriteLine("INBOX_PHASE=workspace-git-real-http-uds-grpc-postgres-pinned-lease-recovery-encrypted-and-confirmed");

            string Url(Guid item) => $"{collection}/{item:D}";
            async Task<WorkspaceSourceView> Read(Guid item) => (await owner.GetFromJsonAsync<WorkspaceSourceView>(Url(item)))!;
            Task Begin(Guid item) => Expect(HttpMethod.Put, Url(item) + "/git", JsonContent.Create(source), HttpStatusCode.Accepted, owner, csrf);
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
            new CapabilitiesResponse { ProtocolMajor = 1, ProtocolMinor = DevelopmentGitBinding.ProtocolMinor, Capabilities = { DevelopmentGitBinding.Capability } });
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
