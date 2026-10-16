using Erp.Api.Registration;

namespace Erp.Api;

internal static class RegistrationEndpoints
{
    public static RouteGroupBuilder MapRegistrationEndpoints(this RouteGroupBuilder api)
    {
        var registrations = api.MapGroup("/registrations")
            .AllowAnonymous()
            .RequireRateLimiting(RegistrationOptions.RateLimitPolicy);

        registrations.MapPost("", async (
            RegistrationRequest request,
            MandantRegistration registration,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await registration.RegisterAsync(request.Firmenname, request.Email, cancellationToken);
            }
            catch (RegistrationUnavailableException)
            {
                return Unavailable();
            }

            return Results.Accepted((string?)null, new RegistrationAcceptedResponse("pending"));
        })
        .Produces<RegistrationAcceptedResponse>(StatusCodes.Status202Accepted);

        registrations.MapPost("/approve", async (
            ApprovalRequest request,
            MandantRegistration registration,
            CancellationToken cancellationToken) =>
        {
            ApprovalOutcome outcome;
            try
            {
                outcome = await registration.ApproveAsync(request.Token, cancellationToken);
            }
            catch (RegistrationUnavailableException)
            {
                return Unavailable();
            }

            return outcome.Result switch
            {
                ApprovalResult.Approved => Results.Ok(new ApprovalResponse(outcome.MandantNumber, AlreadyApproved: false)),
                ApprovalResult.AlreadyApproved => Results.Ok(new ApprovalResponse(outcome.MandantNumber, AlreadyApproved: true)),
                ApprovalResult.Expired => Results.Problem(
                    title: "The approval link has expired.",
                    detail: "Der Freigabelink ist abgelaufen.",
                    statusCode: StatusCodes.Status410Gone),
                _ => Results.Problem(
                    title: "The resource was not found.",
                    detail: "Der Freigabelink ist ungültig.",
                    statusCode: StatusCodes.Status404NotFound),
            };
        })
        .Produces<ApprovalResponse>();

        return api;
    }

    private static IResult Unavailable() =>
        Results.Problem(
            title: "Registration is temporarily unavailable.",
            detail: "Die Anfrage konnte gerade nicht abgeschlossen werden. Bitte später erneut versuchen.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
}

public sealed record RegistrationRequest(string Firmenname, string Email);

public sealed record RegistrationAcceptedResponse(string Status);

public sealed record ApprovalRequest(string Token);

public sealed record ApprovalResponse(long MandantNumber, bool AlreadyApproved);
