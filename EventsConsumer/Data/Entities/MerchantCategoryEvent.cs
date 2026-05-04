namespace EventsConsumer.Data.Entities;

public sealed class MerchantCategoryEvent
{
    public string MessageId { get; set; } = string.Empty;
    public int MerchantCategoryId { get; set; }
    public int MerchantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
}

