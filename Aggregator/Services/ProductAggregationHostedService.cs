using Aggregator.Models;
using Aggregator.Options;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Aggregator.Services;

public sealed class ProductAggregationHostedService(
    IOptions<AggregatorOptions> options,
    IConfiguration configuration,
    IAggregatorSettings aggregatorSettings,
    ILogger<ProductAggregationHostedService> logger) : BackgroundService
{
    private const string CheckpointId = "products";

    private readonly AggregatorOptions _options = options.Value;
    private readonly Random _random = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Default is required for ProductAggregationHostedService");
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
                logger.LogError(ex, "Product aggregation batch failed");
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

        foreach (var productEvent in events)
        {
            await ApplyEventAsync(connection, transaction, productEvent, ct);
        }

        var last = events[^1];
        checkpoint.LastOccurredAtUtc = last.OccurredAtUtc;
        checkpoint.LastMessageId = last.MessageId;
        checkpoint.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await SaveCheckpointAsync(connection, transaction, checkpoint, ct);

        await transaction.CommitAsync(ct);
        logger.LogInformation("Aggregated {Count} product events", events.Count);

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

    private async Task<List<ProductEventRow>> LoadNextBatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AggregationCheckpoint checkpoint,
        CancellationToken ct)
    {
        var events = new List<ProductEventRow>();

        const string sql =
            """
            SELECT
                "MessageId",
                "ProductId",
                "MerchantId",
                "ProductCategoryId",
                "SortOrder",
                "Name",
                "Price",
                "Action",
                "OccurredAtUtc",
                "ReceivedAtUtc"
            FROM "ProductEvents"
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
            events.Add(new ProductEventRow
            {
                MessageId = reader.GetString(0),
                ProductId = reader.GetInt32(1),
                MerchantId = reader.GetInt32(2),
                ProductCategoryId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                SortOrder = reader.GetInt32(4),
                Name = reader.GetString(5),
                Price = reader.GetDecimal(6),
                Action = reader.GetString(7),
                OccurredAtUtc = reader.GetFieldValue<DateTimeOffset>(8),
                ReceivedAtUtc = reader.GetFieldValue<DateTimeOffset>(9)
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
        ProductEventRow productEvent,
        CancellationToken ct)
    {
        await UpsertMerchantAsync(connection, transaction, productEvent.MerchantId, ct);

        if (string.Equals(productEvent.Action, "deleted", StringComparison.OrdinalIgnoreCase))
        {
            const string deleteSql = "DELETE FROM \"AggregateProducts\" WHERE \"Id\" = @id;";
            await using var deleteCommand = new NpgsqlCommand(deleteSql, connection, transaction);
            deleteCommand.Parameters.AddWithValue("id", productEvent.ProductId);
            await deleteCommand.ExecuteNonQueryAsync(ct);
            return;
        }

        var resolvedCategoryId = productEvent.ProductCategoryId;
        if (resolvedCategoryId.HasValue && !await CategoryExistsAsync(connection, transaction, resolvedCategoryId.Value, ct))
        {
            resolvedCategoryId = null;
        }

        const string upsertSql =
            """
            INSERT INTO "AggregateProducts" (
                "Id", "MerchantId", "ProductCategoryId", "SortOrder", "Name", "Price", "LastAction", "LastOccurredAtUtc", "UpdatedAtUtc")
            VALUES (@id, @merchantId, @productCategoryId, @sortOrder, @name, @price, @lastAction, @lastOccurredAtUtc, @updatedAtUtc)
            ON CONFLICT ("Id") DO UPDATE SET
                "MerchantId" = EXCLUDED."MerchantId",
                "ProductCategoryId" = EXCLUDED."ProductCategoryId",
                "SortOrder" = EXCLUDED."SortOrder",
                "Name" = EXCLUDED."Name",
                "Price" = EXCLUDED."Price",
                "LastAction" = EXCLUDED."LastAction",
                "LastOccurredAtUtc" = EXCLUDED."LastOccurredAtUtc",
                "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc";
            """;

        await using var command = new NpgsqlCommand(upsertSql, connection, transaction);
        command.Parameters.AddWithValue("id", productEvent.ProductId);
        command.Parameters.AddWithValue("merchantId", productEvent.MerchantId);
        command.Parameters.AddWithValue("productCategoryId", (object?)resolvedCategoryId ?? DBNull.Value);
        command.Parameters.AddWithValue("sortOrder", productEvent.SortOrder);
        command.Parameters.AddWithValue("name", productEvent.Name);
        command.Parameters.AddWithValue("price", productEvent.Price);
        command.Parameters.AddWithValue("lastAction", productEvent.Action);
        command.Parameters.AddWithValue("lastOccurredAtUtc", productEvent.OccurredAtUtc);
        command.Parameters.AddWithValue("updatedAtUtc", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> CategoryExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int categoryId,
        CancellationToken ct)
    {
        const string sql = "SELECT EXISTS (SELECT 1 FROM \"AggregateCategories\" WHERE \"Id\" = @id);";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", categoryId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(ct));
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

