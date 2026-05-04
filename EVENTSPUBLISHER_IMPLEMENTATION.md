# EventsPublisher Implementation Summary

## ✅ Completed Features

### 1. PostgreSQL DbContext with Domain Entities
- **`Merchant`** — Root aggregate for the merchant domain
  - Collections: `Categories`, `Products`
  - Timestamps: `CreatedAtUtc`, `UpdatedAtUtc`
  - Unique index on `Name`

- **`MerchantCategory`** — Product categories owned by merchant
  - Foreign key to `Merchant` (cascade delete)
  - Unique index on `(MerchantId, Name)`
  - Timestamps: `CreatedAtUtc`, `UpdatedAtUtc`

- **`Product`** — Products optionally assigned to categories
  - Fields: `Name`, `SKU`, `Price` (decimal 18,2)
  - Foreign key to `Merchant` (cascade delete)
  - Optional foreign key to `MerchantCategory` (set null on delete)
  - Supports products **with** and **without** categories (via nullable `MerchantCategoryId`)
  - Unique index on `(MerchantId, SKU)`
  - Timestamps: `CreatedAtUtc`, `UpdatedAtUtc`

**Location:** `EventsPublisher/Data/`
- `MerchantDbContext.cs` — EF Core context with full model configuration
- `Entities/Merchant.cs`, `Entities/MerchantCategory.cs`, `Entities/Product.cs`

### 2. MerchantRobot with Data Mutation & RabbitMQ Publishing
The robot runs in a background loop, performing random additions and modifications:

- **`EnsureMerchantAsync`** — Ensures one merchant exists; creates if missing, updates name if settings changed
- **`AddCategoryAsync`** — Creates random categories up to `MaxCategories` limit
  - Publishes `MerchantCategoryMessage` to `ProductCategories` exchange with routing key `product-category.created`
- **`ModifyCategoryAsync`** — Randomly updates existing category name
  - Publishes `MerchantCategoryMessage` with action `"updated"` and routing key `product-category.updated`
- **`AddProductAsync`** — Creates random products up to `MaxProducts` limit
  - Randomly assigns to category or leaves categoryless (based on `CategorylessProductProbabilityPercent`)
  - Publishes `ProductMessage` with action `"created"` and routing key `product.created`
- **`ModifyProductAsync`** — Randomly updates existing product name, price, and category assignment
  - Publishes `ProductMessage` with action `"updated"` and routing key `product.updated`

**Location:** `EventsPublisher/MerchantRobot.cs`
- **Hosted Service:** `EventsPublisher/HostedServices/MerchantRobotHostedService.cs`

**Event Publishing:** All mutations immediately publish RabbitMQ messages via `IRabbitPublisher` to inform consumers of state changes.

### 3. Thread-Safe Robot Settings Store with HTTP Endpoints

**Entities:**
- `RobotSettings` — DTO with configuration fields
  - `WaitBetweenLoopsMs` — milliseconds between robot iterations (default: 3000)
  - `MerchantName` — merchant name to create/sync (default: "Demo merchant")
  - `MaxCategories` — maximum categories to create (default: 10)
  - `MaxProducts` — maximum products to create (default: 30)
  - `CategorylessProductProbabilityPercent` — % chance product has no category (default: 35)

- `RobotSettingsStore` — Thread-safe implementation (lock-based)
  - Implements `IRobotSettings` (read-only properties)
  - Implements `IRobotSettingsManager` (mutation methods)
  - `GetSnapshot()` — returns current settings as DTO
  - `Update(settings)` — validates and replaces settings atomically
  - `Validate(settings)` — static validation with detailed error messages

**HTTP Endpoints:**
```
GET  /robot-settings        → returns current RobotSettings as JSON
PUT  /robot-settings        → accepts RobotSettings, validates, updates, returns updated settings
                              Returns 400 Bad Request if validation fails
```

**Location:** `EventsPublisher/RobotSettings.cs`

### 4. Event Message Contracts
**Location:** `EventsPublisher/Messaging/Contracts/ProductMessages.cs`

- `ProductMessage` — represents product create/update events
  ```csharp
  record ProductMessage(
      Guid ProductId,
      Guid MerchantId,
      Guid? MerchantCategoryId,    // null if product has no category
      string Name,
      string Sku,
      decimal Price,
      string Action,               // "created" or "updated"
      DateTimeOffset OccurredAtUtc)
  ```

- `MerchantCategoryMessage` — represents category create/update events
  ```csharp
  record MerchantCategoryMessage(
      Guid MerchantCategoryId,
      Guid MerchantId,
      string Name,
      string Action,               // "created" or "updated"
      DateTimeOffset OccurredAtUtc)
  ```

### 5. Configuration & Startup
**appsettings.json (Production):**
```json
{
  "ConnectionStrings": {
    "Default": "Host=postgres;Port=5432;Database=merchantdemo;Username=merchant;Password=merchant"
  },
  "RabbitMq": {
    "Host": "rabbitmq",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest",
    "VirtualHost": "/"
  },
  "Robot": {
    "WaitBetweenLoopsMs": 3000,
    "MerchantName": "Demo merchant",
    "MaxCategories": 10,
    "MaxProducts": 30,
    "CategorylessProductProbabilityPercent": 35
  }
}
```

