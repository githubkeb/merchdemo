namespace EventsPublisher.Messaging.Contracts;

public sealed record ProductMessage(
    int ProductId,
    int MerchantId,
    int? ProductCategoryId,
    string Name,
    decimal Price,
    string Action,
    DateTimeOffset OccurredAtUtc)
{
    public Guid Id { get; } = Guid.CreateVersion7();
}

public sealed record MerchantCategoryMessage(
    int MerchantCategoryId,
    int MerchantId,
    string Name,
    string Action,
    DateTimeOffset OccurredAtUtc)
{
    public Guid Id { get; } = Guid.CreateVersion7();
}

