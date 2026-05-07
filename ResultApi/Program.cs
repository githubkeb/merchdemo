using Aggregator.Models;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var replicaConnectionString = builder.Configuration.GetConnectionString("AggregatesReplica");
if (string.IsNullOrWhiteSpace(replicaConnectionString))
{
    throw new InvalidOperationException("ConnectionStrings:AggregatesReplica is required for ResultApi");
}

builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(replicaConnectionString).Build());

builder.Services.AddHttpClient("EventsPublisher", client =>
{
    var isDocker = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")) &&
                   Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development" &&
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RabbitMq__Host"));
    
    var baseUrl = isDocker
        ? "http://eventspublisher:8080"
        : "http://localhost:8082";
    client.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddHttpClient("EventsConsumer", client =>
{
    var isDocker = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")) &&
                   Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development" &&
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RabbitMq__Host"));

    var baseUrl = isDocker
        ? "http://eventsconsumer:8080"
        : "http://localhost:8083";
    client.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddHttpClient("Aggregator", client =>
{
    var isDocker = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")) &&
                   Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development" &&
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RabbitMq__Host"));

    var baseUrl = isDocker
        ? "http://aggregator:8080"
        : "http://localhost:8081";
    client.BaseAddress = new Uri(baseUrl);
});

var app = builder.Build();

app.UseStaticFiles();

app.MapGet("/", (IWebHostEnvironment env) => 
{
    var filePath = Path.Combine(env.ContentRootPath, "wwwroot", "index.html");
    return Results.File(filePath, "text/html");
});

// Proxy endpoints to EventsPublisher robot settings
app.MapGet("/api/robot-settings", async (IHttpClientFactory httpClientFactory) =>
{
    try
    {
        var client = httpClientFactory.CreateClient("EventsPublisher");
        var response = await client.GetAsync("/robot-settings");
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            return Results.Ok(JsonSerializer.Deserialize<object>(content));
        }
        return Results.StatusCode((int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapPut("/api/robot-settings", async (object settings, IHttpClientFactory httpClientFactory) =>
{
    try
    {
        var client = httpClientFactory.CreateClient("EventsPublisher");
        var json = JsonSerializer.Serialize(settings);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PutAsync("/robot-settings", content);
        
        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            return Results.Ok(JsonSerializer.Deserialize<object>(responseContent));
        }
        
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            return Results.BadRequest(JsonSerializer.Deserialize<object>(errorContent));
        }
        
        return Results.StatusCode((int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapGet("/api/consumer-failure-settings", async (IHttpClientFactory httpClientFactory) =>
{
    try
    {
        var client = httpClientFactory.CreateClient("EventsConsumer");
        var response = await client.GetAsync("/consumer-failure-settings");
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            return Results.Ok(JsonSerializer.Deserialize<object>(content));
        }

        return Results.StatusCode((int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapPut("/api/consumer-failure-settings", async (object settings, IHttpClientFactory httpClientFactory) =>
{
    try
    {
        var client = httpClientFactory.CreateClient("EventsConsumer");
        var json = JsonSerializer.Serialize(settings);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PutAsync("/consumer-failure-settings", content);

        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            return Results.Ok(JsonSerializer.Deserialize<object>(responseContent));
        }

        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            return Results.BadRequest(JsonSerializer.Deserialize<object>(errorContent));
        }

        return Results.StatusCode((int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapGet("/api/aggregator-settings", async (IHttpClientFactory httpClientFactory) =>
{
    try
    {
        var client = httpClientFactory.CreateClient("Aggregator");
        var response = await client.GetAsync("/aggregator-settings");
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            return Results.Ok(JsonSerializer.Deserialize<object>(content));
        }

        return Results.StatusCode((int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapPut("/api/aggregator-settings", async (object settings, IHttpClientFactory httpClientFactory) =>
{
    try
    {
        var client = httpClientFactory.CreateClient("Aggregator");
        var json = JsonSerializer.Serialize(settings);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PutAsync("/aggregator-settings", content);

        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            return Results.Ok(JsonSerializer.Deserialize<object>(responseContent));
        }

        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            return Results.BadRequest(JsonSerializer.Deserialize<object>(errorContent));
        }

        return Results.StatusCode((int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapGet("/api/state-counts", async (IHttpClientFactory httpClientFactory, NpgsqlDataSource replicaDataSource, CancellationToken ct) =>
{
    try
    {
        var publisherClient = httpClientFactory.CreateClient("EventsPublisher");

        var originalResponse = await publisherClient.GetAsync("/state-counts");

        if (!originalResponse.IsSuccessStatusCode)
        {
            return Results.StatusCode((int)originalResponse.StatusCode);
        }

        var originalJson = await originalResponse.Content.ReadAsStringAsync();
        await using var connection = await replicaDataSource.OpenConnectionAsync(ct);

        var products = await ExecuteCountAsync(connection, "SELECT COUNT(*) FROM \"AggregateProducts\";", ct);
        var categories = await ExecuteCountAsync(connection, "SELECT COUNT(*) FROM \"AggregateCategories\";", ct);
        var merchants = await ExecuteCountAsync(connection, "SELECT COUNT(*) FROM \"AggregateMerchants\";", ct);

        return Results.Ok(new
        {
            originalDb = JsonSerializer.Deserialize<object>(originalJson),
            aggregateDb = new
            {
                merchants,
                products,
                categories
            }
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapGet("/api/merchant/{merchantId:int}", async (
    int merchantId,
    int? page,
    int? pageSize,
    int? categoryId,
    NpgsqlDataSource replicaDataSource,
    CancellationToken ct) =>
{
    try
    {
        var pagination = NormalizePagination(page, pageSize);
        return await LoadMerchantAggregateAsync(replicaDataSource, merchantId, pagination.Page, pagination.PageSize, categoryId, ct);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapGet("/api/random-merchant", async (int? page, int? pageSize, int? categoryId, NpgsqlDataSource replicaDataSource, CancellationToken ct) =>
{
    try
    {
        var pagination = NormalizePagination(page, pageSize);
        await using var connection = await replicaDataSource.OpenConnectionAsync(ct);

        var merchantIds = new List<int>();
        await using (var merchantCommand = new NpgsqlCommand("SELECT \"Id\" FROM \"AggregateMerchants\" ORDER BY \"Id\";", connection))
        await using (var merchantReader = await merchantCommand.ExecuteReaderAsync(ct))
        {
            while (await merchantReader.ReadAsync(ct))
            {
                merchantIds.Add(merchantReader.GetInt32(0));
            }
        }

        if (merchantIds.Count == 0)
        {
            return Results.NotFound(new { message = "No merchants found in aggregates" });
        }

        var merchantId = merchantIds[Random.Shared.Next(merchantIds.Count)];
        return await LoadMerchantAggregateAsync(replicaDataSource, merchantId, pagination.Page, pagination.PageSize, categoryId, ct);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapPost("/api/robot-stop", async (IHttpClientFactory httpClientFactory) =>
{
    try
    {
        var client = httpClientFactory.CreateClient("EventsPublisher");
        var response = await client.PostAsync("/robot-stop", content: null);
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            return Results.Ok(JsonSerializer.Deserialize<object>(content));
        }

        return Results.StatusCode((int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.Run();

static async Task<int> ExecuteCountAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
{
    await using var command = new NpgsqlCommand(sql, connection);
    return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
}

static (int Page, int PageSize) NormalizePagination(int? page, int? pageSize)
{
    var normalizedPage = page.GetValueOrDefault(1);
    if (normalizedPage < 1)
    {
        normalizedPage = 1;
    }

    var normalizedPageSize = pageSize.GetValueOrDefault(10);
    normalizedPageSize = Math.Clamp(normalizedPageSize, 1, 100);

    return (normalizedPage, normalizedPageSize);
}

static async Task<IResult> LoadMerchantAggregateAsync(
    NpgsqlDataSource replicaDataSource,
    int merchantId,
    int page,
    int pageSize,
    int? categoryId,
    CancellationToken ct)
{
    await using var connection = await replicaDataSource.OpenConnectionAsync(ct);

    var exists = false;
    await using (var existsCommand = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM \"AggregateMerchants\" WHERE \"Id\" = @merchantId);", connection))
    {
        existsCommand.Parameters.AddWithValue("merchantId", merchantId);
        exists = Convert.ToBoolean(await existsCommand.ExecuteScalarAsync(ct));
    }

    if (!exists)
    {
        return Results.NotFound(new { message = $"Merchant {merchantId} not found in aggregates" });
    }

    var merchantCategories = new List<CategoryAggregate>();
    const string categoriesSql =
        """
        SELECT "Id", "MerchantId", "Name", "LastAction", "LastOccurredAtUtc", "UpdatedAtUtc"
        FROM "AggregateCategories"
        WHERE "MerchantId" = @merchantId
        ORDER BY "Id";
        """;
    await using (var categoriesCommand = new NpgsqlCommand(categoriesSql, connection))
    {
        categoriesCommand.Parameters.AddWithValue("merchantId", merchantId);
        await using var categoriesReader = await categoriesCommand.ExecuteReaderAsync(ct);
        while (await categoriesReader.ReadAsync(ct))
        {
            merchantCategories.Add(new CategoryAggregate
            {
                Id = categoriesReader.GetInt32(0),
                MerchantId = categoriesReader.GetInt32(1),
                Name = categoriesReader.GetString(2),
                LastAction = categoriesReader.GetString(3),
                LastOccurredAtUtc = categoriesReader.GetFieldValue<DateTimeOffset>(4),
                UpdatedAtUtc = categoriesReader.GetFieldValue<DateTimeOffset>(5)
            });
        }
    }

    var filteredCategories = merchantCategories
        .Where(x => !categoryId.HasValue || x.Id == categoryId.Value)
        .ToList();

    var offset = (page - 1) * pageSize;
    var categoryResult = new List<object>(filteredCategories.Count);
    foreach (var category in filteredCategories)
    {
        var total = await ExecuteCountByCategoryAsync(connection, merchantId, category.Id, ct);
        var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
        var pagedProducts = await LoadCategoryPageAsync(connection, merchantId, category.Id, offset, pageSize, ct);

        categoryResult.Add(new
        {
            id = category.Id,
            name = category.Name,
            pagination = new
            {
                page,
                pageSize,
                total,
                totalPages
            },
            products = pagedProducts
        });
    }

    return Results.Ok(new
    {
        merchantId,
        pagination = new
        {
            page,
            pageSize
        },
        filters = new
        {
            categoryId
        },
        categories = categoryResult
    });
}

static async Task<int> ExecuteCountByCategoryAsync(NpgsqlConnection connection, int merchantId, int categoryId, CancellationToken ct)
{
    const string sql =
        """
        SELECT COUNT(*)
        FROM "AggregateProducts"
        WHERE "MerchantId" = @merchantId AND "ProductCategoryId" = @categoryId;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("merchantId", merchantId);
    command.Parameters.AddWithValue("categoryId", categoryId);
    return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
}

static async Task<List<object>> LoadCategoryPageAsync(NpgsqlConnection connection, int merchantId, int categoryId, int offset, int pageSize, CancellationToken ct)
{
    const string sql =
        """
        SELECT "Id", "SortOrder", "Name", "Price"
        FROM "AggregateProducts"
        WHERE "MerchantId" = @merchantId AND "ProductCategoryId" = @categoryId
        ORDER BY "SortOrder", "Id"
        OFFSET @offset
        LIMIT @limit;
        """;

    var items = new List<object>();
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("merchantId", merchantId);
    command.Parameters.AddWithValue("categoryId", categoryId);
    command.Parameters.AddWithValue("offset", offset);
    command.Parameters.AddWithValue("limit", pageSize);

    await using var reader = await command.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct))
    {
        items.Add(new
        {
            id = reader.GetInt32(0),
            sortOrder = reader.GetInt32(1),
            name = reader.GetString(2),
            price = reader.GetDecimal(3)
        });
    }

    return items;
}

