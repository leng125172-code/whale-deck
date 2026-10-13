using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.Worker;

public static class BackupCapacityAlerts
{
    public static async Task EvaluateAsync(PlatformDbContext db, IAgentGateway agent, CancellationToken cancellationToken)
    {
        var policies = await db.BackupPolicies.Where(item => item.TargetDirectoryId == BackupTargetCapacityDto.FixedTarget)
            .OrderBy(item => item.Id).Take(1000).ToArrayAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var capacity = policies.Any(item => item.IsEnabled) ? await agent.GetBackupTargetCapacityAsync(cancellationToken) :
            BackupTargetCapacityDto.Unknown("NO_ENABLED_POLICY");
        var ruleIds = policies.Select(item => RuleId(item.Id)).ToArray();
        var rules = await db.AlertRules.Where(item => ruleIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        var events = await db.AlertEvents.Where(item => ruleIds.Contains(item.RuleId) && item.State != "Recovered").ToArrayAsync(cancellationToken);
        foreach (var policy in policies)
        {
            var id = RuleId(policy.Id);
            if (!rules.TryGetValue(id, out var rule))
            {
                rule = new AlertRule { Id = id, RuleType = BackupCapacityPolicy.RuleType };
                db.AlertRules.Add(rule);
            }
            if (rule.RuleType != BackupCapacityPolicy.RuleType) throw new InvalidOperationException("Managed capacity rule identity collision.");
            rule.IsEnabled = policy.IsEnabled;
            rule.ResourceSelectorJson = JsonSerializer.Serialize(new { backupPolicyId = policy.Id, targetDirectoryId = policy.TargetDirectoryId });
            rule.ThresholdJson = JsonSerializer.Serialize(new { warningPercent = policy.CapacityWarningPercent, criticalPercent = policy.CapacityCriticalPercent });
            rule.EvaluationWindowSeconds = 60;
            var active = events.Where(item => item.RuleId == id).ToArray();
            if (active.Length == 0 && BackupCapacityPolicy.Triggered(policy, capacity, now))
            {
                var item = new AlertEvent
                {
                    RuleId = id,
                    ResourceId = policy.InstanceResourceId,
                    Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"backup-capacity:{policy.Id:D}"))),
                    Severity = BackupCapacityPolicy.Severity(policy, capacity, now),
                    SummaryCode = "ALERT_BACKUPSTORAGECAPACITY",
                    DetailJson = BackupCapacityPolicy.Detail(policy, capacity, now)
                };
                db.AlertEvents.Add(item);
                Record(db, item, "Active");
            }
            foreach (var item in active)
            {
                var change = BackupCapacityPolicy.Apply(item, policy, capacity, now);
                if (change is not null) Record(db, item, change);
            }
        }
        MonitoringAlerts.TouchChangedEvents(db);
        await db.SaveChangesAsync(cancellationToken);
    }

    public static Guid RuleId(Guid policyId) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"whaledeck:backup-capacity:{policyId:D}")).AsSpan(0, 16));

    private static void Record(PlatformDbContext db, AlertEvent item, string state) => db.AlertEventHistory.Add(
        new AlertEventHistory { AlertEventId = item.Id, State = state, ActorSubject = "system:backup-capacity" });
}
