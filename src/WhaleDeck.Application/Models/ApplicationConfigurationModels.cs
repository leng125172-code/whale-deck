namespace WhaleDeck.Application.Models;

public sealed record ApplicationConfigurationDto(string ResourceId, string Fingerprint,
    IReadOnlyCollection<ApplicationServiceConfigurationDto> Services, IReadOnlyCollection<string> EnvironmentNames,
    bool AutoUpdate, string VersionPolicy, string MaintenanceWindow, IReadOnlyCollection<string> Dependencies,
    IReadOnlyCollection<string> ApprovedNetworks, string OwnedDataDirectory);

public sealed record ApplicationServiceConfigurationDto(string Name, string Image, double Cpus, long MemoryMb,
    string RestartPolicy, IReadOnlyCollection<string> Ports, bool CommandPresent, bool EntrypointPresent,
    IReadOnlyCollection<string> EnvironmentNames, IReadOnlyCollection<ApplicationConfigurationMountDto> Mounts,
    IReadOnlyCollection<string> Networks, string NetworkMode);

public sealed record ApplicationConfigurationMountDto(string Type, string Source, string Target, bool ReadOnly);
