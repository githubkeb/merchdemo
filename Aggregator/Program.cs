using Aggregator;
using Aggregator.Models;
using Aggregator.Options;
using Aggregator.Services;
using Npgsql;

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
builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(connectionString).Build());

builder.Services.AddHostedService<ProductAggregationHostedService>();
builder.Services.AddHostedService<CategoryAggregationHostedService>();

var app = builder.Build();

await EnsureAggregateSchemaAsync(app.Services.GetRequiredService<NpgsqlDataSource>());

app.MapGet("/", () => "Aggregator is running");
app.MapGet("/products", async (NpgsqlDataSource dataSource, CancellationToken ct) =>
{
	var products = new List<ProductAggregate>();
	await using var connection = await dataSource.OpenConnectionAsync(ct);
	const string sql =
		"""
		SELECT "Id", "MerchantId", "ProductCategoryId", "Name", "Price", "LastAction", "LastOccurredAtUtc", "UpdatedAtUtc"
		FROM "AggregateProducts"
		ORDER BY "Id";
		""";
	await using var command = new NpgsqlCommand(sql, connection);
	await using var reader = await command.ExecuteReaderAsync(ct);
	while (await reader.ReadAsync(ct))
	{
		products.Add(new ProductAggregate
		{
			Id = reader.GetInt32(0),
			MerchantId = reader.GetInt32(1),
			ProductCategoryId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
			Name = reader.GetString(3),
			Price = reader.GetDecimal(4),
			LastAction = reader.GetString(5),
			LastOccurredAtUtc = reader.GetFieldValue<DateTimeOffset>(6),
			UpdatedAtUtc = reader.GetFieldValue<DateTimeOffset>(7)
		});
	}
	return Results.Ok(products);
});

app.MapGet("/categories", async (NpgsqlDataSource dataSource, CancellationToken ct) =>
{
	var categories = new List<CategoryAggregate>();
	await using var connection = await dataSource.OpenConnectionAsync(ct);
	const string sql =
		"""
		SELECT "Id", "MerchantId", "Name", "LastAction", "LastOccurredAtUtc", "UpdatedAtUtc"
		FROM "AggregateCategories"
		ORDER BY "Id";
		""";
	await using var command = new NpgsqlCommand(sql, connection);
	await using var reader = await command.ExecuteReaderAsync(ct);
	while (await reader.ReadAsync(ct))
	{
		categories.Add(new CategoryAggregate
		{
			Id = reader.GetInt32(0),
			MerchantId = reader.GetInt32(1),
			Name = reader.GetString(2),
			LastAction = reader.GetString(3),
			LastOccurredAtUtc = reader.GetFieldValue<DateTimeOffset>(4),
			UpdatedAtUtc = reader.GetFieldValue<DateTimeOffset>(5)
		});
	}
	return Results.Ok(categories);
});

app.MapGet("/state-counts", async (NpgsqlDataSource dataSource, CancellationToken ct) =>
{
	await using var connection = await dataSource.OpenConnectionAsync(ct);
	var products = await ExecuteCountAsync(connection, "SELECT COUNT(*) FROM \"AggregateProducts\";", ct);
	var categories = await ExecuteCountAsync(connection, "SELECT COUNT(*) FROM \"AggregateCategories\";", ct);
	var merchants = await ExecuteCountAsync(connection, "SELECT COUNT(*) FROM \"AggregateMerchants\";", ct);

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

static async Task EnsureAggregateSchemaAsync(NpgsqlDataSource dataSource)
{
	await using var connection = await dataSource.OpenConnectionAsync();
	const string sql =
		"""
		CREATE TABLE IF NOT EXISTS "AggregationCheckpoints" (
		    "Id" character varying(64) PRIMARY KEY,
		    "LastOccurredAtUtc" timestamp with time zone NOT NULL,
		    "LastMessageId" character varying(256) NOT NULL,
		    "UpdatedAtUtc" timestamp with time zone NOT NULL
		);

		CREATE TABLE IF NOT EXISTS "AggregateMerchants" (
		    "Id" integer PRIMARY KEY,
		    "UpdatedAtUtc" timestamp with time zone NOT NULL
		);

		CREATE TABLE IF NOT EXISTS "AggregateCategories" (
		    "Id" integer PRIMARY KEY,
		    "MerchantId" integer NOT NULL,
		    "Name" character varying(200) NOT NULL,
		    "LastAction" character varying(32) NOT NULL,
		    "LastOccurredAtUtc" timestamp with time zone NOT NULL,
		    "UpdatedAtUtc" timestamp with time zone NOT NULL,
		    CONSTRAINT "FK_AggregateCategories_AggregateMerchants_MerchantId"
		        FOREIGN KEY ("MerchantId") REFERENCES "AggregateMerchants" ("Id") ON DELETE CASCADE
		);

		CREATE TABLE IF NOT EXISTS "AggregateProducts" (
		    "Id" integer PRIMARY KEY,
		    "MerchantId" integer NOT NULL,
		    "ProductCategoryId" integer NULL,
		    "Name" character varying(200) NOT NULL,
		    "Price" numeric(18,2) NOT NULL,
		    "LastAction" character varying(32) NOT NULL,
		    "LastOccurredAtUtc" timestamp with time zone NOT NULL,
		    "UpdatedAtUtc" timestamp with time zone NOT NULL,
		    CONSTRAINT "FK_AggregateProducts_AggregateMerchants_MerchantId"
		        FOREIGN KEY ("MerchantId") REFERENCES "AggregateMerchants" ("Id") ON DELETE CASCADE,
		    CONSTRAINT "FK_AggregateProducts_AggregateCategories_ProductCategoryId"
		        FOREIGN KEY ("ProductCategoryId") REFERENCES "AggregateCategories" ("Id") ON DELETE SET NULL
		);

		CREATE INDEX IF NOT EXISTS "IX_AggregateProducts_MerchantId" ON "AggregateProducts" ("MerchantId");
		CREATE INDEX IF NOT EXISTS "IX_AggregateCategories_MerchantId" ON "AggregateCategories" ("MerchantId");
		""";

	await using var command = new NpgsqlCommand(sql, connection);
	await command.ExecuteNonQueryAsync();
}

static async Task<int> ExecuteCountAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
{
	await using var command = new NpgsqlCommand(sql, connection);
	return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
}
