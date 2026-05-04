namespace Aggregator.Models;

public sealed class ProductAggregate
{
    public int Id { get; set; }
    public int MerchantId { get; set; }
    public int? ProductCategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string LastAction { get; set; } = string.Empty;
    public DateTimeOffset LastOccurredAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

