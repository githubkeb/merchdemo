namespace EventsConsumer.Messaging.Contracts;

public sealed record ProductMessage(
    int ProductId,
    int MerchantId,
    int? ProductCategoryId,
    string Name,
    decimal Price,
    string Action,
    DateTimeOffset OccurredAtUtc);

public sealed record MerchantCategoryMessage(
    int MerchantCategoryId,
    int MerchantId,
    string Name,
    string Action,
    DateTimeOffset OccurredAtUtc);

