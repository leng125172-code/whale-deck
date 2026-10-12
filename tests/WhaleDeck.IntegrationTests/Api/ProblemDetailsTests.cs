using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Api.Middleware;
using Grpc.Core;

namespace WhaleDeck.IntegrationTests.Api;

public sealed class ProblemDetailsTests
{
    [Theory]
    [MemberData(nameof(ExpectedMappings))]
    public async Task ExpectedExceptionsProduceStableProblemDetails(
        Exception exception,
        int expectedStatus,
        string expectedCode)
    {
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "test-trace";
        context.Response.Body = new MemoryStream();
        context.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature { Error = exception });

        await TraceAndErrors.WriteProblemAsync(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
        Assert.Equal("test-trace", document.RootElement.GetProperty("traceId").GetString());
    }

    public static TheoryData<Exception, int, string> ExpectedMappings => new()
    {
        { new RpcException(new Status(StatusCode.InvalidArgument, "private")), StatusCodes.Status400BadRequest, "AGENT_INVALID_ARGUMENT" },
        { new RpcException(new Status(StatusCode.NotFound, "private")), StatusCodes.Status404NotFound, "AGENT_RESOURCE_NOT_FOUND" },
        { new RpcException(new Status(StatusCode.PermissionDenied, "private")), StatusCodes.Status403Forbidden, "AGENT_OPERATION_FORBIDDEN" },
        { new RpcException(new Status(StatusCode.FailedPrecondition, "private")), StatusCodes.Status409Conflict, "AGENT_PRECONDITION_FAILED" },
        { new RpcException(new Status(StatusCode.Unavailable, "private")), StatusCodes.Status503ServiceUnavailable, "AGENT_UNAVAILABLE" },
        { new DbUpdateConcurrencyException(), StatusCodes.Status409Conflict, "CONCURRENCY_CONFLICT" },
        { new InvalidOperationException(), StatusCodes.Status409Conflict, "OPERATION_CONFLICT" },
        { new ArgumentException(), StatusCodes.Status400BadRequest, "VALIDATION_FAILED" },
        { new KeyNotFoundException(), StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND" },
        { new UnauthorizedAccessException(), StatusCodes.Status403Forbidden, "AUTH_FORBIDDEN" }
    };
}
