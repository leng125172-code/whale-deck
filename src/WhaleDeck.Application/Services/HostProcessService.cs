using System.Globalization;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public sealed class HostProcessService(IHostProcessGateway gateway)
{
    public Task<HostProcessPageDto> ListAsync(HostProcessQuery query, CancellationToken cancellationToken)
    {
        if (query.Page is < 1 or > 10_000 || query.PageSize is < 1 or > 500)
            throw new ArgumentException("Process pagination is outside the supported range.");
        if (query.Sort is not ("cpu" or "memory" or "pid" or "name"))
            throw new ArgumentException("Unsupported process sort field.");
        var filter = query.Filter?.Trim();
        if (filter?.Length > 128 || filter?.Any(char.IsControl) == true)
            throw new ArgumentException("Invalid process filter.");
        return gateway.ListProcessesAsync(query with { Filter = filter }, cancellationToken);
    }

    public Task<HostProcessDto> GetAsync(int pid, string instanceId, CancellationToken cancellationToken)
    {
        if (pid <= 0 || string.IsNullOrWhiteSpace(instanceId) || instanceId.Length > 64 ||
            !instanceId.StartsWith(pid.ToString(CultureInfo.InvariantCulture) + ":", StringComparison.Ordinal) ||
            !long.TryParse(instanceId.AsSpan(instanceId.IndexOf(':') + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) || ticks <= 0)
            throw new ArgumentException("A valid process identity is required.");
        return gateway.GetProcessAsync(pid, instanceId, cancellationToken);
    }
}
