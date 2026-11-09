using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Development;
using WhaleDeck.Application.Models;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.IntegrationTests.Security;

public sealed partial class ContainerUploadHttpTests
{
    internal static async Task DownloadWorkspaceReportsFromPostgres(
        DbContextOptions<PlatformDbContext> options, Guid workspace, Guid run, WorkspaceReportDownload stored,
        bool exerciseGrants = false)
    {
        var directory = new WorkspaceReportHttpFixture();
        await using var fixture = await Fixture.Start(services =>
        {
            services.AddSingleton(TimeProvider.System);
            services.AddScoped(_ => new PlatformDbContext(options));
            services.AddScoped<IWorkspaceRunReportStore, WorkspaceRunReportRepository>();
            services.AddScoped<IWorkspaceRunReportReadAudit, WorkspaceRunReportReadAudit>();
            services.AddSingleton<IPrivilegedIdentityDirectory>(directory);
            services.AddScoped<IWorkspaceReportAccess, WorkspaceReportAccessRepository>();
            services.AddScoped<WorkspaceReportAuthorization>();
            services.AddScoped<WorkspaceReportGrantManagement>();
            services.AddScoped<WorkspaceRunReportDownloads>();
        });
        using var client = await fixture.Client("admin-subject", true);
        using var response = await client.GetAsync($"/api/v1/development/workspaces/{workspace:D}/runs/{run:D}/report/json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(stored.Content, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(stored.Sha256, Assert.Single(response.Headers.GetValues("X-Content-SHA256")));
        using var other = await client.GetAsync($"/api/v1/development/workspaces/{Guid.NewGuid():D}/runs/{run:D}/report/json");
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        if (!exerciseGrants) return;

        using var developer = await fixture.Client("developer-subject", false);
        var download = $"/api/v1/development/workspaces/{workspace:D}/runs/{run:D}/report/json";
        var access = $"/api/v1/development/workspaces/{workspace:D}/report-access/{directory.Group:D}";
        var csrf = await Csrf(client);
        using (var before = await developer.GetAsync(download))
            Assert.Equal(HttpStatusCode.Forbidden, before.StatusCode);
        using (var put = Write(client, HttpMethod.Put, access, csrf, JsonContent.Create(new { enabled = true, expectedVersion = 0 })))
        using (var granted = await client.SendAsync(put))
            Assert.Equal(1, (await granted.Content.ReadFromJsonAsync<WorkspaceReportGrant>())!.Version);
        using (var allowed = await developer.GetAsync(download))
        {
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            Assert.Equal(stored.Content, await allowed.Content.ReadAsByteArrayAsync());
        }
        using (var foreignScope = await developer.GetAsync($"/api/v1/development/workspaces/{Guid.NewGuid():D}/runs/{run:D}/report/json"))
            Assert.Equal(HttpStatusCode.Forbidden, foreignScope.StatusCode);
        directory.LiveMembership = false;
        using (var removed = await developer.GetAsync(download))
            Assert.Equal(HttpStatusCode.Forbidden, removed.StatusCode);
        directory.LiveMembership = true;
        using (var put = Write(client, HttpMethod.Put, access, csrf, JsonContent.Create(new { enabled = false, expectedVersion = 1 })))
        using (var revoked = await client.SendAsync(put))
            Assert.Equal(new WorkspaceReportGrant(directory.Group, false, 2),
                await revoked.Content.ReadFromJsonAsync<WorkspaceReportGrant>());
        using (var denied = await developer.GetAsync(download))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var put = Write(client, HttpMethod.Put, access, csrf, JsonContent.Create(new { enabled = true, expectedVersion = 0 })))
        using (var stale = await client.SendAsync(put))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var manager = await developer.GetAsync(access);
        Assert.Equal(HttpStatusCode.Forbidden, manager.StatusCode);
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("ordinary-subject", HttpStatusCode.Forbidden)]
    public async Task WorkspaceReportsRequireAnAuthenticatedAdministrator(string subject, HttpStatusCode expected)
    {
        await using var fixture = await Fixture.Start();
        using var client = await fixture.Client(subject, false);
        using var response = await client.GetAsync(fixture.WorkspaceReports.Url("json"));
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0, fixture.WorkspaceReports.Reads);
        Assert.Equal(0, fixture.WorkspaceReports.Audits);
    }

    [Theory]
    [InlineData("json", "application/json", ".json")]
    [InlineData("markdown", "text/markdown", ".md")]
    public async Task WorkspaceReportsServeAuditedNonCacheableAttachments(string format, string media, string suffix)
    {
        await using var fixture = await Fixture.Start();
        using var client = await fixture.Client("admin-subject", true);
        using var request = new HttpRequestMessage(HttpMethod.Get, fixture.WorkspaceReports.Url(format));
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
        request.Headers.Range = new RangeHeaderValue(0, 1);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(media, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal($"workspace-run-{fixture.WorkspaceReports.Run:D}{suffix}", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Null(response.Content.Headers.ContentRange);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(fixture.WorkspaceReports.Hash, Assert.Single(response.Headers.GetValues("X-Content-SHA256")));
        Assert.Equal(fixture.WorkspaceReports.Content, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, fixture.WorkspaceReports.Audits);
    }

    [Theory]
    [InlineData("scope", HttpStatusCode.NotFound)]
    [InlineData("expired", HttpStatusCode.NotFound)]
    [InlineData("foreign", HttpStatusCode.Forbidden)]
    [InlineData("sibling", HttpStatusCode.Forbidden)]
    [InlineData("query", HttpStatusCode.BadRequest)]
    [InlineData("format", HttpStatusCode.NotFound)]
    public async Task WorkspaceReportsRejectInvalidOrUnavailableDownloads(string variant, HttpStatusCode expected)
    {
        await using var fixture = await Fixture.Start();
        using var client = await fixture.Client("admin-subject", true);
        var report = fixture.WorkspaceReports;
        if (variant == "expired") report.Expired = true;
        var path = report.Url(variant == "format" ? "0" : "json", variant == "scope" ? Guid.NewGuid() : null);
        if (variant == "query") path += "?unexpected=1";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (variant == "foreign") request.Headers.Add("Origin", "https://outside.invalid");
        if (variant == "sibling") request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-site");
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.False(response.Headers.Contains("X-Content-SHA256"));
        Assert.Equal(variant is "scope" or "expired" ? 1 : 0, report.Reads);
        Assert.Equal(0, report.Audits);
    }

    [Theory]
    [InlineData("logout", HttpStatusCode.Forbidden)]
    [InlineData("demotion", HttpStatusCode.Forbidden)]
    [InlineData("audit-failure", HttpStatusCode.InternalServerError)]
    public async Task WorkspaceReportsWithholdBytesAfterRevocationOrAuditFailure(string variant, HttpStatusCode expected)
    {
        await using var fixture = await Fixture.Start();
        using var client = await fixture.Client("admin-subject", true);
        var report = fixture.WorkspaceReports;
        if (variant == "logout") report.AfterRead = () => fixture.Store.RemoveAsync(fixture.AdminKey!);
        if (variant == "demotion") report.AfterRead = () => { report.Admin = false; return Task.CompletedTask; };
        if (variant == "audit-failure") report.FailAudit = true;
        using var response = await client.GetAsync(report.Url("json"));
        Assert.Equal(expected, response.StatusCode);
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.False(response.Headers.Contains("X-Content-SHA256"));
        Assert.DoesNotContain("qa-private-report", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(1, report.Reads);
        Assert.Equal(0, report.Audits);
    }

    [Fact]
    public async Task WorkspaceReportsRecheckDelegationAfterReadingBytes()
    {
        await using var fixture = await Fixture.Start();
        using var client = await fixture.Client("developer-subject", false);
        var report = fixture.WorkspaceReports;
        report.GrantEnabled = true;
        report.AfterRead = () => { report.GrantEnabled = false; return Task.CompletedTask; };
        using var response = await client.GetAsync(report.Url("json"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, report.Reads);
        Assert.Equal(0, report.Audits);
        Assert.False(response.Headers.Contains("X-Content-SHA256"));
    }

    private sealed class WorkspaceReportHttpFixture : IWorkspaceRunReportStore, IWorkspaceRunReportReadAudit, IPrivilegedIdentityDirectory, IWorkspaceReportAccess
    {
        public byte[] Content { get; } = "qa-private-report"u8.ToArray();
        public string Hash => Convert.ToHexStringLower(SHA256.HashData(Content));
        public Guid Workspace { get; } = Guid.NewGuid();
        public Guid Run { get; } = Guid.NewGuid();
        public Guid Group { get; } = Guid.NewGuid();
        public bool LiveMembership { get; set; } = true;
        public bool GrantEnabled { get; set; }
        public bool Expired { get; set; }
        public bool Admin { get; set; } = true;
        public bool FailAudit { get; set; }
        public int Reads { get; private set; }
        public int Audits { get; private set; }
        public Func<Task>? AfterRead { get; set; }

        public string Url(string format, Guid? workspace = null) =>
            $"/api/v1/development/workspaces/{workspace ?? Workspace:D}/runs/{Run:D}/report/{format}";

        public async Task<WorkspaceReportDownload?> ReadAsync(Guid workspaceId, Guid runId, WorkspaceReportFormat format, CancellationToken cancellationToken)
        {
            Reads++;
            if (AfterRead is not null) await AfterRead();
            return workspaceId == Workspace && runId == Run
                ? new(Content, Hash, DateTimeOffset.UtcNow.AddMinutes(Expired ? -1 : 1)) : null;
        }

        public Task RecordAsync(TerminalActor actor, Guid workspaceId, Guid runId, WorkspaceReportFormat format, CancellationToken cancellationToken)
        {
            if (FailAudit) throw new IOException("private audit failure");
            Assert.True(actor.Subject is "admin-subject" or "developer-subject");
            Assert.Equal(Workspace, workspaceId);
            Assert.Equal(Run, runId);
            Audits++;
            return Task.CompletedTask;
        }

        public Task<CurrentUserDto> ResolveForPrivilegedActionAsync(string subject, string? name, CancellationToken cancellationToken) =>
            Task.FromResult(new CurrentUserDto(subject, name,
                LiveMembership && subject == "developer-subject" ? [Group.ToString("D")] : [],
                Admin && subject == "admin-subject", DateTimeOffset.UtcNow.AddHours(1)));

        public Task<bool> AllowsAsync(Guid workspaceId, Guid[] groupIds, CancellationToken token) =>
            Task.FromResult(GrantEnabled && workspaceId == Workspace && groupIds.Contains(Group));
        public Task<WorkspaceReportGrant> GetAsync(Guid workspaceId, Guid groupId, CancellationToken token) => throw new NotSupportedException();
        public Task<WorkspaceReportGrant> SetAsync(TerminalActor actor, Guid workspaceId, Guid groupId, bool enabled,
            long expectedVersion, CancellationToken token) => throw new NotSupportedException();

        public Task<WorkspaceReportWriteState> SaveAsync(WorkspaceRunReport report, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> ExpireAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
