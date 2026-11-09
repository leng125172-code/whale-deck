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
        bool clearEnvironment = false,
        IReadOnlySet<int>? acceptedExitCodes = null)
    {
        var start = CreateStart(executable, arguments, standardInput is not null, workingDirectory);
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
        if (result.ExitCode != 0 && acceptedExitCodes?.Contains(result.ExitCode) != true) throw new InvalidOperationException($"{errorCode}: approved command failed with exit code {result.ExitCode}.");
        return result;
    }

    internal static async Task<(byte[] Content, string Error, int ExitCode)> RunBinaryAsync(string executable,
        IReadOnlyCollection<string> arguments, string standardInput, int size, TimeSpan timeout, CancellationToken token)
    {
        using var process = Process.Start(CreateStart(executable, arguments, true, null)) ?? throw new InvalidOperationException();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(timeout);
        try
        {
            var errors = ReadBoundedAsync(process.StandardError, lifetime.Token);
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), lifetime.Token);
            process.StandardInput.Close();
            var content = new byte[size];
            var output = process.StandardOutput.BaseStream;
            var count = await output.ReadAtLeastAsync(content, size, false, lifetime.Token);
            if (count == size && await output.ReadAsync(new byte[1], lifetime.Token) != 0)
                throw new InvalidDataException();
            await process.WaitForExitAsync(lifetime.Token);
            var error = await errors;
            if (process.ExitCode == 0 && count != size) throw new InvalidDataException();
            return (process.ExitCode == 0 ? content : [], error, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        }
    }

    internal static async Task<ProcessResult> RunWithInputAsync(string executable, IReadOnlyCollection<string> arguments,
        Func<Stream, CancellationToken, Task> pump, TimeSpan timeout, CancellationToken token)
    {
        using var process = Process.Start(CreateStart(executable, arguments, true, null)) ?? throw new InvalidOperationException();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(timeout);
        using var input = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var output = ReadBoundedAsync(process.StandardOutput, lifetime.Token);
        var errors = ReadBoundedAsync(process.StandardError, lifetime.Token);
        var exited = process.WaitForExitAsync(lifetime.Token);
        var writing = WriteInputAsync();
        try
        {
            if (await Task.WhenAny(writing, exited) == exited)
            {
                await exited;
                // A busy helper can reject before the client sends its next frame.
                if (process.ExitCode != 0) input.Cancel();
            }
            try { await writing; }
            catch (Exception error) when (error is IOException or OperationCanceledException)
            {
                await exited;
                if (process.ExitCode == 0) throw;
            }
            await exited;
            return new ProcessResult(process.ExitCode, await output, await errors);
        }
        finally
        {
            input.Cancel();
            lifetime.Cancel();
            if (!process.HasExited)
                // The root transient unit owns its own deadline. Close stdin
                // and keep the original RPC error if sudo disallows signals.
                try { process.Kill(); } catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            // Observe all tasks on malformed or cancelled streams.
            try { await Task.WhenAll(writing, exited, output, errors); } catch (Exception) { }
        }

        async Task WriteInputAsync()
        {
            var stream = process.StandardInput.BaseStream;
            try { await pump(stream, input.Token); }
            finally { await stream.DisposeAsync(); }
        }
    }

    private static ProcessStartInfo CreateStart(string executable, IReadOnlyCollection<string> arguments,
        bool standardInput, string? workingDirectory)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? string.Empty
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
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
