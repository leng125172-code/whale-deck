using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public static class NotificationSources
{
    public static NotificationEvent? Alert(AlertEvent source, AlertEventHistory history, bool silenced)
    {
        if (silenced || history.State is not ("Active" or "SeverityChanged" or "Recovered" or "SilenceExpired")) return null;
        return new()
        {
            SourceKey = $"alert-history:{history.Id:D}", SourceKind = "Alert", SourceId = source.Id, ResourceId = source.ResourceId,
            Audience = "Administrator", MessageCode = $"ALERT_{history.State.ToUpperInvariant()}",
            Severity = history.State == "Recovered" ? "Info" : source.Severity,
            OccurredAtUtc = history.OccurredAtUtc
        };
    }

    public static NotificationEvent? Job(OperationJob source)
    {
        if (source.State is not ("Succeeded" or "Failed" or "Canceled" or "RolledBack") || source.CompletedAtUtc is null) return null;
        var personal = !source.ActorSubject.StartsWith("system:", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(source.ActorSubject);
        return new()
        {
            SourceKey = $"job:{source.Id:D}:{source.State}", SourceKind = "Job", SourceId = source.Id, ResourceId = source.ResourceId,
            Audience = personal ? "Subject" : "Administrator", RecipientSubject = personal ? source.ActorSubject : null,
            MessageCode = $"JOB_{source.State.ToUpperInvariant()}", Severity = source.State is "Failed" or "RolledBack" ? "Warning" : "Info",
            OccurredAtUtc = source.CompletedAtUtc.Value
        };
    }

    public static (string Title, string Summary) Text(NotificationEvent item) => item.MessageCode switch
    {
        "ALERT_ACTIVE" => ("告警触发", "工作站发现新的风险，请查看告警详情。"),
        "ALERT_SEVERITYCHANGED" => ("告警级别变更", "风险级别发生变化，请重新检查。"),
        "ALERT_RECOVERED" => ("告警已恢复", "风险已恢复或规则已明确关闭，详情保留在告警历史。"),
        "ALERT_SILENCEEXPIRED" => ("告警静默到期", "风险仍需检查；静默到期不表示问题已解决。"),
        "JOB_SUCCEEDED" => ("任务已完成", "后台任务已执行完成。"),
        "JOB_FAILED" => ("任务失败", "后台任务执行失败，请查看任务诊断。"),
        "JOB_CANCELED" => ("任务已取消", "取消结果与已执行步骤请查看任务详情。"),
        "JOB_ROLLEDBACK" => ("任务已回退", "操作已结束并执行了该任务允许的恢复步骤。"),
        "NOTIFICATION_TEST" => ("通知测试已提交", "测试发送已提交；实际投递状态请查看通知管理，不代表消息已送达。"),
        _ => ("平台消息", "请查看关联记录。")
    };
}
