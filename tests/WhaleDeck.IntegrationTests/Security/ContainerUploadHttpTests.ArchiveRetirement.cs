using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using Grpc.Core;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.IntegrationTests.Security;

public sealed partial class ContainerUploadHttpTests
{
    private static string RetirementBase => $"/api/v1/containers/{Cid}/files/recycle-archive-retirements";
    private const string RetirementId = "aa0a8865-343e-49bd-b01a-72680131bf39";
    private static object RetirementBody => new { archiveIntentId = RetirementId, retainDays = 30 };

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("ordinary-subject", HttpStatusCode.Forbidden)]
    public async Task ArchiveRetirementRoutesEnforceAdministratorCookiePolicy(string subject, HttpStatusCode expected)
    {
        await using var fixture = await Fixture.Start();
        using var client = await fixture.Client(subject, false);
        var csrf = await Csrf(client);
        foreach (var suffix in new[] { "", $"/{RetirementId}/confirm", $"/{RetirementId}/cancel", $"/{RetirementId}/recovery/acknowledge" })
        {
            using var request = Write(client, HttpMethod.Post, RetirementBase + suffix, csrf, JsonContent.Create(RetirementBody), RetirementId);
            using var result = await client.SendAsync(request);
            Assert.Equal(expected, result.StatusCode);
        }
        foreach (var suffix in new[] { $"/{RetirementId}", $"/by-key/{RetirementId}", $"/{RetirementId}/recovery" })
        {
            using var result = await client.GetAsync(RetirementBase + suffix);
            Assert.Equal(expected, result.StatusCode);
        }
        Assert.Equal(0, fixture.Retirement.Calls);
    }

    [Fact]
    public async Task ArchiveRetirementRejectsOriginCsrfAndMalformedRequestsBeforeGateway()
    {
        await using var fixture = await Fixture.Start();
        using var client = await fixture.Client("admin-subject", true);
        var csrf = await Csrf(client);
        using var missingCsrf = Write(client, HttpMethod.Post, RetirementBase, null, JsonContent.Create(RetirementBody), RetirementId);
        using var denied = await client.SendAsync(missingCsrf);
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        using var badOrigin = Write(client, HttpMethod.Post, RetirementBase, csrf, JsonContent.Create(RetirementBody), RetirementId);
        badOrigin.Headers.Remove("Origin");
        badOrigin.Headers.Add("Origin", "http://unapproved.invalid");
        using var foreign = await client.SendAsync(badOrigin);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        var body = $"{{\"archiveIntentId\":\"{RetirementId}\",\"retainDays\":30,\"retainDays\":\"private-must-not-leak\"}}";
        using var badBody = Write(client, HttpMethod.Post, RetirementBase, csrf, new StringContent(body, Encoding.UTF8, "application/json"), RetirementId);
        using var malformed = await client.SendAsync(badBody);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        var error = await malformed.Content.ReadAsStringAsync();
        Assert.Contains("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_INPUT_INVALID", error, StringComparison.Ordinal);
        Assert.DoesNotContain("private-must-not-leak", error, StringComparison.Ordinal);

        using var badKey = Write(client, HttpMethod.Post, RetirementBase, csrf, JsonContent.Create(RetirementBody), RetirementId.ToUpperInvariant());
        using var invalidKey = await client.SendAsync(badKey);
        Assert.Equal(HttpStatusCode.BadRequest, invalidKey.StatusCode);
        Assert.Equal(0, fixture.Retirement.Calls);
    }

    [Fact]
    public async Task ArchiveRetirementRealMvcReturns503ForMissingAgentCapability()
    {
        await using var fixture = await Fixture.Start();
        using var client = await fixture.Client("admin-subject", true);
        var csrf = await Csrf(client);
        using var request = Write(client, HttpMethod.Post, RetirementBase, csrf, JsonContent.Create(RetirementBody), RetirementId);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("AGENT_CAPABILITY_UNSUPPORTED", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(1, fixture.Retirement.Calls);
    }

    public class RetirementHttpRepository : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IContainerRecycleArchiveRetirementRepository.FindAsync) ? Task.FromResult<ContainerRecycleArchiveRetirementIntent?>(null)
            : throw new InvalidOperationException("The HTTP boundary fixture must not write retirement state.");
    }

    private sealed class RetirementHttpGate : IContainerRecycleArchiveRetirementGateway
    {
        public int Calls { get; private set; }
        public Task RequireContainerRecycleArchiveRetirementAsync(CancellationToken cancellationToken)
        {
            Calls++;
            throw new RpcException(new Status(StatusCode.Unimplemented, "Synthetic missing retirement capability"));
        }
        public Task<ContainerRecycleArchiveRetirementPlanDto> PlanContainerRecycleArchiveRetirementAsync(TerminalActor actor, ContainerRecycleArchiveRetirementCommandDto command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ContainerRecycleArchiveRetirementReceiptDto> ReserveContainerRecycleArchiveRetirementAsync(ContainerRecycleArchiveRetirementOperationDto operation, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ContainerRecycleArchiveRetirementReceiptDto> ApplyContainerRecycleArchiveRetirementAsync(ContainerRecycleArchiveRetirementOperationDto operation, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ContainerRecycleArchiveRetirementReceiptDto?> InspectContainerRecycleArchiveRetirementAsync(ContainerRecycleArchiveRetirementOperationDto operation, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ContainerRecycleArchiveRetirementReceiptDto> DiscardContainerRecycleArchiveRetirementAsync(ContainerRecycleArchiveRetirementOperationDto operation, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
