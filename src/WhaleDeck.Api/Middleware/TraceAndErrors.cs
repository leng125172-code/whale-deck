using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Grpc.Core;
using WhaleDeck.Application.Development;
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
            WorkspaceSourceException { Failure: WorkspaceSourceFailure.Missing } => (StatusCodes.Status404NotFound, "WORKSPACE_SOURCE_PREVIOUS_NOT_FOUND", "The retained source is unavailable."),
            WorkspaceSourceException { Failure: WorkspaceSourceFailure.Conflict } => (StatusCodes.Status409Conflict, "WORKSPACE_SOURCE_PREVIOUS_CHANGED", "The retained source no longer matches the publication."),
            WorkspaceSourceException { Failure: WorkspaceSourceFailure.Rejected } => (StatusCodes.Status422UnprocessableEntity, "WORKSPACE_SOURCE_PREVIOUS_REJECTED", "The retained source cannot be exported."),
            WorkspaceSourceException { Failure: WorkspaceSourceFailure.Integrity } => (StatusCodes.Status502BadGateway, "WORKSPACE_SOURCE_PREVIOUS_INCOMPLETE", "The retained source export could not be verified."),
            WorkspaceSourceException => (StatusCodes.Status503ServiceUnavailable, "WORKSPACE_SOURCE_PREVIOUS_UNAVAILABLE", "The retained source export is temporarily unavailable."),
            WorkspaceLifecycleException { Failure: WorkspaceLifecycleFailure.Integrity } =>
                (StatusCodes.Status502BadGateway, "WORKSPACE_LIFECYCLE_UNVERIFIED", "The workspace host receipt could not be verified."),
            WorkspaceArchiveException { Failure: WorkspaceArchiveFailure.Integrity } =>
                (StatusCodes.Status502BadGateway, "WORKSPACE_ARCHIVE_UNVERIFIED", "The archive host receipt could not be verified."),
            WorkspaceArchiveException =>
                (StatusCodes.Status503ServiceUnavailable, "WORKSPACE_ARCHIVE_AGENT_REQUIRED", "The archive Agent is unavailable; the original task remains retained."),
            InvalidOperationException { Message: "WORKSPACE_ARCHIVE_NOT_SETTLED" } =>
                (StatusCodes.Status409Conflict, "WORKSPACE_ARCHIVE_NOT_SETTLED", "Cancel or complete the archive and obtain host confirmation before resuming."),
            PostgresException { SqlState: PostgresErrorCodes.CheckViolation, ConstraintName: "ck_workspace_archive_not_settled" } or
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.CheckViolation, ConstraintName: "ck_workspace_archive_not_settled" } } =>
                (StatusCodes.Status409Conflict, "WORKSPACE_ARCHIVE_NOT_SETTLED", "Cancel or complete the archive and obtain host confirmation before resuming."),
            WorkspaceLifecycleException { Failure: WorkspaceLifecycleFailure.Rejected } =>
                (StatusCodes.Status409Conflict, "WORKSPACE_LIFECYCLE_HOST_CHANGED", "The workspace host state changed. Prepare a fresh plan."),
            WorkspaceLifecycleException =>
                (StatusCodes.Status503ServiceUnavailable, "WORKSPACE_LIFECYCLE_AGENT_REQUIRED", "The workspace lifecycle Agent is unavailable."),
            InvalidOperationException { Message: "WORKSPACE_LIFECYCLE_NOT_IDLE" } =>
                (StatusCodes.Status409Conflict, "WORKSPACE_LIFECYCLE_NOT_IDLE", "Finish or cancel active tasks and source imports before stopping the workspace."),
            InvalidOperationException { Message: "WORKSPACE_LIFECYCLE_GIT_DRAINING" } =>
                (StatusCodes.Status409Conflict, "WORKSPACE_LIFECYCLE_GIT_DRAINING", "A recently canceled Git acquisition is still within its host teardown window."),
            InvalidOperationException { Message: "WORKSPACE_LIFECYCLE_INACTIVE" } =>
                (StatusCodes.Status409Conflict, "WORKSPACE_LIFECYCLE_INACTIVE", "Resume the workspace before submitting new work."),
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
