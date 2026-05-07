using System.Text.Json;
using EventsConsumer.Data;
using EventsConsumer.Data.Entities;
using EventsConsumer.Messaging.Contracts;
using EventsConsumer.Messaging.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EventsConsumer.Messaging;

public sealed class RabbitMqConsumerHostedService(
    IServiceScopeFactory scopeFactory,
    IConsumerSettings failureSettings,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqConsumerHostedService> logger) : BackgroundService
{
    private const string ProductsExchange = "stub.products.exchange";
    private const string ProductCategoriesExchange = "stub.product-categories.exchange";

    private const string ProductsQueue = "merchantaggregates.products.queue";
    private const string ProductCategoriesQueue = "merchantaggregates.product-categories.queue";

    private readonly RabbitMqOptions _options = options.Value;

    private IConnection? _connection;
    private IModel? _channel;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        InitializeRabbit();
        RegisterQueueConsumer(ProductsQueue, stoppingToken);
        RegisterQueueConsumer(ProductCategoriesQueue, stoppingToken);

        logger.LogInformation("RabbitMQ consumer started");
        return Task.CompletedTask;
    }

    private void InitializeRabbit()
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            DispatchConsumersAsync = true
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.BasicQos(prefetchSize: 0, prefetchCount: 20, global: false);

        _channel.ExchangeDeclare(ProductsExchange, ExchangeType.Direct, durable: true, autoDelete: false);
        _channel.ExchangeDeclare(ProductCategoriesExchange, ExchangeType.Direct, durable: true, autoDelete: false);

        _channel.QueueDeclare(ProductsQueue, durable: true, exclusive: false, autoDelete: false);
        _channel.QueueDeclare(ProductCategoriesQueue, durable: true, exclusive: false, autoDelete: false);

        _channel.QueueBind(ProductsQueue, ProductsExchange, "product.created");
        _channel.QueueBind(ProductsQueue, ProductsExchange, "product.updated");

        _channel.QueueBind(ProductCategoriesQueue, ProductCategoriesExchange, "product-category.created");
        _channel.QueueBind(ProductCategoriesQueue, ProductCategoriesExchange, "product-category.updated");
    }

    private void RegisterQueueConsumer(string queueName, CancellationToken stoppingToken)
    {
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (_, args) => await HandleMessageAsync(args, stoppingToken);

        _channel!.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);
    }

    private async Task HandleMessageAsync(BasicDeliverEventArgs args, CancellationToken stoppingToken)
    {
        var body = args.Body.ToArray();
        var messageId = args.BasicProperties?.MessageId;
        if (string.IsNullOrWhiteSpace(messageId))
        {
            throw new InvalidOperationException("Incoming message does not contain MessageId");
        }

        var receivedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MerchantAggregatesDbContext>();

            if (args.Exchange == ProductsExchange)
            {
                var alreadyProcessed = await dbContext.ProductEvents
                    .AnyAsync(x => x.MessageId == messageId, stoppingToken);

                if (alreadyProcessed)
                {
                    _channel!.BasicAck(args.DeliveryTag, multiple: false);
                    return;
                }

                var message = JsonSerializer.Deserialize<ProductMessage>(body)
                              ?? throw new JsonException("Product message cannot be null");

                dbContext.ProductEvents.Add(new ProductEvent
                {
                    MessageId = messageId,
                    ProductId = message.ProductId,
                    MerchantId = message.MerchantId,
                    ProductCategoryId = message.ProductCategoryId,
                    SortOrder = message.SortOrder,
                    Name = message.Name,
                    Price = message.Price,
                    Action = message.Action,
                    OccurredAtUtc = message.OccurredAtUtc,
                    ReceivedAtUtc = receivedAtUtc
                });
            }
            else if (args.Exchange == ProductCategoriesExchange)
            {
                var alreadyProcessed = await dbContext.MerchantCategoryEvents
                    .AnyAsync(x => x.MessageId == messageId, stoppingToken);

                if (alreadyProcessed)
                {
                    _channel!.BasicAck(args.DeliveryTag, multiple: false);
                    return;
                }

                var message = JsonSerializer.Deserialize<MerchantCategoryMessage>(body)
                              ?? throw new JsonException("Category message cannot be null");

                dbContext.MerchantCategoryEvents.Add(new MerchantCategoryEvent
                {
                    MessageId = messageId,
                    MerchantCategoryId = message.MerchantCategoryId,
                    MerchantId = message.MerchantId,
                    Name = message.Name,
                    Action = message.Action,
                    OccurredAtUtc = message.OccurredAtUtc,
                    ReceivedAtUtc = receivedAtUtc
                });
            }
            else
            {
                logger.LogWarning("Skip message from unknown exchange {Exchange}", args.Exchange);
                _channel!.BasicAck(args.DeliveryTag, multiple: false);
                return;
            }

            if (ShouldFail(failureSettings.FailBeforeSavePercent))
            {
                throw new InvalidOperationException("Simulated consumer failure before database save");
            }

            await dbContext.SaveChangesAsync(stoppingToken);

            if (ShouldFail(failureSettings.FailAfterSaveBeforeAckPercent))
            {
                throw new InvalidOperationException("Simulated consumer failure after database save and before ACK");
            }

            _channel!.BasicAck(args.DeliveryTag, multiple: false);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Invalid message payload. exchange={Exchange}, routingKey={RoutingKey}", args.Exchange, args.RoutingKey);
            _channel!.BasicNack(args.DeliveryTag, multiple: false, requeue: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist message. exchange={Exchange}, routingKey={RoutingKey}", args.Exchange, args.RoutingKey);
            _channel!.BasicNack(args.DeliveryTag, multiple: false, requeue: true);
        }
    }


    private static bool ShouldFail(int percent) =>
        percent > 0 && Random.Shared.Next(100) < percent;

    public override void Dispose()
    {
        try
        {
            _channel?.Dispose();
            _connection?.Dispose();
        }
        finally
        {
            base.Dispose();
        }
    }
}

