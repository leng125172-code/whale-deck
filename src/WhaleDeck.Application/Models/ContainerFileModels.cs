namespace WhaleDeck.Application.Models;

public sealed record ContainerFileLocationDto(string ContainerId, string InstanceId, string Path);
public sealed record ContainerFileTargetDto(string ContainerId, string Name, string InstanceId, string ImageId, string ConfiguredUser);
