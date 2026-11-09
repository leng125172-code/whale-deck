using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

public interface IContainerRecycleArchiveRetirementSourceProtector
{
    string FingerprintRetirementSource(ContainerRecycleArchiveRetirementSourceBinding binding);
}
