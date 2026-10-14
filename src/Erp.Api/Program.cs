using System.Security.Claims;
using System.Text.Json;
using Erp.BuildingBlocks;
using Erp.Inventory.Domain;
using Erp.MasterData.Domain;
using Erp.Persistence;
using Erp.Platform.Domain;
using Erp.Sales.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var connectionString = BuildConnectionString(builder.Configuration);
var cognitoAuthority = builder.Configuration["Authentication:Authority"];
var authenticationEnabled = !string.IsNullOrWhiteSpace(cognitoAuthority);

builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
        .AllowAnyHeader()
        .AllowAnyMethod()));
builder.Services.AddScoped<RequestTenantContext>();
builder.Services.AddScoped<ITenantContext>(services => services.GetRequiredService<RequestTenantContext>());
builder.Services.AddDbContext<ErpDbContext>((services, options) => options
    .UseNpgsql(connectionString)
    .AddInterceptors(new TenantConnectionInterceptor(services.GetRequiredService<ITenantContext>())));

if (authenticationEnabled)
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = cognitoAuthority;
            options.Audience = builder.Configuration["Authentication:Audience"];
        });
    builder.Services.AddAuthorization();
}

var app = builder.Build();

app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
        ?? Guid.NewGuid().ToString("N");
    context.TraceIdentifier = correlationId;
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    await next();
});
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
if (authenticationEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}
app.Use(async (context, next) =>
{
    var tenant = context.RequestServices.GetRequiredService<RequestTenantContext>();
    tenant.Set(
        ParseTenant(context.User, builder.Configuration),
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub")
            ?? builder.Configuration["DevelopmentIdentity:UserId"]
            ?? "local-developer");
    await next();
});

app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

var api = app.MapGroup("/api/v1");
if (authenticationEnabled)
{
    api.RequireAuthorization();
}

api.MapGet("/customers", async (ErpDbContext db, string? after, int? limit, CancellationToken cancellationToken) =>
{
    var pageSize = Math.Clamp(limit ?? 50, 1, 200);
    var query = db.Customers.AsNoTracking().OrderBy(customer => customer.Number).AsQueryable();
    if (!string.IsNullOrWhiteSpace(after))
    {
        query = query.Where(customer => string.Compare(customer.Number, after) > 0);
    }

    var customers = await query.Take(pageSize)
        .Select(customer => new CustomerResponse(customer.Id, customer.Number, customer.Name))
        .ToListAsync(cancellationToken);
    return Results.Ok(customers);
});

api.MapPost("/customers", async (CreateCustomerRequest request, ITenantContext tenant, ErpDbContext db, CancellationToken cancellationToken) =>
{
    var customer = Customer.Create(tenant.TenantId, request.Number, request.Name);
    db.Customers.Add(customer);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/v1/customers/{customer.Id}", new CustomerResponse(customer.Id, customer.Number, customer.Name));
});

api.MapGet("/articles", async (ErpDbContext db, string? after, int? limit, CancellationToken cancellationToken) =>
{
    var pageSize = Math.Clamp(limit ?? 50, 1, 200);
    var query = db.Articles.AsNoTracking().OrderBy(article => article.Sku).AsQueryable();
    if (!string.IsNullOrWhiteSpace(after))
    {
        query = query.Where(article => string.Compare(article.Sku, after) > 0);
    }

    var articles = await query.Take(pageSize)
        .Select(article => new ArticleResponse(article.Id, article.Sku, article.Name, article.SalesPrice))
        .ToListAsync(cancellationToken);
    return Results.Ok(articles);
});

api.MapPost("/articles", async (CreateArticleRequest request, ITenantContext tenant, ErpDbContext db, CancellationToken cancellationToken) =>
{
    var article = Article.Create(tenant.TenantId, request.Sku, request.Name, request.SalesPrice);
    db.Articles.Add(article);
    db.StockBalances.Add(StockBalance.Create(tenant.TenantId, article.Id, request.WarehouseCode ?? "MAIN", request.OpeningStock));
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/v1/articles/{article.Id}", new ArticleResponse(article.Id, article.Sku, article.Name, article.SalesPrice));
});

