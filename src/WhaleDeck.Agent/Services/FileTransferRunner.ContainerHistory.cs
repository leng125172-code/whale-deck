using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed partial class FileTransferRunner
{
    private static bool ContainerHistoryAction(string action)
    {
        if (action is "history:list" or "history:source") return true;
        if (action.StartsWith("history:", StringComparison.Ordinal)) throw new ArgumentException("CONTAINER_HISTORY_REQUEST_INVALID");
        return false;
    }

    public async Task<JsonDocument> ContainerHistoryOnceAsync(string input, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); lifetime.CancelAfter(TimeSpan.FromSeconds(25));
        using var driver = await StartAsync("history:list", input, lifetime.Token);
        var response = await ReadJsonAsync(driver, lifetime.Token);
        try { await FinishContainerHistoryAsync(driver, lifetime.Token); return response; }
        catch { response.Dispose(); throw; }
    }

    public async Task ContainerHistorySourceAsync(string input, ContainerHistorySourceRequest request,
        IServerStreamWriter<ContainerHistorySourceFrame> output, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); lifetime.CancelAfter(TimeSpan.FromSeconds(300));
        using var driver = await StartAsync("history:source", input, lifetime.Token);
        using var decoder = new ContainerHistorySourceDecoder(request);
        while (!decoder.Complete)
        {
            using var row = await ReadJsonAsync(driver, lifetime.Token);
            var frame = decoder.Read(row.RootElement);
            if (decoder.Complete) await FinishContainerHistoryAsync(driver, lifetime.Token);
            await output.WriteAsync(frame, lifetime.Token).WaitAsync(TimeSpan.FromSeconds(10), lifetime.Token);
        }
    }

    private static async Task FinishContainerHistoryAsync(Driver driver, CancellationToken cancellationToken)
    {
        if (!driver.IsContainerHistory || driver.BufferCount != 0) throw ContainerHistoryPolicy.Invalid();
        var tail = new byte[1];
        if (await driver.Process.StandardOutput.BaseStream.ReadAsync(tail, cancellationToken) != 0) throw ContainerHistoryPolicy.Invalid();
        await FinishAsync(driver, cancellationToken);
    }
}

internal sealed class ContainerHistorySourceDecoder : IDisposable
{
    private readonly ContainerHistorySourceRequest _request;
    private readonly IncrementalHash _digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private ContainerHistoryEntry? _entry;
    private string _proof = "";
    private long _offset;
    public bool Complete { get; private set; }

    public ContainerHistorySourceDecoder(ContainerHistorySourceRequest request)
    {
        ContainerHistoryPolicy.Source(request); _request = request.Clone();
    }

    public ContainerHistorySourceFrame Read(JsonElement value)
    {
        if (Complete) throw ContainerHistoryPolicy.Invalid();
        var kind = ContainerHistoryPolicy.Text(value, "type");
        if (kind == "metadata" && _entry is null)
        {
            ContainerHistoryPolicy.Fields(value, "type", "entry", "targetProof");
            var entry = ContainerHistoryPolicy.Entry(value.GetProperty("entry"));
            var proof = ContainerHistoryPolicy.Text(value, "targetProof"); FileTransferPolicy.Hash(proof);
            if (entry.CaptureId != _request.CaptureId || entry.CaptureHash != _request.ExpectedCaptureHash || entry.ContainerId != _request.Location.ContainerId ||
                entry.Path != _request.Location.Path || entry.Availability != "Present") throw ContainerHistoryPolicy.Invalid();
            _entry = entry; _proof = proof;
            return new() { Kind = "metadata", Metadata = entry.Clone(), TargetProof = proof, CaptureHash = entry.CaptureHash };
        }
        if (kind == "chunk" && _entry is not null)
        {
            ContainerHistoryPolicy.Fields(value, "type", "offset", "data");
            if (value.GetProperty("offset").GetInt64() != _offset) throw ContainerHistoryPolicy.Invalid();
            var data = value.GetProperty("data").GetBytesFromBase64();
            if (data.Length is < 1 or > 32768 || _offset + data.Length > _entry.SizeBytes) throw ContainerHistoryPolicy.Invalid();
            var frame = new ContainerHistorySourceFrame { Kind = "chunk", Offset = _offset, Data = ByteString.CopyFrom(data) };
            _offset += data.Length; _digest.AppendData(data); return frame;
        }
        if (kind == "end" && _entry is not null && _offset == _entry.SizeBytes)
        {
            ContainerHistoryPolicy.Fields(value, "type", "sizeBytes", "sha256", "captureHash", "targetProof");
            var sha = ContainerHistoryPolicy.Text(value, "sha256");
            if (value.GetProperty("sizeBytes").GetInt64() != _offset || sha != _entry.Sha256 || sha != Convert.ToHexString(_digest.GetHashAndReset()) ||
                ContainerHistoryPolicy.Text(value, "captureHash") != _request.ExpectedCaptureHash || ContainerHistoryPolicy.Text(value, "targetProof") != _proof)
                throw ContainerHistoryPolicy.Invalid();
            Complete = true;
            return new() { Kind = "end", SizeBytes = _offset, Sha256 = sha, CaptureHash = _entry.CaptureHash, TargetProof = _proof };
        }
        throw ContainerHistoryPolicy.Invalid();
    }

    public void Dispose() => _digest.Dispose();
}
