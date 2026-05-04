namespace EventsPublisher.Messaging.Publishing;

public interface IRabbitPublisher
{
    Task PublishAsync<T>(string exchangeName, string routingKey, T message, CancellationToken cancellationToken);
}

