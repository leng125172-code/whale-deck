using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;

namespace WhaleDeck.Application.Abstractions;

public interface IFileGateway
{
    Task<IReadOnlyCollection<FileRootDto>> RootsAsync(CancellationToken cancellationToken);
    Task<FilePageDto> ListFilesAsync(FileLocationDto location, string after, int take, CancellationToken cancellationToken);
    Task<FileEntryDto?> InspectFileAsync(FileLocationDto location, CancellationToken cancellationToken);
    IAsyncEnumerable<FileDownloadPart> DownloadFileAsync(FileLocationDto location, string expectedVersion, CancellationToken cancellationToken);
    Task<FileUploadReceiptDto> StageFileAsync(FileUploadOperation operation, IAsyncEnumerable<ReadOnlyMemory<byte>> bytes, CancellationToken cancellationToken);
    Task<FileUploadReceiptDto?> UploadReceiptAsync(FileUploadOperation operation, CancellationToken cancellationToken);
    Task<FileUploadReceiptDto> PublishFileAsync(FileUploadOperation operation, CancellationToken cancellationToken);
    Task<FileUploadReceiptDto> DiscardFileAsync(FileUploadOperation operation, CancellationToken cancellationToken);
}
public interface IFilePlanProtector
{
    string Sign(FileUploadPlanClaims claims);
    FileUploadPlanClaims Verify(string plan);
    string Fingerprint(FileUploadBinding binding);
}
public interface IFileUploadRepository
{
    Task<FileUploadOperation?> OwnedAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileUploadOperation> EnqueueAsync(TerminalActor actor, Guid id, string key, string requestHash, FileUploadPlanClaims claims, DateTimeOffset expires, CancellationToken cancellationToken);
    Task<FileUploadOperation> BeginReceiveAsync(TerminalActor actor, Guid id, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> MayReceiveAsync(Guid id, Guid token, DateTimeOffset now, CancellationToken cancellationToken);
    Task ReadyAsync(FileUploadOperation operation, CancellationToken cancellationToken);
    Task CancelAsync(TerminalActor actor, Guid id, CancellationToken cancellationToken);
    Task<FileUploadOperation?> ClaimAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> RenewAsync(FileUploadOperation operation, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CancellationRequestedAsync(Guid id, CancellationToken cancellationToken);
    Task<FileUploadStartResult> StartedAsync(FileUploadOperation operation, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FinishAsync(FileUploadOperation operation, string state, string code, FileUploadReceiptDto? receipt, CancellationToken cancellationToken);
    Task DeferAsync(FileUploadOperation operation, CancellationToken cancellationToken);
    Task AuditReadAsync(TerminalActor actor, FileLocationDto location, string result, FileReadPurpose purpose, CancellationToken cancellationToken);
}