api.MapGet("/stock", async (ErpDbContext db, CancellationToken cancellationToken) =>
{
    var stock = await (
        from balance in db.StockBalances.AsNoTracking()
        join article in db.Articles.AsNoTracking() on balance.ArticleId equals article.Id
        orderby article.Sku, balance.WarehouseCode
        select new StockResponse(
            balance.ArticleId,
            article.Sku,
            article.Name,
            balance.WarehouseCode,
            balance.OnHand,
            balance.Reserved,
            balance.OnHand - balance.Reserved))
        .ToListAsync(cancellationToken);
    return Results.Ok(stock);
});

api.MapGet("/stock/movements", async (ErpDbContext db, int? limit, CancellationToken cancellationToken) =>
{
    var pageSize = Math.Clamp(limit ?? 50, 1, 200);
    var movements = await (
        from movement in db.StockMovements.AsNoTracking()
        join article in db.Articles.AsNoTracking() on movement.ArticleId equals article.Id
        orderby movement.OccurredAt descending
        select new StockMovementResponse(
            movement.Id,
            movement.ArticleId,
            article.Sku,
            article.Name,
            movement.WarehouseCode,
            movement.Quantity,
            movement.MovementType.ToString(),
            movement.Reference,
            movement.CreatedBy,
            movement.OccurredAt))
        .Take(pageSize)
        .ToListAsync(cancellationToken);
    return Results.Ok(movements);
});

api.MapPost("/stock/receipts", async (
    CreateStockReceiptRequest request,
    HttpContext httpContext,
    ITenantContext tenant,
    ErpDbContext db,
    CancellationToken cancellationToken) =>
{
    var idempotencyKey = RequireIdempotencyKey(httpContext);
    if (idempotencyKey is null) return Results.BadRequest(new { error = "Idempotency-Key header is required." });
    if (request.Quantity <= 0) return Results.BadRequest(new { error = "Quantity must be positive." });

    var existing = await db.IdempotencyRecords.AsNoTracking()
        .SingleOrDefaultAsync(record => record.Operation == "StockGoodsReceipt" && record.Key == idempotencyKey, cancellationToken);
    if (existing is not null)
    {
        var prior = await db.StockMovements.AsNoTracking()
            .SingleOrDefaultAsync(movement => movement.Id == existing.ResourceId, cancellationToken);
        if (prior is null) return Results.Ok(new { id = existing.ResourceId, replayed = true });

        var priorArticle = await db.Articles.AsNoTracking()
            .SingleAsync(article => article.Id == prior.ArticleId, cancellationToken);
        return Results.Ok(new StockReceiptResponse(
            prior.Id,
            prior.ArticleId,
            priorArticle.Sku,
            prior.WarehouseCode,
            prior.Quantity,
            prior.Reference,
            prior.OccurredAt,
            Replayed: true));
    }

    var article = await db.Articles.SingleOrDefaultAsync(item => item.Id == request.ArticleId, cancellationToken);
    if (article is null) return Results.BadRequest(new { error = "Article does not exist." });

    var warehouseCode = string.IsNullOrWhiteSpace(request.WarehouseCode) ? "MAIN" : request.WarehouseCode.Trim().ToUpperInvariant();
    await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

    var stock = await db.StockBalances.SingleOrDefaultAsync(
        balance => balance.ArticleId == request.ArticleId && balance.WarehouseCode == warehouseCode,
        cancellationToken);
    if (stock is null)
    {
        stock = StockBalance.Create(tenant.TenantId, request.ArticleId, warehouseCode, 0);
        db.StockBalances.Add(stock);
    }

    stock.Receive(request.Quantity);
    var occurredAt = DateTimeOffset.UtcNow;
    var movement = StockMovement.GoodsReceipt(
        tenant.TenantId,
        request.ArticleId,
        warehouseCode,
        request.Quantity,
        request.Reference,
        tenant.UserId,
        occurredAt);
    db.StockMovements.Add(movement);

    var auditPayload = JsonSerializer.Serialize(new
    {
        movementId = movement.Id,
        articleId = movement.ArticleId,
        sku = article.Sku,
        warehouseCode = movement.WarehouseCode,
        quantity = movement.Quantity,
        reference = movement.Reference
    });
    db.AuditEntries.Add(AuditEntry.Create(
        tenant.TenantId,
        tenant.UserId,
        "Stock.GoodsReceipt",
        nameof(StockMovement),
        movement.Id,
        auditPayload,
        httpContext.TraceIdentifier));
    db.IdempotencyRecords.Add(IdempotencyRecord.Create(tenant.TenantId, idempotencyKey, "StockGoodsReceipt", movement.Id));

    await db.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);

    return Results.Created(
        $"/api/v1/stock/movements/{movement.Id}",
        new StockReceiptResponse(
            movement.Id,
            movement.ArticleId,
            article.Sku,
            movement.WarehouseCode,
            movement.Quantity,
            movement.Reference,
            movement.OccurredAt,
            Replayed: false));
});

