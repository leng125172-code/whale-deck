namespace WhaleDeck.Domain.Entities;

public sealed class DevelopmentWorkspaceRecord
{
    public Guid Id { get; init; }
    public required string Slug { get; init; }
    public required string OwnerSubject { get; init; }
    public required string StorageRootId { get; init; }
    public required string State { get; set; }
    public long Version { get; set; }
    public string LifecycleState { get; set; } = "Active";
    public long LifecycleVersion { get; set; } = 1;
    public int CpuMillis { get; init; }
    public int MemoryMiB { get; init; }
    public int DiskMiB { get; init; }
    public required string EnvironmentJson { get; init; }
    public required string EnvironmentSha256 { get; init; }
    public Guid RegistrationKey { get; init; }
    public required string RequestSha256 { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string? StoragePlanSha256 { get; set; }
    public DateTimeOffset? StorageAllocationStartedAtUtc { get; set; }
    public DateTimeOffset? StorageAllocatedAtUtc { get; set; }
}
