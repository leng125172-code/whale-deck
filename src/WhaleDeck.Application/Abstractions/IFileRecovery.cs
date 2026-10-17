using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

public interface IFileRecoveryInventory
{
    Task<FileRecoveryPageDto> ListAsync(FileRecoveryCursor? after, int take, CancellationToken cancellationToken);
    Task<FileRecoveryCandidateDto?> GetAsync(string kind, Guid id, CancellationToken cancellationToken);
}
public interface IFileRecoveryProtector
{
    string SignRecovery(FileRecoveryPlanClaims claims);
    FileRecoveryPlanClaims VerifyRecovery(string token);
}