api.MapPost("/sales-orders", async (
    CreateSalesOrderRequest request,
    HttpContext httpContext,
    ITenantContext tenant,
    ErpDbContext db,
    CancellationToken cancellationToken) =>
{
    var idempotencyKey = RequireIdempotencyKey(httpContext);
    if (idempotencyKey is null) return Results.BadRequest(new { error = "Idempotency-Key header is required." });

    var existing = await db.IdempotencyRecords.AsNoTracking()
        .SingleOrDefaultAsync(record => record.Operation == "CreateSalesOrder" && record.Key == idempotencyKey, cancellationToken);
    if (existing is not null)
    {
        return Results.Ok(new { id = existing.ResourceId, replayed = true });
    }

    var articleIds = request.Lines.Select(line => line.ArticleId).Distinct().ToArray();
    var articles = await db.Articles
        .Where(article => articleIds.Contains(article.Id))
        .ToDictionaryAsync(article => article.Id, cancellationToken);
    if (articles.Count != articleIds.Length) return Results.BadRequest(new { error = "One or more articles do not exist." });

    var order = SalesOrder.Create(tenant.TenantId, request.CustomerId, request.OrderNumber);
    foreach (var line in request.Lines)
    {
        var article = articles[line.ArticleId];
        order.AddLine(article.Id, article.Sku, line.Quantity, article.SalesPrice);
    }

    db.SalesOrders.Add(order);
    db.IdempotencyRecords.Add(IdempotencyRecord.Create(tenant.TenantId, idempotencyKey, "CreateSalesOrder", order.Id));
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/v1/sales-orders/{order.Id}", ToResponse(order));
});

api.MapGet("/sales-orders/{id:guid}", async (Guid id, ErpDbContext db, CancellationToken cancellationToken) =>
{
    var order = await db.SalesOrders.AsNoTracking().Include(item => item.Lines)
        .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    return order is null ? Results.NotFound() : Results.Ok(ToResponse(order));
});

api.MapPost("/sales-orders/{id:guid}/release", async (
    Guid id,
    HttpContext httpContext,
    ITenantContext tenant,
    ErpDbContext db,
    CancellationToken cancellationToken) =>
{
    var idempotencyKey = RequireIdempotencyKey(httpContext);
    if (idempotencyKey is null) return Results.BadRequest(new { error = "Idempotency-Key header is required." });

    var existing = await db.IdempotencyRecords.AsNoTracking()
        .SingleOrDefaultAsync(record => record.Operation == "ReleaseSalesOrder" && record.Key == idempotencyKey, cancellationToken);
    if (existing is not null) return Results.Ok(new { id = existing.ResourceId, replayed = true });

    await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
    var order = await db.SalesOrders.Include(item => item.Lines)
        .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (order is null) return Results.NotFound();

    foreach (var line in order.Lines)
    {
        var stock = await db.StockBalances.SingleOrDefaultAsync(
            balance => balance.ArticleId == line.ArticleId && balance.WarehouseCode == "MAIN",
            cancellationToken);
        if (stock is null || stock.Available < line.Quantity)
        {
            return Results.Conflict(new { error = $"Insufficient stock for article {line.Sku}." });
        }
        stock.Reserve(line.Quantity);
    }

    var released = order.Release();
    const string eventType = "erp.sales-order.released.v1";
    var payload = JsonSerializer.Serialize(new
    {
        eventId = released.EventId,
        eventType,
        timestamp = released.OccurredAt,
        tenantId = released.TenantId,
        salesOrderId = released.SalesOrderId,
        salesOrderNumber = released.SalesOrderNumber,
        netAmount = order.NetAmount,
        lines = released.Lines
    });

    db.OutboxMessages.Add(OutboxMessage.From(tenant.TenantId, eventType, payload, released.EventId, released.OccurredAt));
    db.AuditEntries.Add(AuditEntry.Create(
        tenant.TenantId,
        tenant.UserId,
        "SalesOrder.Released",
        nameof(SalesOrder),
        order.Id,
        payload,
        httpContext.TraceIdentifier));
    db.IdempotencyRecords.Add(IdempotencyRecord.Create(tenant.TenantId, idempotencyKey, "ReleaseSalesOrder", order.Id));

    await db.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return Results.Ok(ToResponse(order));
});

