using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Grpc.Core;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Api.Middleware;

public static class TraceAndErrors
{
    public static async Task WriteProblemAsync(HttpContext context)
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var (status, code, title) = exception switch
        {
            RpcException { StatusCode: StatusCode.InvalidArgument } => (StatusCodes.Status400BadRequest, "AGENT_INVALID_ARGUMENT", "The Agent request is invalid."),
            RpcException { StatusCode: StatusCode.NotFound } => (StatusCodes.Status404NotFound, "AGENT_RESOURCE_NOT_FOUND", "The Agent resource was not found."),
            RpcException { StatusCode: StatusCode.PermissionDenied } => (StatusCodes.Status403Forbidden, "AGENT_OPERATION_FORBIDDEN", "The Agent operation is not permitted."),
            RpcException { StatusCode: StatusCode.FailedPrecondition } => (StatusCodes.Status409Conflict, "AGENT_PRECONDITION_FAILED", "The Agent operation prerequisites are not satisfied."),
            RpcException { StatusCode: StatusCode.Aborted } => (StatusCodes.Status409Conflict, "AGENT_VERSION_CONFLICT", "The resource changed since planning."),
            RpcException { StatusCode: StatusCode.ResourceExhausted } => (StatusCodes.Status429TooManyRequests, "AGENT_RESOURCE_BUDGET", "The Agent resource budget is exhausted. Try again later."),
            RpcException { StatusCode: StatusCode.Unavailable or StatusCode.DeadlineExceeded } => (StatusCodes.Status503ServiceUnavailable, "AGENT_UNAVAILABLE", "The Agent is temporarily unavailable."),
            RpcException { StatusCode: StatusCode.Unimplemented } => (StatusCodes.Status503ServiceUnavailable, "AGENT_CAPABILITY_UNSUPPORTED", "The Agent does not support this capability."),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "AUTH_FORBIDDEN", "The operation is not permitted."),
            NotificationSnapshotUnavailableException => (StatusCodes.Status404NotFound, "NOTIFICATION_READ_SNAPSHOT_UNAVAILABLE", "The notification read snapshot is unavailable. Prepare a new snapshot."),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "The requested resource was not found."),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "CONCURRENCY_CONFLICT", "The resource was changed by another request."),
            ArgumentException => (StatusCodes.Status400BadRequest, "VALIDATION_FAILED", "The request is invalid."),
            InvalidOperationException => (StatusCodes.Status409Conflict, "OPERATION_CONFLICT", "The operation cannot be completed in the current state."),
            _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "An internal error occurred.")
        };
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Extensions = { ["code"] = code, ["traceId"] = context.TraceIdentifier }
        });
    }
}
