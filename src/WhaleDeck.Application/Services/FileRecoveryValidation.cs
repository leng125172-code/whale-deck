using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Services;

public static class FileRecoveryValidation
{
    public const int MaximumPageSize = 50;
    public static string ReceiptHash(string kind, Guid execution, string state, string canonicalJson) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes("files-recovery-observation-v1\n" + kind + "\n" + execution.ToString("N") + "\n" + state + "\n" + canonicalJson)));
    public static void Command(QueueFileRecoveryCommand command)
    {
        Kind(command.Kind);
        if (command.IntentId == Guid.Empty || command.OriginalVersion < 0 || command.Disposition is not ("AcknowledgeUnknown" or "ReconcileCompleted") ||
            command.ReasonCode is not ("OriginalSessionLost" or "OperatorHandover" or "IncidentReview")) throw new ArgumentException("FILES_RECOVERY_COMMAND_INVALID");
    }
    public static FileRecoveryTargetBinding Target(FileRecoveryOperation row) => new(row.Kind, row.OriginalIntentId, row.OriginalExecutionJobId, row.OriginalVersion, row.OriginalBindingHash);
    public static FileRecoverySnapshot Snapshot(FileRecoveryOperation row) => new(row.Kind, row.OriginalJson, Target(row));
    public static FileRecoveryRequestBinding Request(FileRecoveryOperation row) => new(row.Id, row.ActorSubject, row.LoginBinding, row.IdempotencyKey,
        Target(row), row.Disposition, row.ReasonCode, row.CreatedAtUtc, row.PlanningExpiresAtUtc);
    public static FileRecoveryPlanClaims Ready(FileRecoveryOperation row) => new(row.Id, row.ActorSubject, row.LoginBinding, Target(row),
        row.ObservationHash ?? throw new InvalidOperationException("FILES_RECOVERY_PLAN_MISSING"), row.RootState ?? throw new InvalidOperationException("FILES_RECOVERY_PLAN_MISSING"),
        row.Disposition, row.ReasonCode, row.ReadyAtUtc ?? throw new InvalidOperationException("FILES_RECOVERY_PLAN_MISSING"), row.ReadyExpiresAtUtc ?? throw new InvalidOperationException("FILES_RECOVERY_PLAN_MISSING"));
    public static bool Bound(FileRecoveryOperation row, IFileRecoveryProtector protector)
    {
        try
        {
            var actor = new TerminalActor(row.ActorSubject, null, [], row.LoginBinding, row.Id.ToString("D"), null);
            Command(new(row.Kind, row.OriginalIntentId, row.OriginalVersion, row.Disposition, row.ReasonCode)); Target(Target(row));
            if (RegistrationId(actor, row.IdempotencyKey) != row.Id || row.PlanningExpiresAtUtc != row.CreatedAtUtc.AddMinutes(15) ||
                !Equal(row.RequestHash, protector.FingerprintRecoveryRequest(Request(row)))) return false;
            if (row.PlanHash is null) return row.RootState is null && row.ObservationHash is null && row.ObservationJson is null && row.ReadyAtUtc is null && row.ReadyExpiresAtUtc is null && row.ExecutionJobId is null;
            var claims = Ready(row); Claims(claims);
            return Equal(row.PlanHash, protector.FingerprintRecoveryReady(claims)) && row.ObservationJson is not null && row.ObservationBindingCurrent is not null &&
                (row.ExecutionJobId is null || row.ExecutionJobId == ExecutionId(row.Id));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return false; }
    }
    private static bool Equal(string? left, string right) => FileManagementService.Hex(left) && FileManagementService.Hex(right) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left!), Encoding.ASCII.GetBytes(right));
    public static void Kind(string kind)
    {
        if (!FileRecoveryKinds.All.Contains(kind, StringComparer.Ordinal)) throw new ArgumentException("FILES_RECOVERY_KIND_INVALID");
    }
    public static int Rank(string kind) => kind switch
    {
        FileRecoveryKinds.Archive => 0, FileRecoveryKinds.Mutation => 1, FileRecoveryKinds.TreeCopy => 2,
        FileRecoveryKinds.TreeMove => 3, FileRecoveryKinds.Upload => 4, FileRecoveryKinds.TreePacking => 5, FileRecoveryKinds.TreeUnpacking => 6, FileRecoveryKinds.TreeRecycling => 7,
        FileRecoveryKinds.TreeRestoring => 8, FileRecoveryKinds.TreeOverwriteRestoring => 9, _ => throw new ArgumentException("FILES_RECOVERY_KIND_INVALID")
    };
    public static void Target(FileRecoveryTargetBinding target)
    {
        if (target is null) throw new ArgumentException("FILES_RECOVERY_TARGET_INVALID");
        Kind(target.Kind);
        var execution = target.Kind switch
        {
            FileRecoveryKinds.TreeCopy => FileTreeCopyValidation.ExecutionId(target.IntentId),
            FileRecoveryKinds.TreeMove => FileTreeMoveValidation.ExecutionId(target.IntentId),
            FileRecoveryKinds.TreePacking => FileTreePackingValidation.ExecutionId(target.IntentId),
            FileRecoveryKinds.TreeUnpacking => FileTreeUnpackingValidation.ExecutionId(target.IntentId),
            FileRecoveryKinds.TreeRecycling => FileTreeRecyclingValidation.ExecutionId(target.IntentId),
            FileRecoveryKinds.TreeRestoring => FileTreeRestoringValidation.ExecutionId(target.IntentId),
            FileRecoveryKinds.TreeOverwriteRestoring => FileTreeOverwriteRestoringValidation.ExecutionId(target.IntentId),
            _ => target.IntentId
        };
        if (target.IntentId == Guid.Empty || target.ExecutionJobId != execution || target.OriginalVersion < 0 ||
            !FileManagementService.Hex(target.OriginalBindingHash)) throw new ArgumentException("FILES_RECOVERY_TARGET_INVALID");
    }
    public static void Claims(FileRecoveryPlanClaims claims)
    {
        if (claims is null || claims.Id == Guid.Empty || string.IsNullOrWhiteSpace(claims.Actor) || claims.Actor.Length > 256 ||
            claims.Actor.Any(char.IsControl) || !FileManagementService.Hex(claims.LoginBinding) || !FileManagementService.Hex(claims.ObservationHash))
            throw new ArgumentException("FILES_RECOVERY_PLAN_INVALID");
        Target(claims.Target);
        if (claims.ReasonCode is not ("OriginalSessionLost" or "OperatorHandover" or "IncidentReview") ||
            claims.RootState is not ("Missing" or "Aborted" or "Unknown" or "Completed") ||
            claims.Disposition is not ("AcknowledgeUnknown" or "ReconcileCompleted") ||
            (claims.RootState == "Completed") != (claims.Disposition == "ReconcileCompleted") ||
            claims.IssuedAtUtc.Offset != TimeSpan.Zero || claims.ExpiresAtUtc.Offset != TimeSpan.Zero ||
            claims.IssuedAtUtc < DateTimeOffset.UnixEpoch || claims.ExpiresAtUtc <= claims.IssuedAtUtc ||
            claims.ExpiresAtUtc - claims.IssuedAtUtc > TimeSpan.FromMinutes(5)) throw new ArgumentException("FILES_RECOVERY_PLAN_INVALID");
    }
    public static void RequireCurrentActor(FileRecoveryPlanClaims claims, TerminalActor actor, DateTimeOffset now)
    {
        Claims(claims);
        if (claims.Actor != actor.Subject || claims.LoginBinding != actor.LoginBinding) throw new UnauthorizedAccessException("FILES_RECOVERY_CURRENT_LOGIN_REQUIRED");
        if (claims.ExpiresAtUtc <= now || claims.IssuedAtUtc > now.AddSeconds(5)) throw new InvalidOperationException("FILES_RECOVERY_PLAN_EXPIRED");
    }
    public static Guid RegistrationId(TerminalActor actor, string key)
    {
        _ = FileTreeCopyValidation.IntentId(actor, key);
        return new(SHA256.HashData(Encoding.UTF8.GetBytes("files-administrator-recovery-plan-v1:" + actor.Subject + ":" + actor.LoginBinding + ":" + key)).AsSpan(0, 16));
    }
    public static Guid ExecutionId(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("FILES_RECOVERY_ID_INVALID");
        return new(SHA256.HashData(Encoding.UTF8.GetBytes("files-administrator-recovery-execution-v1:" + id.ToString("N"))).AsSpan(0, 16));
    }
    public static FileRecoveryCursor? DecodeCursor(string? after)
    {
        if (string.IsNullOrEmpty(after)) return null;
        if (after.Length > 160 || after.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
            throw new ArgumentException("FILES_RECOVERY_CURSOR_INVALID");
        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(after.Replace('-', '+').Replace('_', '/') + new string('=', (4 - after.Length % 4) % 4)));
            var parts = text.Split(':');
            if (parts.Length != 3 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
                ticks < DateTimeOffset.UnixEpoch.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks ||
                !Guid.TryParseExact(parts[2], "N", out var id) || id == Guid.Empty) throw new ArgumentException("FILES_RECOVERY_CURSOR_INVALID");
            Kind(parts[1]); var result = new FileRecoveryCursor(new(ticks, TimeSpan.Zero), parts[1], id);
            if (EncodeCursor(result) != after) throw new ArgumentException("FILES_RECOVERY_CURSOR_INVALID");
            return result;
        }
        catch (Exception error) when (error is FormatException or DecoderFallbackException)
        { throw new ArgumentException("FILES_RECOVERY_CURSOR_INVALID"); }
    }
    public static string EncodeCursor(FileRecoveryCursor value)
    {
        Kind(value.Kind);
        if (value.IntentId == Guid.Empty || value.CreatedAtUtc < DateTimeOffset.UnixEpoch) throw new ArgumentException("FILES_RECOVERY_CURSOR_INVALID");
        var text = value.CreatedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture) + ":" + value.Kind + ":" + value.IntentId.ToString("N");
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