app.MapFallbackToFile("index.html");
app.Run();

static Guid ParseTenant(ClaimsPrincipal user, IConfiguration configuration)
{
    var claim = user.FindFirstValue("custom:tenant_id") ?? user.FindFirstValue("tenant_id");
    if (Guid.TryParse(claim, out var claimedTenant)) return claimedTenant;

    var configured = configuration["DevelopmentIdentity:TenantId"];
    if (Guid.TryParse(configured, out var developmentTenant)) return developmentTenant;

    throw new InvalidOperationException("No verified tenant identity is available.");
}

static string BuildConnectionString(IConfiguration configuration)
{
    var configured = configuration.GetConnectionString("Erp");
    if (!string.IsNullOrWhiteSpace(configured)) return configured;

    return new NpgsqlConnectionStringBuilder
    {
        Host = configuration["Database:Host"] ?? throw new InvalidOperationException("Database:Host is required."),
        Database = configuration["Database:Name"] ?? "erp_dev",
        Username = configuration["Database:Username"] ?? throw new InvalidOperationException("Database:Username is required."),
        Password = configuration["Database:Password"] ?? throw new InvalidOperationException("Database:Password is required."),
        SslMode = SslMode.Require
    }.ConnectionString;
}

static string? RequireIdempotencyKey(HttpContext context)
{
    var value = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
    return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

static SalesOrderResponse ToResponse(SalesOrder order) =>
    new(
        order.Id,
        order.OrderNumber,
        order.CustomerId,
        order.Status.ToString(),
        order.NetAmount,
        order.Lines.Select(line => new SalesOrderLineResponse(line.ArticleId, line.Sku, line.Quantity, line.UnitPrice)).ToArray());

public sealed class RequestTenantContext : ITenantContext
{
    public Guid TenantId { get; private set; }
    public string UserId { get; private set; } = string.Empty;

    public void Set(Guid tenantId, string userId)
    {
        TenantId = tenantId;
        UserId = userId;
    }
}

public sealed record CreateCustomerRequest(string Number, string Name);
public sealed record CustomerResponse(Guid Id, string Number, string Name);
public sealed record CreateArticleRequest(string Sku, string Name, decimal SalesPrice, decimal OpeningStock, string? WarehouseCode);
public sealed record ArticleResponse(Guid Id, string Sku, string Name, decimal SalesPrice);
public sealed record StockResponse(Guid ArticleId, string Sku, string Name, string WarehouseCode, decimal OnHand, decimal Reserved, decimal Available);
public sealed record CreateStockReceiptRequest(Guid ArticleId, decimal Quantity, string? WarehouseCode, string? Reference);
public sealed record StockReceiptResponse(Guid Id, Guid ArticleId, string Sku, string WarehouseCode, decimal Quantity, string? Reference, DateTimeOffset OccurredAt, bool Replayed);
public sealed record StockMovementResponse(Guid Id, Guid ArticleId, string Sku, string Name, string WarehouseCode, decimal Quantity, string MovementType, string? Reference, string CreatedBy, DateTimeOffset OccurredAt);
public sealed record CreateSalesOrderRequest(Guid CustomerId, string OrderNumber, IReadOnlyCollection<CreateSalesOrderLineRequest> Lines);
public sealed record CreateSalesOrderLineRequest(Guid ArticleId, decimal Quantity);
public sealed record SalesOrderResponse(Guid Id, string OrderNumber, Guid CustomerId, string Status, decimal NetAmount, IReadOnlyCollection<SalesOrderLineResponse> Lines);
public sealed record SalesOrderLineResponse(Guid ArticleId, string Sku, decimal Quantity, decimal UnitPrice);

public partial class Program;
