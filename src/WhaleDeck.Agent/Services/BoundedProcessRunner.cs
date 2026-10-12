using System.Diagnostics;
using System.Text;

namespace WhaleDeck.Agent.Services;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public sealed class BoundedProcessRunner
{
    private const int MaximumCapturedCharacters = 64 * 1024;

    public async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyCollection<string> arguments,
        string? standardInput,
        TimeSpan timeout,
        string errorCode,
        CancellationToken cancellationToken,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        bool clearEnvironment = false)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? string.Empty
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (clearEnvironment) start.Environment.Clear();
        if (environment is not null)
        {
            foreach (var item in environment) start.Environment[item.Key] = item.Value;
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{errorCode}: process could not start.");
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), timeoutSource.Token);
            await process.StandardInput.FlushAsync(timeoutSource.Token);
            process.StandardInput.Close();
        }

        var outputTask = ReadBoundedAsync(process.StandardOutput, timeoutSource.Token);
        var errorTask = ReadBoundedAsync(process.StandardError, timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }

        var result = new ProcessResult(process.ExitCode, await outputTask, await errorTask);
        if (result.ExitCode != 0) throw new InvalidOperationException($"{errorCode}: approved command failed with exit code {result.ExitCode}.");
        return result;
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var output = new StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length < MaximumCapturedCharacters)
            {
                output.Append(buffer, 0, Math.Min(read, MaximumCapturedCharacters - output.Length));
            }
        }
        return output.ToString();
    }
}
