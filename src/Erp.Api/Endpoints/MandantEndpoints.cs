using System.Text.Json;
using Erp.BuildingBlocks;
using Erp.Persistence;
using Erp.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api;

internal static class MandantEndpoints
{
    // mandanten has no row-level security, so every lookup keys on the verified tenant, never on input.
    public static RouteGroupBuilder MapMandantEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/mandant/seller", async (ITenantContext tenant, ErpDbContext db, CancellationToken cancellationToken) =>
        {
            var mandant = await db.Mandanten.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == tenant.TenantId, cancellationToken);
            return mandant is null ? Results.NotFound() : Results.Ok(ToResponse(mandant));
        }).Produces<SellerDetailsResponse>().Produces(StatusCodes.Status404NotFound);

        api.MapPut("/mandant/seller", async (
            UpdateSellerDetailsRequest request,
            ITenantContext tenant,
            HttpContext httpContext,
            ErpDbContext db,
            CancellationToken cancellationToken) =>
        {
            var mandant = await db.Mandanten.SingleOrDefaultAsync(item => item.Id == tenant.TenantId, cancellationToken);
            if (mandant is null) return Results.NotFound();

            mandant.ChangeSellerDetails(
                request.Street,
                request.PostalCode,
                request.City,
                request.CountryCode,
                request.AdditionalLine,
                request.Steuernummer,
                request.UstIdNr);
            var response = ToResponse(mandant);
            db.AuditEntries.Add(AuditEntry.Create(
                tenant.TenantId,
                tenant.UserId,
                "Mandant.SellerDetailsChanged",
                nameof(Mandant),
                mandant.Id,
                JsonSerializer.Serialize(response, JsonSerializerOptions.Web),
                httpContext.TraceIdentifier));
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(response);
        }).Produces<SellerDetailsResponse>().Produces(StatusCodes.Status404NotFound);

        return api;
    }

    private static SellerDetailsResponse ToResponse(Mandant mandant) =>
        new(
            mandant.Number,
            mandant.Firmenname,
            mandant.SellerStreet,
            mandant.SellerPostalCode,
            mandant.SellerCity,
            mandant.SellerCountryCode,
            mandant.SellerAdditionalLine,
            mandant.Steuernummer,
            mandant.UstIdNr,
            mandant.MissingSellerFields());
}

public sealed record UpdateSellerDetailsRequest(
    string? Street,
    string? PostalCode,
    string? City,
    string? CountryCode,
    string? AdditionalLine,
    string? Steuernummer,
    string? UstIdNr);

public sealed record SellerDetailsResponse(
    long MandantNumber,
    string Firmenname,
    string? Street,
    string? PostalCode,
    string? City,
    string? CountryCode,
    string? AdditionalLine,
    string? Steuernummer,
    string? UstIdNr,
    IReadOnlyList<string> MissingFields);
