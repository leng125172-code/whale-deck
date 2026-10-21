namespace WhaleDeck.Agent.Services;

/// <summary>Bounded replay of actual canonical batches, never interpolated or collected by RPC readers.</summary>
public sealed class HostMetricHistory
{
    public const int MaximumBatches = 120;
    public const int MaximumPoints = 65_536;
    private readonly Queue<HostMetricBatch> _batches = new();
    private readonly object _sync = new();
    private int _points;

    public void Append(HostMetricBatch batch)
    {
        if (batch.Metrics.Count > MaximumPoints)
            throw new InvalidOperationException("HOST_METRIC_BATCH_BUDGET_EXCEEDED");
        // Copy both the collection and its ownership: a caller cannot alter retained samples.
        var frozen = batch with { Metrics = Array.AsReadOnly(batch.Metrics.ToArray()) };
        lock (_sync)
        {
            while (_batches.Count > 0 && (_batches.Count >= MaximumBatches || _points + frozen.Metrics.Count > MaximumPoints))
                _points -= _batches.Dequeue().Metrics.Count;
            _batches.Enqueue(frozen);
            _points += frozen.Metrics.Count;
        }
    }

    public IReadOnlyCollection<HostMetricBatch> ReadAfter(DateTimeOffset? afterUtc)
    {
        lock (_sync)
            return _batches.Where(batch => afterUtc is null || batch.SampledAtUtc > afterUtc.Value).ToArray();
    }
}
