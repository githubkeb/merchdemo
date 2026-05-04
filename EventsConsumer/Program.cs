using EventsConsumer;
using EventsConsumer.Data;
using EventsConsumer.Messaging;
using EventsConsumer.Messaging.Options;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:Default is required for EventsConsumer");
}

builder.Services.AddDbContext<MerchantAggregatesDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.AddSingleton<ConsumerSettingsStore>(sp =>
{
    var store = new ConsumerSettingsStore();
    store.Update(new ConsumerSettings
    {
        FailBeforeSavePercent = builder.Configuration.GetValue<int?>("ConsumerFailure:FailBeforeSavePercent") ?? 0,
        FailAfterSaveBeforeAckPercent = builder.Configuration.GetValue<int?>("ConsumerFailure:FailAfterSaveBeforeAckPercent") ?? 0
    });
    return store;
});
builder.Services.AddSingleton<IConsumerSettings>(sp => sp.GetRequiredService<ConsumerSettingsStore>());
builder.Services.AddSingleton<IConsumerSettingsManager>(sp => sp.GetRequiredService<ConsumerSettingsStore>());
builder.Services.AddHostedService<RabbitMqConsumerHostedService>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<MerchantAggregatesDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.MapGet("/", () => "EventsConsumer is running");
app.MapGet("/consumer-failure-settings", (IConsumerSettingsManager settingsManager) => Results.Ok(settingsManager.GetSnapshot()));
app.MapPut("/consumer-failure-settings", (ConsumerSettings settings, IConsumerSettingsManager settingsManager) =>
{

    var updated = settingsManager.Update(settings);
    return Results.Ok(updated);
});

app.Run();