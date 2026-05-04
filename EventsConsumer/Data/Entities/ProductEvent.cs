namespace EventsConsumer.Data.Entities;

public sealed class ProductEvent
{
    public string MessageId { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public int MerchantId { get; set; }
    public int? ProductCategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
}

