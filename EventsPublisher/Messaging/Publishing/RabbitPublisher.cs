using System.Text;
using System.Text.Json;
using EventsPublisher.Messaging.Options;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace EventsPublisher.Messaging.Publishing;

public sealed class RabbitPublisher : IRabbitPublisher, IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitPublisher> _logger;
    private readonly object _sync = new();

    private IConnection? _connection;
    private IModel? _channel;

    public RabbitPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task PublishAsync<T>(string exchangeName, string routingKey, T message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        EnsureChannel();

        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);

        var properties = _channel!.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = "application/json";
        properties.MessageId = ResolveMessageId(message);

        _channel.ExchangeDeclare(exchangeName, ExchangeType.Direct, durable: true, autoDelete: false);
        _channel.BasicPublish(exchangeName, routingKey, basicProperties: properties, body: body);

        _logger.LogInformation(
            "Published message to exchange {ExchangeName} with routing key {RoutingKey}",
            exchangeName,
            routingKey);

        return Task.CompletedTask;
    }

    private static string ResolveMessageId<T>(T message)
    {
        var idProperty = typeof(T).GetProperty("Id");
        if (idProperty?.GetValue(message) is Guid messageId)
        {
            return messageId.ToString();
        }

        return Guid.CreateVersion7().ToString();
    }

    private void EnsureChannel()
    {
        lock (_sync)
        {
            if (_channel is { IsOpen: true })
            {
                return;
            }

            DisposeChannelOnly();

            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost
            };

            _connection ??= factory.CreateConnection();
            _channel = _connection.CreateModel();
        }
    }

    private void DisposeChannelOnly()
    {
        if (_channel is null)
        {
            return;
        }

        try
        {
            _channel.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while disposing RabbitMQ channel");
        }
        finally
        {
            _channel = null;
        }
    }

    public void Dispose()
    {
        DisposeChannelOnly();

        if (_connection is null)
        {
            return;
        }

        try
        {
            _connection.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while disposing RabbitMQ connection");
        }
        finally
        {
            _connection = null;
        }
    }
}

