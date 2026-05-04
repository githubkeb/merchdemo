namespace Aggregator.Models;

public sealed class AggregationCheckpoint
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset LastOccurredAtUtc { get; set; } = DateTimeOffset.MinValue;
    public string LastMessageId { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

