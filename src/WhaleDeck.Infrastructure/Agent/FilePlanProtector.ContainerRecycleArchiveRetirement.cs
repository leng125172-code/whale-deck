using WhaleDeck.Application.Abstractions;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.Infrastructure.Agent;

public sealed partial class FilePlanProtector : IContainerRecycleArchiveRetirementSourceProtector
{
    public string FingerprintRetirementSource(ContainerRecycleArchiveRetirementSourceBinding binding)
    {
        if (binding.ArchiveIntentId == Guid.Empty || !FileManagementService.Hex(binding.RequestHash) || !FileManagementService.Hex(binding.PlanHash) ||
            !FileManagementService.Hex(binding.ExecutionHash) || !FileManagementService.Hex(binding.ReservationHash) ||
            !FileManagementService.Hex(binding.ReceiptHash) || !FileManagementService.Hex(binding.ArchiveSourceProofHash))
            throw new ArgumentException("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_SOURCE_INVALID");
        ContainerRecycleArchiveRetirementValidation.Command(binding.Command);
        return Mac(ContainerRecycleArchiveWorkflowValidation.Serialize(binding), "whaledeck-container-recycle-archive-retirement-business-source-v1");
    }
}
