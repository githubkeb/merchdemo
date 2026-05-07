using EventsPublisher.Data;
using EventsPublisher.Data.Entities;
using EventsPublisher.Messaging.Contracts;
using EventsPublisher.Messaging.Publishing;
using EventsPublisher.Messaging.Stubs;
using Microsoft.EntityFrameworkCore;

namespace EventsPublisher;

public class MerchantRobot
{
    private const int TargetMerchantId = 1;
    private const int NewMerchantProbabilityPercent = 25;
    private const int ProductsPerStep = 10;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRobotSettings _settings;
    private readonly IRabbitPublisher _publisher;
    private readonly ILogger<MerchantRobot> _logger;

    public MerchantRobot(
        IServiceScopeFactory scopeFactory,
        IRobotSettings settings,
        IRabbitPublisher publisher,
        ILogger<MerchantRobot> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task Loop(CancellationToken ct)
    {
        await Task.Delay(20000, ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_settings.IsEnabled)
                {
                    await WaitDelay(ct);
                    continue;
                }

                await using var scope = _scopeFactory.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<MerchantDbContext>();


                var preferredMerchant = await EnsureMerchantAsync(dbContext, TargetMerchantId, ct);
                await ProcessMerchantAsync(dbContext, preferredMerchant, ct);

                var randomMerchant = await EnsureRandomMerchantAsync(dbContext, ct);
                if (randomMerchant.Id != preferredMerchant.Id)
                {
                    await ProcessMerchantAsync(dbContext, randomMerchant, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Merchant robot loop iteration failed");
            }

            await WaitDelay(ct);
        }
    }

    private async Task<Merchant> EnsureMerchantAsync(MerchantDbContext dbContext, int preferredMerchantId, CancellationToken ct)
    {
        var preferredMerchant = await dbContext.Merchants.FirstOrDefaultAsync(x => x.Id == preferredMerchantId, ct);
        if (preferredMerchant is not null)
        {
            return preferredMerchant;
        }

        var merchantsCount = await dbContext.Merchants.CountAsync(ct);
        var maxMerchants = _settings.MaxMerchants;
        var canCreateMore = maxMerchants <= 0 || merchantsCount < maxMerchants;
        var shouldCreateNew = merchantsCount == 0 ||
                              (canCreateMore && Random.Shared.Next(100) < NewMerchantProbabilityPercent);

        if (shouldCreateNew)
        {
            var now = DateTimeOffset.UtcNow;
            var merchant = new Merchant
            {
                Name = $"Merchant-{RandomSuffix()}",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            dbContext.Merchants.Add(merchant);
            await dbContext.SaveChangesAsync(ct);

            _logger.LogInformation("Created merchant {MerchantId} ({MerchantName})", merchant.Id, merchant.Name);
            return merchant;
        }

        var randomIndex = Random.Shared.Next(merchantsCount);
        return await dbContext.Merchants
            .OrderBy(x => x.CreatedAtUtc)
            .Skip(randomIndex)
            .FirstAsync(ct);
    }

    private async Task<Merchant> EnsureRandomMerchantAsync(MerchantDbContext dbContext, CancellationToken ct)
    {
        var merchantsCount = await dbContext.Merchants.CountAsync(ct);
        var maxMerchants = _settings.MaxMerchants;
        var canCreateMore = maxMerchants <= 0 || merchantsCount < maxMerchants;
        var shouldCreateNew = merchantsCount == 0 ||
                              (canCreateMore && Random.Shared.Next(100) < NewMerchantProbabilityPercent);

        if (shouldCreateNew)
        {
            var now = DateTimeOffset.UtcNow;
            var merchant = new Merchant
            {
                Name = $"Merchant-{RandomSuffix()}",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            dbContext.Merchants.Add(merchant);
            await dbContext.SaveChangesAsync(ct);

            _logger.LogInformation("Created merchant {MerchantId} ({MerchantName})", merchant.Id, merchant.Name);
            return merchant;
        }

        var randomIndex = Random.Shared.Next(merchantsCount);
        return await dbContext.Merchants
            .OrderBy(x => x.CreatedAtUtc)
            .Skip(randomIndex)
            .FirstAsync(ct);
    }

    private async Task ProcessMerchantAsync(MerchantDbContext dbContext, Merchant merchant, CancellationToken ct)
    {
        await AddCategoryAsync(dbContext, merchant, ct);
        await ModifyCategoryAsync(dbContext, merchant, ct);
        for (var i = 0; i < ProductsPerStep; i++)
        {
            await AddProductAsync(dbContext, merchant, ct);
        }
        await ModifyProductAsync(dbContext, merchant, ct);
    }

    private async Task AddCategoryAsync(MerchantDbContext dbContext, Merchant merchant, CancellationToken ct)
    {
        var categoriesCount = await dbContext.MerchantCategories.CountAsync(x => x.MerchantId == merchant.Id, ct);
        if (categoriesCount >= _settings.MaxCategories)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var category = new MerchantCategory
        {
            MerchantId = merchant.Id,
            Name = $"Category-{RandomSuffix()}",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.MerchantCategories.Add(category);
        await dbContext.SaveChangesAsync(ct);

        await _publisher.PublishAsync(
            ExchangeNames.ProductCategories,
            routingKey: "product-category.created",
            new MerchantCategoryMessage(category.Id, merchant.Id, category.Name, "created", now),
            ct);
    }

    private async Task ModifyCategoryAsync(MerchantDbContext dbContext, Merchant merchant, CancellationToken ct)
    {
        var categories = await dbContext.MerchantCategories
            .Where(x => x.MerchantId == merchant.Id)
            .ToListAsync(ct);

        if (categories.Count == 0)
        {
            return;
        }

        var category = categories[Random.Shared.Next(categories.Count)];
        var now = DateTimeOffset.UtcNow;

        category.Name = $"Category-{RandomSuffix()}";
        category.UpdatedAtUtc = now;

        await dbContext.SaveChangesAsync(ct);

        await _publisher.PublishAsync(
            ExchangeNames.ProductCategories,
            routingKey: "product-category.updated",
            new MerchantCategoryMessage(category.Id, merchant.Id, category.Name, "updated", now),
            ct);
    }

    private async Task AddProductAsync(MerchantDbContext dbContext, Merchant merchant, CancellationToken ct)
    {
        var productsCount = await dbContext.Products.CountAsync(x => x.MerchantId == merchant.Id, ct);
        if (productsCount >= _settings.MaxProducts)
        {
            return;
        }

        var categories = await dbContext.MerchantCategories
            .Where(x => x.MerchantId == merchant.Id)
            .ToListAsync(ct);

        if (categories.Count == 0)
        {
            return;
        }

        var categoryId = SelectCategoryId(categories);
        var nextSortOrder = await GetNextSortOrderAsync(dbContext, merchant.Id, ct);
        var now = DateTimeOffset.UtcNow;
        var product = new Product
        {
            MerchantId = merchant.Id,
            MerchantCategoryId = categoryId,
            SortOrder = nextSortOrder,
            Name = $"Product-{RandomSuffix()}",
            Price = RandomPrice(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(ct);

        await PublishProductAsync(product, "created", ct);
    }

    private async Task ModifyProductAsync(MerchantDbContext dbContext, Merchant merchant, CancellationToken ct)
    {
        var products = await dbContext.Products
            .Where(x => x.MerchantId == merchant.Id)
            .ToListAsync(ct);

        if (products.Count == 0)
        {
            return;
        }

        var product = products[Random.Shared.Next(products.Count)];
        var now = DateTimeOffset.UtcNow;

        product.Name = $"Product-{RandomSuffix()}";
        product.Price = RandomPrice();
        product.UpdatedAtUtc = now;

        await dbContext.SaveChangesAsync(ct);

        await PublishProductAsync(product, "updated", ct);
    }

    private async Task PublishProductAsync(Product product, string action, CancellationToken ct)
    {
        await _publisher.PublishAsync(
            ExchangeNames.Products,
            routingKey: $"product.{action}",
            new ProductMessage(
                product.Id,
                product.MerchantId,
                product.MerchantCategoryId,
                product.SortOrder,
                product.Name,
                product.Price,
                action,
                DateTimeOffset.UtcNow),
            ct);
    }

    private static async Task<int> GetNextSortOrderAsync(MerchantDbContext dbContext, int merchantId, CancellationToken ct)
    {
        var maxSortOrder = await dbContext.Products
            .Where(x => x.MerchantId == merchantId)
            .Select(x => (int?)x.SortOrder)
            .MaxAsync(ct);

        return (maxSortOrder ?? 0) + 1;
    }

    private int? SelectCategoryId(IReadOnlyList<MerchantCategory> categories)
    {
        if (categories.Count == 0)
        {
            return null;
        }

        return categories[Random.Shared.Next(categories.Count)].Id;
    }

    private async Task WaitDelay(CancellationToken ct)
    {
        await Task.Delay(_settings.WaitBetweenLoops, ct);
    }

    private static string RandomSuffix() => Guid.NewGuid().ToString("N")[..8];

    private static decimal RandomPrice() => decimal.Round((decimal)(Random.Shared.NextDouble() * 990 + 10), 2);
}