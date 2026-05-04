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

app.MapGet("/api/random-merchant", async (NpgsqlDataSource replicaDataSource, CancellationToken ct) =>
{
    try
    {
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

        var merchantProducts = new List<ProductAggregate>();
        const string productsSql =
            """
            SELECT "Id", "MerchantId", "ProductCategoryId", "Name", "Price", "LastAction", "LastOccurredAtUtc", "UpdatedAtUtc"
            FROM "AggregateProducts"
            WHERE "MerchantId" = @merchantId
            ORDER BY "Id";
            """;
        await using (var productsCommand = new NpgsqlCommand(productsSql, connection))
        {
            productsCommand.Parameters.AddWithValue("merchantId", merchantId);
            await using var productsReader = await productsCommand.ExecuteReaderAsync(ct);
            while (await productsReader.ReadAsync(ct))
            {
                merchantProducts.Add(new ProductAggregate
                {
                    Id = productsReader.GetInt32(0),
                    MerchantId = productsReader.GetInt32(1),
                    ProductCategoryId = productsReader.IsDBNull(2) ? null : productsReader.GetInt32(2),
                    Name = productsReader.GetString(3),
                    Price = productsReader.GetDecimal(4),
                    LastAction = productsReader.GetString(5),
                    LastOccurredAtUtc = productsReader.GetFieldValue<DateTimeOffset>(6),
                    UpdatedAtUtc = productsReader.GetFieldValue<DateTimeOffset>(7)
                });
            }
        }

        var categoryIds = merchantCategories.Select(x => x.Id).ToHashSet();
        var productsByCategory = merchantProducts
            .Where(x => x.ProductCategoryId.HasValue && categoryIds.Contains(x.ProductCategoryId.Value))
            .GroupBy(x => x.ProductCategoryId!.Value)
            .ToDictionary(
                x => x.Key,
                x => x.Select(p => new
                {
                    id = p.Id,
                    name = p.Name,
                    price = p.Price
                }).ToList());

        var categoryResult = merchantCategories.Select(category => new
        {
            id = category.Id,
            name = category.Name,
            products = productsByCategory.GetValueOrDefault(category.Id, [])
        }).ToList();

        var uncategorizedProducts = merchantProducts
            .Where(x => !x.ProductCategoryId.HasValue || !categoryIds.Contains(x.ProductCategoryId.Value))
            .Select(p => new
            {
                id = p.Id,
                name = p.Name,
                price = p.Price
            })
            .ToList();

        return Results.Ok(new
        {
            merchantId,
            categories = categoryResult,
            uncategorizedProducts
        });
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

