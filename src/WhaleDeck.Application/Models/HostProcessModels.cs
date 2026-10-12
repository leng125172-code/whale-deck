namespace WhaleDeck.Application.Models;

public sealed record HostProcessQuery(int Page = 1, int PageSize = 50, string Sort = "cpu", bool Descending = true, string? Filter = null);

public sealed record HostProcessDto(
    int Pid, int ParentPid, uint UserId, string InstanceId, string Name, string State, int ThreadCount,
    DateTimeOffset StartedAtUtc, double CpuTimeSeconds, double? CpuPercent, long MemoryBytes, long VirtualMemoryBytes);

public sealed record HostProcessPageDto(
    IReadOnlyCollection<HostProcessDto> Items, int Total, int Page, int PageSize,
    DateTimeOffset SampledAtUtc, double CpuWindowSeconds, bool IsStale, bool IsTruncated, int SkippedCount);
