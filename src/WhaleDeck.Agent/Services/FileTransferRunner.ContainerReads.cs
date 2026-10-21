using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.Agent.Services;

public sealed partial class FileTransferRunner
{
    private static bool ContainerAction(string action)
    {
        if (action is "container:list" or "container:stat" or "container:download") return true;
        if (action.StartsWith("container:", StringComparison.Ordinal)) throw new ArgumentException("CONTAINER_FILES_ACTION_INVALID");
        return false;
    }

    public async Task<JsonDocument> ContainerOnceAsync(string action, string input, CancellationToken cancellationToken)
    {
        if (action is not ("list" or "stat")) throw new ArgumentException("CONTAINER_FILES_ACTION_INVALID");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(30));
        using var driver = await StartAsync("container:" + action, input, lifetime.Token);
        var response = await ReadJsonAsync(driver, lifetime.Token);
        try { await FinishContainerReadAsync(driver, lifetime.Token); return response; }
        catch { response.Dispose(); throw; }
    }

    public async Task ContainerDownloadAsync(string input, string name, string version, IServerStreamWriter<FileDownloadFrame> output, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(310));
        using var driver = await StartAsync("container:download", input, lifetime.Token);
        using var decoder = new ContainerFileDownloadDecoder(name, version);
        while (!decoder.Complete)
        {
            using var row = await ReadJsonAsync(driver, lifetime.Token);
            var frame = decoder.Read(row.RootElement);
            if (decoder.Complete)
            {
                // Do not acknowledge success before exact EOF and helper exit success.
                await FinishContainerReadAsync(driver, lifetime.Token);
            }
            await output.WriteAsync(frame, lifetime.Token).WaitAsync(TimeSpan.FromSeconds(10), lifetime.Token);
        }
    }

    private static async Task FinishContainerReadAsync(Driver driver, CancellationToken cancellationToken)
    {
        if (!driver.IsContainerRead || driver.BufferCount != 0) throw new InvalidOperationException("CONTAINER_FILES_TRAILING_OUTPUT");
        var tail = new byte[1];
        if (await driver.Process.StandardOutput.BaseStream.ReadAsync(tail, cancellationToken) != 0)
            throw new InvalidOperationException("CONTAINER_FILES_TRAILING_OUTPUT");
        await FinishAsync(driver, cancellationToken);
    }
}

/// <summary>Bounded decoding binds filename/version and hashes actual delivered bytes before end acknowledgement.</summary>
internal sealed class ContainerFileDownloadDecoder : IDisposable
{
    private readonly string _name;
    private readonly string _version;
    private readonly IncrementalHash _digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long _size = -1;
    private long _offset;
    public bool Complete { get; private set; }

    public ContainerFileDownloadDecoder(string name, string version)
    {
        ContainerFilePolicy.Cursor(name);
        if (name.Length == 0) throw new ArgumentException("CONTAINER_FILES_NAME_REQUIRED");
        FileTransferPolicy.Hash(version);
        _name = name; _version = version;
    }

    public FileDownloadFrame Read(JsonElement value)
    {
        if (Complete) throw Invalid();
        var kind = value.GetProperty("type").GetString();
        if (kind == "metadata" && _size < 0)
        {
            var entry = FileTransferPolicy.DownloadEntry(value, FileTransferPolicy.MaximumBytes);
            ContainerFilePolicy.Entry(entry);
            if (entry.Name != _name || entry.Version != _version) throw Invalid();
            _size = entry.SizeBytes;
            return new() { Kind = "metadata", Metadata = entry };
        }
        if (kind == "chunk" && _size >= 0 && value.GetProperty("offset").GetInt64() == _offset)
        {
            var bytes = value.GetProperty("data").GetBytesFromBase64();
            if (bytes.Length is < 1 or > 32768 || _offset + bytes.Length > _size) throw Invalid();
            var frame = new FileDownloadFrame { Kind = "chunk", Offset = _offset, Data = ByteString.CopyFrom(bytes) };
            _digest.AppendData(bytes); _offset += bytes.Length;
            return frame;
        }
        if (kind == "end" && _size >= 0 && _offset == _size && value.GetProperty("sizeBytes").GetInt64() == _size)
        {
            var hash = value.GetProperty("sha256").GetString() ?? string.Empty;
            FileTransferPolicy.Hash(hash);
            if (value.GetProperty("version").GetString() != _version || hash != Convert.ToHexString(_digest.GetHashAndReset())) throw Invalid();
            Complete = true;
            return new() { Kind = "end", SizeBytes = _size, Sha256 = hash, Version = _version };
        }
        throw Invalid();
    }

    private static InvalidOperationException Invalid() => new("CONTAINER_FILES_STREAM_INVALID");
    public void Dispose() => _digest.Dispose();
}
