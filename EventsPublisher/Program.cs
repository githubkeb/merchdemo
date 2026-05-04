using EventsPublisher;
using EventsPublisher.Data;
using EventsPublisher.HostedServices;
using EventsPublisher.Messaging.Options;
using EventsPublisher.Messaging.Publishing;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
	throw new InvalidOperationException("ConnectionStrings:Default is required for EventsPublisher");
}

builder.Services.AddDbContext<MerchantDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.AddSingleton<IRabbitPublisher, RabbitPublisher>();
builder.Services.AddSingleton<RobotSettingsStore>();
builder.Services.AddSingleton<IRobotSettings>(sp => sp.GetRequiredService<RobotSettingsStore>());
builder.Services.AddSingleton<IRobotSettingsManager>(sp => sp.GetRequiredService<RobotSettingsStore>());
builder.Services.AddSingleton<MerchantRobot>();
builder.Services.AddHostedService<MerchantRobotHostedService>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
	var dbContext = scope.ServiceProvider.GetRequiredService<MerchantDbContext>();
	await dbContext.Database.MigrateAsync();
}

app.MapGet("/", () => "EventsPublisher is running");
app.MapGet("/state-counts", async (MerchantDbContext dbContext) =>
{
	var merchants = await dbContext.Merchants.CountAsync();
	var products = await dbContext.Products.CountAsync();
	var categories = await dbContext.MerchantCategories.CountAsync();

	return Results.Ok(new
	{
		merchants,
		products,
		categories
	});
});
app.MapGet("/robot-settings", (IRobotSettingsManager settingsManager) => Results.Ok(settingsManager.GetSnapshot()));
app.MapPut("/robot-settings", (RobotSettings settings, IRobotSettingsManager settingsManager) =>
{

	var updated = settingsManager.Update(settings);
	return Results.Ok(updated);
});
app.MapPost("/robot-stop", (IRobotSettingsManager settingsManager) =>
{
	var current = settingsManager.GetSnapshot();
	current.IsEnabled = false;
	var updated = settingsManager.Update(current);
	return Results.Ok(updated);
});

app.Run();