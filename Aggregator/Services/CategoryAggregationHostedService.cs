using Aggregator.Models;
using Aggregator.Options;
using Marten;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Aggregator.Services;

public sealed class CategoryAggregationHostedService(
    IDocumentStore store,
    IOptions<AggregatorOptions> options,
    IConfiguration configuration,
    IAggregatorSettings aggregatorSettings,
    ILogger<CategoryAggregationHostedService> logger) : BackgroundService
{
    private const string CheckpointId = "categories";

    private readonly AggregatorOptions _options = options.Value;
    private readonly Random _random = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Default is required for CategoryAggregationHostedService");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var hasMore = await AggregateBatchAsync(connectionString, stoppingToken);
                if (!hasMore)
                {
                    await Task.Delay(_options.PollIntervalMs, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Category aggregation batch failed");
                await Task.Delay(_options.PollIntervalMs, stoppingToken);
            }
        }
    }

    private async Task<bool> AggregateBatchAsync(string connectionString, CancellationToken ct)
    {
        await using var session = store.LightweightSession();
        var checkpoint = await session.LoadAsync<AggregationCheckpoint>(CheckpointId, ct)
                         ?? new AggregationCheckpoint { Id = CheckpointId };

        var events = await LoadNextBatchAsync(connectionString, checkpoint, ct);
        if (events.Count == 0)
        {
            return false;
        }

        // Check if we should fail
        if (ShouldFail(aggregatorSettings.FailDuringAggregationPercent))
        {
            throw new InvalidOperationException("Simulated aggregation failure");
        }

        foreach (var categoryEvent in events)
        {
            await ApplyEventAsync(session, categoryEvent, ct);
        }

        var last = events[^1];
        checkpoint.LastOccurredAtUtc = last.OccurredAtUtc;
        checkpoint.LastMessageId = last.MessageId;
        checkpoint.UpdatedAtUtc = DateTimeOffset.UtcNow;
        session.Store(checkpoint);

        await session.SaveChangesAsync(ct);
        logger.LogInformation("Aggregated {Count} category events", events.Count);

        return events.Count >= _options.BatchSize;
    }

    private bool ShouldFail(int failurePercent)
    {
        if (failurePercent <= 0)
            return false;
        if (failurePercent >= 100)
            return true;
        return _random.Next(0, 100) < failurePercent;
    }

    private async Task<List<CategoryEventRow>> LoadNextBatchAsync(
        string connectionString,
        AggregationCheckpoint checkpoint,
        CancellationToken ct)
    {
        var events = new List<CategoryEventRow>();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        const string sql =
            """
            SELECT
                "MessageId",
                "MerchantCategoryId",
                "MerchantId",
                "Name",
                "Action",
                "OccurredAtUtc",
                "ReceivedAtUtc"
            FROM "MerchantCategoryEvents"
            WHERE
                "OccurredAtUtc" > @lastOccurredAtUtc
                OR ("OccurredAtUtc" = @lastOccurredAtUtc AND "MessageId" > @lastMessageId)
            ORDER BY "OccurredAtUtc", "MessageId"
            LIMIT @batchSize;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("lastOccurredAtUtc", checkpoint.LastOccurredAtUtc);
        command.Parameters.AddWithValue("lastMessageId", checkpoint.LastMessageId);
        command.Parameters.AddWithValue("batchSize", Math.Max(1, _options.BatchSize));

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            events.Add(new CategoryEventRow
            {
                MessageId = reader.GetString(0),
                MerchantCategoryId = reader.GetInt32(1),
                MerchantId = reader.GetInt32(2),
                Name = reader.GetString(3),
                Action = reader.GetString(4),
                OccurredAtUtc = reader.GetFieldValue<DateTimeOffset>(5),
                ReceivedAtUtc = reader.GetFieldValue<DateTimeOffset>(6)
            });
        }

        return events;
    }

    private static async Task ApplyEventAsync(IDocumentSession session, CategoryEventRow categoryEvent, CancellationToken ct)
    {
        if (string.Equals(categoryEvent.Action, "deleted", StringComparison.OrdinalIgnoreCase))
        {
            session.Delete<CategoryAggregate>(categoryEvent.MerchantCategoryId);
            return;
        }

        var aggregate = await session.LoadAsync<CategoryAggregate>(categoryEvent.MerchantCategoryId, ct)
                        ?? new CategoryAggregate { Id = categoryEvent.MerchantCategoryId };

        aggregate.MerchantId = categoryEvent.MerchantId;
        aggregate.Name = categoryEvent.Name;
        aggregate.LastAction = categoryEvent.Action;
        aggregate.LastOccurredAtUtc = categoryEvent.OccurredAtUtc;
        aggregate.UpdatedAtUtc = DateTimeOffset.UtcNow;

        session.Store(aggregate);
    }
}


