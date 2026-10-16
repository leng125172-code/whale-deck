using WhaleDeck.Application.Models;

namespace WhaleDeck.Application.Abstractions;

public sealed record TerminalInput(string Kind, byte[] Data, uint Columns = 0, uint Rows = 0);
public sealed record TerminalOutput(string Kind, byte[] Data, string Code, int ExitCode);
public interface IHostTerminalConnection : IAsyncDisposable
{
    Task WriteAsync(TerminalInput frame, CancellationToken cancellationToken);
    Task<TerminalOutput?> ReadAsync(CancellationToken cancellationToken);
}
public interface IHostTerminalGateway
{
    Task<IHostTerminalConnection> OpenHostAsync(Guid id, TerminalActor actor, CancellationToken cancellationToken);
}
