using Aggregator;
using Aggregator.Models;
using Aggregator.Options;
using Aggregator.Services;
using Marten;
using Weasel.Core;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
	throw new InvalidOperationException("ConnectionStrings:Default is required for Aggregator");
}

builder.Services.Configure<AggregatorOptions>(builder.Configuration.GetSection(AggregatorOptions.SectionName));
builder.Services.AddSingleton<AggregatorSettingsStore>(sp =>
{
	var store = new AggregatorSettingsStore();
	store.Update(new AggregatorSettings
	{
		FailDuringAggregationPercent = builder.Configuration.GetValue<int?>("Aggregator:FailDuringAggregationPercent") ?? 0
	});
	return store;
});
builder.Services.AddSingleton<IAggregatorSettings>(sp => sp.GetRequiredService<AggregatorSettingsStore>());
builder.Services.AddSingleton<IAggregatorSettingsManager>(sp => sp.GetRequiredService<AggregatorSettingsStore>());

builder.Services
	.AddMarten(options =>
	{
		options.Connection(connectionString);
		options.AutoCreateSchemaObjects = AutoCreate.All;
		options.Schema.For<ProductAggregate>().Identity(x => x.Id);
		options.Schema.For<CategoryAggregate>().Identity(x => x.Id);
		options.Schema.For<AggregationCheckpoint>().Identity(x => x.Id);
	})
	.UseLightweightSessions();

builder.Services.AddHostedService<ProductAggregationHostedService>();
builder.Services.AddHostedService<CategoryAggregationHostedService>();

var app = builder.Build();

app.MapGet("/", () => "Aggregator is running");
app.MapGet("/products", async (IDocumentSession session, CancellationToken ct) =>
{
	var products = await session.Query<ProductAggregate>()
		.OrderBy(x => x.Id)
		.ToListAsync(ct);
	return Results.Ok(products);
});

app.MapGet("/categories", async (IDocumentSession session, CancellationToken ct) =>
{
	var categories = await session.Query<CategoryAggregate>()
		.OrderBy(x => x.Id)
		.ToListAsync(ct);
	return Results.Ok(categories);
});

app.MapGet("/state-counts", async (IDocumentSession session, CancellationToken ct) =>
{
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
		merchants,
		products,
		categories
	});
});

app.MapGet("/aggregator-settings", (IAggregatorSettingsManager settingsManager) => Results.Ok(settingsManager.GetSnapshot()));
app.MapPut("/aggregator-settings", (AggregatorSettings settings, IAggregatorSettingsManager settingsManager) =>
{
	var updated = settingsManager.Update(settings);
	return Results.Ok(updated);
});

app.Run();