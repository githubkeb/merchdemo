using Aggregator.Models;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Marten;
using Weasel.Core;

var builder = WebApplication.CreateBuilder(args);

var replicaConnectionString = builder.Configuration.GetConnectionString("AggregatesReplica");
if (string.IsNullOrWhiteSpace(replicaConnectionString))
{
    throw new InvalidOperationException("ConnectionStrings:AggregatesReplica is required for ResultApi");
}

builder.Services.AddSingleton<IDocumentStore>(_ => DocumentStore.For(options =>
{
    options.Connection(replicaConnectionString);
    options.AutoCreateSchemaObjects = AutoCreate.None;
    options.Schema.For<ProductAggregate>().Identity(x => x.Id);
    options.Schema.For<CategoryAggregate>().Identity(x => x.Id);
}));

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

app.MapGet("/api/state-counts", async (IHttpClientFactory httpClientFactory, IDocumentStore replicaStore, CancellationToken ct) =>
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
        await using var session = replicaStore.QuerySession();

        var products = await session.Query<ProductAggregate>().CountAsync(ct);
        var categories = await session.Query<CategoryAggregate>().CountAsync(ct);

        var productMerchants = await session.Query<ProductAggregate>()
            .Select(x => x.MerchantId)
            .ToListAsync(ct);
        var categoryMerchants = await session.Query<CategoryAggregate>()
            .Select(x => x.MerchantId)
            .ToListAsync(ct);
        var merchants = productMerchants
            .Concat(categoryMerchants)
            .Distinct()
            .Count();

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

app.MapGet("/api/random-merchant", async (IDocumentStore replicaStore, CancellationToken ct) =>
{
    try
    {
        await using var session = replicaStore.QuerySession();
        var products = await session.Query<ProductAggregate>()
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
        var categories = await session.Query<CategoryAggregate>()
            .OrderBy(x => x.Id)
            .ToListAsync(ct);

        var merchantIds = products
            .Select(x => x.MerchantId)
            .Concat(categories.Select(x => x.MerchantId))
            .Distinct()
            .ToList();

        if (merchantIds.Count == 0)
        {
            return Results.NotFound(new { message = "No merchants found in aggregates" });
        }

        var merchantId = merchantIds[Random.Shared.Next(merchantIds.Count)];

        var merchantCategories = categories
            .Where(x => x.MerchantId == merchantId)
            .OrderBy(x => x.Id)
            .ToList();

        var merchantProducts = products
            .Where(x => x.MerchantId == merchantId)
            .OrderBy(x => x.Id)
            .ToList();

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

