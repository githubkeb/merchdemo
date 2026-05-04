namespace Aggregator.Models;

public sealed class CategoryAggregate
{
    public int Id { get; set; }
    public int MerchantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string LastAction { get; set; } = string.Empty;
    public DateTimeOffset LastOccurredAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

