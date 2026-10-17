using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

public interface IFileTreeMoveProtector
{
    string FingerprintRequest(FileTreeMoveRequestBinding binding);
    string FingerprintPlan(FileTreeMovePlanBinding binding);
    string SignConfirmation(FileTreeMoveConfirmation claims);
    FileTreeMoveConfirmation VerifyMoveConfirmation(string token);
}
