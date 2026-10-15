using Erp.BuildingBlocks;
using Erp.Inventory.Domain;
using Erp.MasterData.Domain;
using Erp.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api;

internal static class MasterDataEndpoints
{
    public static RouteGroupBuilder MapMasterDataEndpoints(this RouteGroupBuilder api)
    {
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

        api.MapGet("/customers/{customerId:guid}/addresses", async (
            Guid customerId,
            ErpDbContext db,
            string? role,
            CancellationToken cancellationToken) =>
        {
            if (!await db.Customers.AnyAsync(customer => customer.Id == customerId, cancellationToken))
            {
                return Results.NotFound();
            }

            var query = db.CustomerAddresses.AsNoTracking().Where(link => link.CustomerId == customerId);
            if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<AddressRole>(role, ignoreCase: true, out var parsedRole))
            {
                query = query.Where(link => link.Role == parsedRole);
            }

            var links = await query.ToListAsync(cancellationToken);
            var addressIds = links.Select(link => link.AddressId).Distinct().ToArray();
            var addresses = await db.Addresses.AsNoTracking()
                .Where(address => addressIds.Contains(address.Id))
                .ToDictionaryAsync(address => address.Id, cancellationToken);

            var response = links
                .Where(link => addresses.ContainsKey(link.AddressId))
                .Select(link =>
                {
                    var address = addresses[link.AddressId];
                    return new CustomerAddressResponse(
                        link.Id,
                        link.CustomerId,
                        link.AddressId,
                        link.Role.ToString(),
                        ToAddressResponse(address));
                })
                .ToList();

            return Results.Ok(response);
        });

        api.MapPost("/customers/{customerId:guid}/addresses", async (
            Guid customerId,
            LinkCustomerAddressRequest request,
            ITenantContext tenant,
            ErpDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (!await db.Customers.AnyAsync(customer => customer.Id == customerId, cancellationToken))
            {
                return Results.NotFound(new { error = "Customer does not exist." });
            }

            if (!await db.Addresses.AnyAsync(address => address.Id == request.AddressId, cancellationToken))
            {
                return Results.BadRequest(new { error = "Address does not exist." });
            }

            if (!Enum.TryParse<AddressRole>(request.Role, ignoreCase: true, out var role))
            {
                return Results.BadRequest(new { error = "Role must be Billing or Shipping." });
            }

            var duplicate = await db.CustomerAddresses.AnyAsync(
                link => link.CustomerId == customerId && link.AddressId == request.AddressId && link.Role == role,
                cancellationToken);
            if (duplicate)
            {
                return Results.Conflict(new { error = "Address is already linked with this role." });
            }

            var link = CustomerAddress.Link(tenant.TenantId, customerId, request.AddressId, role);
            db.CustomerAddresses.Add(link);
            await db.SaveChangesAsync(cancellationToken);

            var address = await db.Addresses.AsNoTracking().SingleAsync(item => item.Id == request.AddressId, cancellationToken);
            return Results.Created(
                $"/api/v1/customers/{customerId}/addresses",
                new CustomerAddressResponse(link.Id, link.CustomerId, link.AddressId, link.Role.ToString(), ToAddressResponse(address)));
        });

