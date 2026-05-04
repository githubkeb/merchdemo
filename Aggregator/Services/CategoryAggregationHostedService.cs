using Aggregator.Models;
using Aggregator.Options;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Aggregator.Services;

public sealed class CategoryAggregationHostedService(
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
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        var checkpoint = await LoadCheckpointAsync(connection, transaction, ct)
                         ?? new AggregationCheckpoint { Id = CheckpointId };

        var events = await LoadNextBatchAsync(connection, transaction, checkpoint, ct);
        if (events.Count == 0)
        {
            await transaction.CommitAsync(ct);
            return false;
        }

        // Check if we should fail
        if (ShouldFail(aggregatorSettings.FailDuringAggregationPercent))
        {
            throw new InvalidOperationException("Simulated aggregation failure");
        }

        foreach (var categoryEvent in events)
        {
            await ApplyEventAsync(connection, transaction, categoryEvent, ct);
        }

        var last = events[^1];
        checkpoint.LastOccurredAtUtc = last.OccurredAtUtc;
        checkpoint.LastMessageId = last.MessageId;
        checkpoint.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await SaveCheckpointAsync(connection, transaction, checkpoint, ct);

        await transaction.CommitAsync(ct);
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
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AggregationCheckpoint checkpoint,
        CancellationToken ct)
    {
        var events = new List<CategoryEventRow>();

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

        await using var command = new NpgsqlCommand(sql, connection, transaction);
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

    private static async Task<AggregationCheckpoint?> LoadCheckpointAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken ct)
    {
        const string sql =
            """
            SELECT "Id", "LastOccurredAtUtc", "LastMessageId", "UpdatedAtUtc"
            FROM "AggregationCheckpoints"
            WHERE "Id" = @id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", CheckpointId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new AggregationCheckpoint
        {
            Id = reader.GetString(0),
            LastOccurredAtUtc = reader.GetFieldValue<DateTimeOffset>(1),
            LastMessageId = reader.GetString(2),
            UpdatedAtUtc = reader.GetFieldValue<DateTimeOffset>(3)
        };
    }

    private static async Task SaveCheckpointAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AggregationCheckpoint checkpoint,
        CancellationToken ct)
    {
        const string sql =
            """
            INSERT INTO "AggregationCheckpoints" ("Id", "LastOccurredAtUtc", "LastMessageId", "UpdatedAtUtc")
            VALUES (@id, @lastOccurredAtUtc, @lastMessageId, @updatedAtUtc)
            ON CONFLICT ("Id") DO UPDATE SET
                "LastOccurredAtUtc" = EXCLUDED."LastOccurredAtUtc",
                "LastMessageId" = EXCLUDED."LastMessageId",
                "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc";
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", checkpoint.Id);
        command.Parameters.AddWithValue("lastOccurredAtUtc", checkpoint.LastOccurredAtUtc);
        command.Parameters.AddWithValue("lastMessageId", checkpoint.LastMessageId);
        command.Parameters.AddWithValue("updatedAtUtc", checkpoint.UpdatedAtUtc);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task ApplyEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CategoryEventRow categoryEvent,
        CancellationToken ct)
    {
        await UpsertMerchantAsync(connection, transaction, categoryEvent.MerchantId, ct);

        if (string.Equals(categoryEvent.Action, "deleted", StringComparison.OrdinalIgnoreCase))
        {
            const string deleteSql = "DELETE FROM \"AggregateCategories\" WHERE \"Id\" = @id;";
            await using var deleteCommand = new NpgsqlCommand(deleteSql, connection, transaction);
            deleteCommand.Parameters.AddWithValue("id", categoryEvent.MerchantCategoryId);
            await deleteCommand.ExecuteNonQueryAsync(ct);
            return;
        }

        const string upsertSql =
            """
            INSERT INTO "AggregateCategories" (
                "Id", "MerchantId", "Name", "LastAction", "LastOccurredAtUtc", "UpdatedAtUtc")
            VALUES (@id, @merchantId, @name, @lastAction, @lastOccurredAtUtc, @updatedAtUtc)
            ON CONFLICT ("Id") DO UPDATE SET
                "MerchantId" = EXCLUDED."MerchantId",
                "Name" = EXCLUDED."Name",
                "LastAction" = EXCLUDED."LastAction",
                "LastOccurredAtUtc" = EXCLUDED."LastOccurredAtUtc",
                "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc";
            """;

        await using var command = new NpgsqlCommand(upsertSql, connection, transaction);
        command.Parameters.AddWithValue("id", categoryEvent.MerchantCategoryId);
        command.Parameters.AddWithValue("merchantId", categoryEvent.MerchantId);
        command.Parameters.AddWithValue("name", categoryEvent.Name);
        command.Parameters.AddWithValue("lastAction", categoryEvent.Action);
        command.Parameters.AddWithValue("lastOccurredAtUtc", categoryEvent.OccurredAtUtc);
        command.Parameters.AddWithValue("updatedAtUtc", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task UpsertMerchantAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int merchantId,
        CancellationToken ct)
    {
        const string sql =
            """
            INSERT INTO "AggregateMerchants" ("Id", "UpdatedAtUtc")
            VALUES (@id, @updatedAtUtc)
            ON CONFLICT ("Id") DO UPDATE SET
                "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc";
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", merchantId);
        command.Parameters.AddWithValue("updatedAtUtc", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(ct);
    }
}


