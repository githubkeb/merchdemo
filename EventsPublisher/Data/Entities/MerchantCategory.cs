namespace EventsPublisher.Data.Entities;

public sealed class MerchantCategory
{
    public int Id { get; set; }
    public int MerchantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public Merchant Merchant { get; set; } = null!;
    public ICollection<Product> Products { get; set; } = new List<Product>();
}

