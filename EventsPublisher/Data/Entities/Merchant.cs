namespace EventsPublisher.Data.Entities;

public sealed class Merchant
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<MerchantCategory> Categories { get; set; } = new List<MerchantCategory>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
}

