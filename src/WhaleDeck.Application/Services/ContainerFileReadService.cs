using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Services;

public sealed class ContainerFileReadService(FileManagementService authorization, IContainerFileGateway gateway,
    IContainerFileReadAudit audit, TimeProvider clock)
{
    private static readonly SearchValues<char> ImageHex = SearchValues.Create("0123456789abcdef");
    public async Task<ContainerFileTargetDto> TargetAsync(TerminalActor actor, string id, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken); ContainerFileValidation.Id(id);
        var result = await gateway.InspectContainerFileTargetAsync(id, cancellationToken);
        var location = new ContainerFileLocationDto(id, result.InstanceId, ""); ContainerFileValidation.Location(location, true);
        if (result.ContainerId != id || result.ImageId is not { Length: 71 } || !result.ImageId.StartsWith("sha256:", StringComparison.Ordinal) ||
            result.ImageId.AsSpan(7).ContainsAnyExcept(ImageHex) || result.Name.Length > 256 || result.Name.Any(char.IsControl) ||
            result.ConfiguredUser.Length > 256 || result.ConfiguredUser.Any(char.IsControl)) throw ContainerFileValidation.InvalidOutput();
        await authorization.AuthorizeAsync(actor, cancellationToken);
        await audit.RecordAsync(actor, location, "target", "Validated", null, null, null, cancellationToken);
        return result;
    }

    public async Task<FilePageDto> ListAsync(TerminalActor actor, ContainerFileLocationDto location, string after, int take, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken); ContainerFileValidation.Location(location, true); ContainerFileValidation.Cursor(after);
        if (take is < 1 or > 100) throw new ArgumentException("CONTAINER_FILES_LIST_INVALID");
        var result = await gateway.ListContainerFilesAsync(location, after, take, cancellationToken);
        ContainerFileValidation.Page(result, after, take);
        await authorization.AuthorizeAsync(actor, cancellationToken);
        await audit.RecordAsync(actor, location, "list", "Validated", result.Version, null, null, cancellationToken);
        return result;
    }

    public async Task<FileEntryDto> InspectAsync(TerminalActor actor, ContainerFileLocationDto location, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(actor, cancellationToken); ContainerFileValidation.Location(location, true);
        var result = await gateway.InspectContainerFileAsync(location, cancellationToken) ?? throw new KeyNotFoundException("CONTAINER_FILES_NOT_FOUND");
        ContainerFileValidation.Entry(result, location.Path.Length == 0);
        if (result.Name != location.Path.Split('/')[^1]) throw ContainerFileValidation.InvalidOutput();
        await authorization.AuthorizeAsync(actor, cancellationToken);
        await audit.RecordAsync(actor, location, "inspect", "Validated", result.Version, result.SizeBytes, null, cancellationToken);
        return result;
    }

    /// <summary>Only emits end after exact RPC EOF, full bytes/digest, current login/permission and durable audit.</summary>
    public async IAsyncEnumerable<FileDownloadPart> DownloadAsync(TerminalActor actor, ContainerFileLocationDto location, string version,
        Func<CancellationToken, Task> validateLogin, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await validateLogin(cancellationToken); await authorization.AuthorizeAsync(actor, cancellationToken);
        ContainerFileValidation.Location(location); ContainerFileValidation.Hash(version);
        await audit.RecordAsync(actor, location, "download", "Started", version, null, null, cancellationToken);
        var validated = false; FileDownloadPart? end = null; FileEntryDto? metadata = null; long offset = 0;
        var next = clock.GetUtcNow(); using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            await foreach (var part in gateway.DownloadContainerFileAsync(location, version, cancellationToken).WithCancellation(cancellationToken))
            {
                if (end is not null) throw ContainerFileValidation.InvalidOutput();
                if (clock.GetUtcNow() >= next)
                {
                    await validateLogin(cancellationToken); await authorization.AuthorizeAsync(actor, cancellationToken);
                    next = clock.GetUtcNow().AddSeconds(6);
                }
                if (part.Kind == "metadata" && metadata is null && offset == 0 && part.Metadata is { } entry)
                {
                    ContainerFileValidation.Entry(entry);
                    if (entry.Name != location.Path.Split('/')[^1] || entry.Kind != "File" || entry.IsProtected || entry.Version != version ||
                        entry.SizeBytes > FileManagementService.MaximumBytes) throw ContainerFileValidation.InvalidOutput();
                    metadata = entry;
                }
                else if (part.Kind == "chunk" && metadata is not null && part.Offset == offset && part.Data.Length is > 0 and <= 32768 && offset + part.Data.Length <= metadata.SizeBytes)
                { digest.AppendData(part.Data.Span); offset += part.Data.Length; }
                else if (part.Kind == "end" && metadata is not null && offset == metadata.SizeBytes && part.SizeBytes == offset &&
                    part.Version == version && FileManagementService.Hex(part.Sha256) && part.Sha256 == Convert.ToHexString(digest.GetHashAndReset()))
                { end = part; continue; }
                else throw ContainerFileValidation.InvalidOutput();
                yield return part;
            }
            if (end is null) throw ContainerFileValidation.InvalidOutput();
            await validateLogin(cancellationToken); await authorization.AuthorizeAsync(actor, cancellationToken);
            await audit.RecordAsync(actor, location, "download", "Validated", version, offset, end.Sha256, cancellationToken);
            validated = true; yield return end;
        }
        finally
        {
            if (!validated)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await audit.RecordAsync(actor, location, "download", "Incomplete", version, null, null, cleanup.Token);
            }
        }
    }
}
