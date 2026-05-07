namespace EventsPublisher.Data.Entities;

public sealed class Product
{
    public int Id { get; set; }
    public int MerchantId { get; set; }
    public int? MerchantCategoryId { get; set; }
    public int SortOrder { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public Merchant Merchant { get; set; } = null!;
    public MerchantCategory? MerchantCategory { get; set; }
}

