using System.Security.Cryptography;
using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed partial class FileTransferRunner
{
    private static bool ContainerWriteAction(string action)
    {
        if (action is "container-write:target" or "container-write:stage" or "container-write:publish" or "container-write:inspect" or "container-write:discard") return true;
        if (action.StartsWith("container-write:", StringComparison.Ordinal)) throw new ArgumentException("CONTAINER_UPLOAD_ACTION_INVALID");
        return false;
    }

    public async Task<JsonDocument> ContainerWriteOnceAsync(string action, string input, CancellationToken cancellationToken)
    {
        if (action is not ("target" or "publish" or "inspect" or "discard")) throw new ArgumentException("CONTAINER_UPLOAD_ACTION_INVALID");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(action == "target" ? 30 : 650));
        using var driver = await StartAsync("container-write:" + action, input, lifetime.Token);
        var response = await ReadJsonAsync(driver, lifetime.Token);
        try { await FinishContainerWriteAsync(driver, lifetime.Token); return response; }
        catch { response.Dispose(); throw; }
    }

    public async Task<ContainerUploadReceipt> ContainerStageAsync(string input, ContainerUploadHeader header,
        IAsyncStreamReader<ContainerUploadFrame> frames, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(330));
        using var driver = await StartAsync("container-write:stage", input, lifetime.Token);
        var writing = Write(); var reading = ReadJsonAsync(driver, lifetime.Token);
        try
        {
            var first = await Task.WhenAny(writing, reading);
            try { await first; }
            catch (IOException) when (first == writing)
            {
                using var rejected = await reading.WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token);
                throw new InvalidOperationException("CONTAINER_UPLOAD_INPUT_INTERRUPTED");
            }
            await writing.WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token);
            using var response = await reading;
            await FinishContainerWriteAsync(driver, lifetime.Token);
            return ContainerUploadPolicy.Receipt(response.RootElement, new()
            {
                ContainerId = header.Location.ContainerId, InstanceId = header.Location.InstanceId,
                OperationId = header.OperationId, Owner = header.Owner, ExpectedTargetProof = header.ExpectedTargetProof
            }, header);
        }
        finally
        {
            lifetime.Cancel();
            try { await writing; } catch (Exception) { }
            FileTransferPipe.AbortInput(driver.Process.StandardInput);
            try { if (reading.IsCompletedSuccessfully) reading.Result.Dispose(); else await reading; } catch (Exception) { }
        }
        async Task Write()
        {
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long offset = 0; var committed = false;
            while (await frames.MoveNext(lifetime.Token))
            {
                var frame = frames.Current;
                var line = ContainerUploadPolicy.Input(frame, ref offset, header.SizeBytes);
                if (frame.Commit)
                {
                    using var end = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    end.CancelAfter(TimeSpan.FromSeconds(5));
                    if (await frames.MoveNext(end.Token)) throw new ArgumentException("CONTAINER_UPLOAD_COMMIT_TRAILING_DATA");
                    if (Convert.ToHexString(digest.GetHashAndReset()) != header.Sha256) throw new ArgumentException("CONTAINER_UPLOAD_CHECKSUM_MISMATCH");
                }
                else digest.AppendData(frame.Data.Span);
                await driver.Process.StandardInput.WriteLineAsync(line.AsMemory(), lifetime.Token).WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token);
                await driver.Process.StandardInput.FlushAsync(lifetime.Token);
                if (frame.Commit) { committed = true; break; }
            }
            if (!committed) throw new ArgumentException("CONTAINER_UPLOAD_COMMIT_REQUIRED");
            driver.Process.StandardInput.Close();
        }
    }

    private static async Task FinishContainerWriteAsync(Driver driver, CancellationToken cancellationToken)
    {
        if (!driver.IsContainerWrite || driver.BufferCount != 0) throw new InvalidOperationException("CONTAINER_UPLOAD_TRAILING_OUTPUT");
        var tail = new byte[1];
        if (await driver.Process.StandardOutput.BaseStream.ReadAsync(tail, cancellationToken) != 0)
            throw new InvalidOperationException("CONTAINER_UPLOAD_TRAILING_OUTPUT");
        await FinishAsync(driver, cancellationToken);
    }
}