**appsettings.Development.json (Local):**
```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=merchantdemo;Username=merchant;Password=merchant"
  },
  "RabbitMq": {
    "Host": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest",
    "VirtualHost": "/"
  },
  "Robot": {
    "WaitBetweenLoopsMs": 3000,
    "MerchantName": "Local demo merchant",
    "MaxCategories": 10,
    "MaxProducts": 30,
    "CategorylessProductProbabilityPercent": 35
  }
}
```

**Program.cs Registration:**
```csharp
builder.Services.AddDbContext<MerchantDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton<RobotSettingsStore>();
builder.Services.AddSingleton<IRobotSettings>(sp => sp.GetRequiredService<RobotSettingsStore>());
builder.Services.AddSingleton<IRobotSettingsManager>(sp => sp.GetRequiredService<RobotSettingsStore>());
builder.Services.AddSingleton<MerchantRobot>();
builder.Services.AddHostedService<MerchantRobotHostedService>();
```

### 6. Dependencies Added
**EventsPublisher.csproj:**
```xml
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.0"/>
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0"/>
<PackageReference Include="RabbitMQ.Client" Version="6.8.1"/>
```

---

## 🚀 How to Run

### Docker Compose (Full Stack)
```powershell
cd C:\Src\MerchantDemo
docker compose -f Docker/docker-compose.yml up -d --build
docker compose -f Docker/docker-compose.yml ps
```

**Services:**
- `eventspublisher` → http://localhost:8082
- `postgres` → localhost:5432
- `rabbitmq` → http://localhost:15672 (guest/guest)

### Local Development (Without Docker)
Requires PostgreSQL and RabbitMQ running on localhost:

```powershell
cd C:\Src\MerchantDemo\EventsPublisher
dotnet run --urls http://localhost:5099
```

---

## 📋 Testing the Solution

### 1. Check Current Robot Settings
```powershell
Invoke-WebRequest http://localhost:5099/robot-settings | Select-Object -ExpandProperty Content
```

### 2. Update Robot Settings
```powershell
$body = @{
    waitBetweenLoopsMs = 5000
    merchantName = "Updated merchant"
    maxCategories = 15
    maxProducts = 50
    categorylessProductProbabilityPercent = 40
} | ConvertTo-Json

Invoke-WebRequest -Uri http://localhost:5099/robot-settings -Method PUT -Body $body -ContentType "application/json"
```

### 3. Test Validation (Should Return 400)
```powershell
$body = @{
    waitBetweenLoopsMs = 100              # Too low (min 250)
    merchantName = ""                     # Empty (invalid)
    maxCategories = 0                     # Too low (min 1)
    maxProducts = -5                      # Negative
    categorylessProductProbabilityPercent = 150  # Out of range (0-100)
} | ConvertTo-Json

Invoke-WebRequest -Uri http://localhost:5099/robot-settings -Method PUT -Body $body -ContentType "application/json" -ErrorAction SilentlyContinue
```

### 4. Monitor RabbitMQ Events
```
Open http://localhost:15672
Login: guest/guest
Navigate to Exchanges → check `stub.products.exchange` and `stub.product-categories.exchange`
```

### 5. Query PostgreSQL
```powershell
# Using psql or any PostgreSQL client
psql -h localhost -U merchant -d merchantdemo

SELECT * FROM "Merchants";
SELECT * FROM "MerchantCategories";
SELECT * FROM "Products" WHERE "MerchantCategoryId" IS NULL;  -- Products without category
SELECT * FROM "Products" WHERE "MerchantCategoryId" IS NOT NULL;  -- Products with category
```

---

## ✅ Verification Checklist

- ✅ PostgreSQL DbContext with Merchant, MerchantCategory, Product entities
- ✅ Products can have categories (via FK) or be categoryless (via nullable FK)
- ✅ MerchantRobot performs random add/modify operations
- ✅ All mutations trigger RabbitMQ message publication
- ✅ Robot settings are thread-safe and can be updated at runtime via HTTP
- ✅ Robot settings have validation with clear error messages
- ✅ HTTP endpoints: `GET /robot-settings`, `PUT /robot-settings`
- ✅ Solution builds without errors
- ✅ Docker Compose configuration is valid
- ✅ PostgreSQL and RabbitMQ integration configured

---

## 📁 File Structure

```
EventsPublisher/
├── Data/
│   ├── Entities/
│   │   ├── Merchant.cs
│   │   ├── MerchantCategory.cs
│   │   └── Product.cs
│   └── MerchantDbContext.cs
├── HostedServices/
│   ├── MerchantRobotHostedService.cs
│   ├── ProductEventsPublisherHostedService.cs (legacy, unused)
│   └── ProductCategoryEventsPublisherHostedService.cs (legacy, unused)
├── Messaging/
│   ├── Contracts/
│   │   └── ProductMessages.cs
│   ├── Publishing/
│   │   ├── IRabbitPublisher.cs
│   │   └── RabbitPublisher.cs
│   ├── Stubs/
│   │   └── ExchangeNames.cs
│   └── Options/
│       └── RabbitMqOptions.cs
├── MerchantRobot.cs
├── RobotSettings.cs
├── Program.cs
├── EventsPublisher.csproj
├── appsettings.json
└── appsettings.Development.json
```

---

## 🎯 Next Steps (Optional)

1. **Add EF Migrations** — Use `dotnet ef migrations add InitialCreate` for production deployments
2. **Consumer Services** — Implement EventsConsumer and Aggregator to consume the published messages
3. **ResultApi** — Add endpoints to query merchant/category/product data
4. **Monitoring** — Integrate health checks and structured logging
5. **Unit Tests** — Add tests for MerchantRobot logic and RobotSettings validation

