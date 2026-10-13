using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhaleDeck.Api.Security;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Api.Controllers;

[ApiController]
[Authorize(Policy = "Administrator")]
[Route("api/v1")]
public sealed class GovernanceController(GovernanceService governance, WhaleDeck.Application.Abstractions.IAgentGateway agent) : ControllerBase
{
    /// <summary>Reads the approved HDD backup filesystem; unknown capacity is never reported as zero usage.</summary>
    [HttpGet("backups/capacity")]
    public async Task<IActionResult> BackupCapacity(CancellationToken cancellationToken) =>
        Ok(await agent.GetBackupTargetCapacityAsync(cancellationToken));
    [HttpGet("schedules")]
    public async Task<IActionResult> Schedules(CancellationToken cancellationToken) => Ok(await governance.ListSchedulesAsync(cancellationToken));

    [HttpPost("schedules")]
    public async Task<IActionResult> CreateSchedule([FromBody] SaveScheduledTaskCommand command, CancellationToken cancellationToken) =>
        Ok(await governance.SaveScheduleAsync(User.RequireSubject(), command with { Id = null }, cancellationToken));

    [HttpPut("schedules/{id:guid}")]
    public async Task<IActionResult> UpdateSchedule(Guid id, [FromBody] SaveScheduledTaskCommand command, CancellationToken cancellationToken) =>
        Ok(await governance.SaveScheduleAsync(User.RequireSubject(), command with { Id = id }, cancellationToken));

    [HttpDelete("schedules/{id:guid}")]
    public async Task<IActionResult> DeleteSchedule(Guid id, [FromQuery] long version, CancellationToken cancellationToken)
    {
        await governance.DeleteScheduleAsync(id, version, cancellationToken);
        return NoContent();
    }

    [HttpPost("schedules/{id:guid}/run")]
    public async Task<IActionResult> RunSchedule(Guid id, CancellationToken cancellationToken) =>
        Accepted(await governance.TriggerScheduleAsync(id, User.RequireSubject(), cancellationToken));

    [HttpGet("schedules/runs")]
    public async Task<IActionResult> ScheduleRuns(
        [FromQuery] Guid? scheduleId,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default) =>
        Ok(await governance.ListScheduleRunsAsync(scheduleId, take, cancellationToken));

    [HttpGet("backups/policies")]
    public async Task<IActionResult> BackupPolicies(CancellationToken cancellationToken) => Ok(await governance.ListBackupPoliciesAsync(cancellationToken));

    [HttpPost("backups/policies")]
    public async Task<IActionResult> SaveBackupPolicy([FromBody] SaveBackupPolicyCommand command, CancellationToken cancellationToken) =>
        Ok(await governance.SaveBackupPolicyAsync(command, cancellationToken));

    [HttpGet("backups/records")]
    public async Task<IActionResult> BackupRecords(
        [FromQuery] string? instanceResourceId,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default) =>
        Ok(await governance.ListBackupRecordsAsync(instanceResourceId, take, cancellationToken));

    [HttpGet("alerts/rules")]
    public async Task<IActionResult> AlertRules(CancellationToken cancellationToken) => Ok(await governance.ListAlertRulesAsync(cancellationToken));

    [HttpPost("alerts/rules")]
    public async Task<IActionResult> SaveAlertRule([FromBody] SaveAlertRuleCommand command, CancellationToken cancellationToken) =>
        Ok(await governance.SaveAlertRuleAsync(AlertContext(), command, cancellationToken));

    [HttpGet("alerts")]
    public async Task<IActionResult> Alerts([FromQuery] bool includeRecovered = false, CancellationToken cancellationToken = default) =>
        Ok(await governance.ListAlertsAsync(includeRecovered, cancellationToken));

    [HttpGet("alerts/history")]
    public async Task<IActionResult> AlertHistory(
        [FromQuery] Guid? alertEventId,
        [FromQuery] int take = 200,
        CancellationToken cancellationToken = default) =>
        Ok(await governance.ListAlertHistoryAsync(alertEventId, take, cancellationToken));

    [HttpPost("alerts/{id:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken cancellationToken) =>
        Ok(await governance.UpdateAlertAsync(id, AlertContext(), null, cancellationToken));

    [HttpPost("alerts/{id:guid}/silence")]
    public async Task<IActionResult> Silence(Guid id, [FromBody] SilenceAlertRequest request, CancellationToken cancellationToken) =>
        Ok(await governance.UpdateAlertAsync(id, AlertContext(), request.UntilUtc, cancellationToken));

    private AlertWriteContext AlertContext() => new(User.RequireSubject(), HttpContext.TraceIdentifier, HttpContext.Connection.RemoteIpAddress?.ToString());

    [HttpGet("settings")]
    public async Task<IActionResult> Settings(CancellationToken cancellationToken) => Ok(await governance.ListSettingsAsync(cancellationToken));

    [HttpPut("settings/{key}")]
    public async Task<IActionResult> SaveSetting(string key, [FromBody] SavePlatformSettingCommand command, CancellationToken cancellationToken) =>
        Ok(await governance.SaveSettingAsync(key, User.RequireSubject(), command, cancellationToken));

    [HttpGet("audit")]
    public async Task<IActionResult> Audit(
        [FromQuery] int take = 100,
        [FromQuery] string? actorSubject = null,
        [FromQuery] string? action = null,
        [FromQuery] string? result = null,
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default) =>
        Ok(await governance.ListAuditAsync(take, actorSubject, action, result, fromUtc, toUtc, false, cancellationToken));

    [HttpGet("audit/export")]
    public async Task<IActionResult> ExportAudit(
        [FromQuery] int take = 5000,
        [FromQuery] string? actorSubject = null,
        [FromQuery] string? action = null,
        [FromQuery] string? result = null,
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var events = await governance.ListAuditAsync(take, actorSubject, action, result, fromUtc, toUtc, true, cancellationToken);
        var csv = new StringBuilder("id,occurredAtUtc,actorSubject,action,targetType,targetId,result,sourceIp,jobId,traceId\r\n");
        foreach (var item in events)
        {
            csv.AppendJoin(',', Csv(item.Id.ToString("D")), Csv(item.OccurredAtUtc.ToString("O")), Csv(item.ActorSubject),
                Csv(item.Action), Csv(item.TargetType), Csv(item.TargetId), Csv(item.Result), Csv(item.SourceIp),
                Csv(item.JobId?.ToString("D")), Csv(item.TraceId)).Append("\r\n");
        }
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            $"whaledeck-audit-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.csv");
    }

    private static string Csv(string? value)
    {
        var safe = value ?? string.Empty;
        if (safe.Length > 0 && safe[0] is '=' or '+' or '-' or '@') safe = "'" + safe;
        return $"\"{safe.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    public sealed record SilenceAlertRequest(DateTimeOffset UntilUtc);
}