        api.MapGet("/addresses", async (ErpDbContext db, string? after, int? limit, string? q, CancellationToken cancellationToken) =>
        {
            var pageSize = Math.Clamp(limit ?? 50, 1, 200);
            var query = db.Addresses.AsNoTracking().OrderBy(address => address.Name).ThenBy(address => address.Id).AsQueryable();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(address =>
                    EF.Functions.ILike(address.Name, $"%{term}%")
                    || EF.Functions.ILike(address.City, $"%{term}%")
                    || EF.Functions.ILike(address.PostalCode, $"%{term}%")
                    || EF.Functions.ILike(address.Street, $"%{term}%"));
            }

            if (!string.IsNullOrWhiteSpace(after) && Guid.TryParse(after, out var afterId))
            {
                var cursor = await db.Addresses.AsNoTracking().SingleOrDefaultAsync(address => address.Id == afterId, cancellationToken);
                if (cursor is not null)
                {
                    query = query.Where(address =>
                        string.Compare(address.Name, cursor.Name) > 0
                        || (address.Name == cursor.Name && address.Id.CompareTo(cursor.Id) > 0));
                }
            }

            var addresses = await query.Take(pageSize).ToListAsync(cancellationToken);
            return Results.Ok(addresses.Select(ToAddressResponse).ToList());
        });

        api.MapPost("/addresses", async (CreateAddressRequest request, ITenantContext tenant, ErpDbContext db, CancellationToken cancellationToken) =>
        {
            var address = Address.Create(
                tenant.TenantId,
                request.Name,
                request.Street,
                request.PostalCode,
                request.City,
                request.CountryCode ?? "DE",
                request.AdditionalLine);
            db.Addresses.Add(address);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/addresses/{address.Id}", ToAddressResponse(address));
        });

        api.MapGet("/addresses/{id:guid}", async (Guid id, ErpDbContext db, CancellationToken cancellationToken) =>
        {
            var address = await db.Addresses.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            return address is null ? Results.NotFound() : Results.Ok(ToAddressResponse(address));
        });

        api.MapPut("/addresses/{id:guid}", async (Guid id, UpdateAddressRequest request, ErpDbContext db, CancellationToken cancellationToken) =>
        {
            var address = await db.Addresses.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (address is null) return Results.NotFound();

            address.Update(
                request.Name,
                request.Street,
                request.PostalCode,
                request.City,
                request.CountryCode,
                request.AdditionalLine);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToAddressResponse(address));
        });

        api.MapGet("/articles", async (ErpDbContext db, string? after, int? limit, string? q, CancellationToken cancellationToken) =>
        {
            var pageSize = Math.Clamp(limit ?? 50, 1, 200);
            var query = db.Articles.AsNoTracking().OrderBy(article => article.Sku).AsQueryable();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(article =>
                    EF.Functions.ILike(article.Sku, $"%{term}%")
                    || EF.Functions.ILike(article.Name, $"%{term}%"));
            }

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

        api.MapGet("/articles/{id:guid}", async (Guid id, ErpDbContext db, CancellationToken cancellationToken) =>
        {
            var article = await db.Articles.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            return article is null
                ? Results.NotFound()
                : Results.Ok(new ArticleResponse(article.Id, article.Sku, article.Name, article.SalesPrice));
        });

        api.MapPut("/articles/{id:guid}", async (Guid id, UpdateArticleRequest request, ErpDbContext db, CancellationToken cancellationToken) =>
        {
            var article = await db.Articles.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (article is null) return Results.NotFound();

            article.Update(request.Name, request.SalesPrice);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new ArticleResponse(article.Id, article.Sku, article.Name, article.SalesPrice));
        });

        return api;
    }

    private static AddressResponse ToAddressResponse(Address address) =>
        new(
            address.Id,
            address.Name,
            address.Street,
            address.PostalCode,
            address.City,
            address.CountryCode,
            address.AdditionalLine);
}

public sealed record CreateCustomerRequest(string Number, string Name);
public sealed record CustomerResponse(Guid Id, string Number, string Name);
public sealed record CreateAddressRequest(
    string Name,
    string Street,
    string PostalCode,
    string City,
    string? CountryCode,
    string? AdditionalLine);
public sealed record UpdateAddressRequest(
    string Name,
    string Street,
    string PostalCode,
    string City,
    string CountryCode,
    string? AdditionalLine);
public sealed record AddressResponse(
    Guid Id,
    string Name,
    string Street,
    string PostalCode,
    string City,
    string CountryCode,
    string? AdditionalLine);
public sealed record LinkCustomerAddressRequest(Guid AddressId, string Role);
public sealed record CustomerAddressResponse(
    Guid Id,
    Guid CustomerId,
    Guid AddressId,
    string Role,
    AddressResponse Address);
public sealed record CreateArticleRequest(string Sku, string Name, decimal SalesPrice, decimal OpeningStock, string? WarehouseCode);
public sealed record UpdateArticleRequest(string Name, decimal SalesPrice);
public sealed record ArticleResponse(Guid Id, string Sku, string Name, decimal SalesPrice);
