using Erp.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Erp.Api.Registration;

internal enum ApprovalResult
{
    Approved,
    AlreadyApproved,
    Unknown,
    Expired,
}

internal readonly record struct ApprovalOutcome(ApprovalResult Result, long MandantNumber = 0);

internal sealed class MandantRegistration(
    ErpConnection connection,
    ICognitoUserAdmin cognito,
    IMailSender mail,
    IOptions<RegistrationOptions> options,
    TimeProvider clock,
    ILogger<MandantRegistration> logger)
{
    private readonly RegistrationOptions settings = options.Value;

    public async Task RegisterAsync(string firmenname, string email, CancellationToken cancellationToken)
    {
        var token = ApprovalToken.Create();
        var now = clock.GetUtcNow();
        var mandant = Mandant.Register(firmenname, token.Hash, now.AddDays(settings.ApprovalLifetimeDays), now);
        var user = MandantUser.Create(mandant.Id, MandantUser.FirstUserName, email);

        // The row is committed only once the approver mail is out, so a pending Mandant always has a link.
        await using var db = connection.OpenRegistry("registration");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Mandanten.Add(mandant);
        db.MandantUsers.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        await SendAsync(ApproverMail(mandant, user, token.Value), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Mandant {MandantNumber} was registered and awaits approval.", mandant.Number);
    }

    public async Task<ApprovalOutcome> ApproveAsync(string? token, CancellationToken cancellationToken)
    {
        if (!ApprovalToken.TryHash(token, out var hash))
        {
            return new ApprovalOutcome(ApprovalResult.Unknown);
        }

        await using var db = connection.OpenRegistry("registration");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var rows = await db.Mandanten
            .FromSql($"""SELECT * FROM erp.mandanten WHERE "ApprovalTokenHash" = {hash} FOR UPDATE""")
            .ToListAsync(cancellationToken);
        var mandant = rows.SingleOrDefault();
        if (mandant is null)
        {
            return new ApprovalOutcome(ApprovalResult.Unknown);
        }

        if (mandant.Status == MandantStatus.Active)
        {
            return new ApprovalOutcome(ApprovalResult.AlreadyApproved, mandant.Number);
        }

        var now = clock.GetUtcNow();
        if (mandant.ApprovalExpired(now))
        {
            return new ApprovalOutcome(ApprovalResult.Expired, mandant.Number);
        }

        var user = await db.MandantUsers.SingleAsync(
            item => item.MandantId == mandant.Id && item.UserName == MandantUser.FirstUserName,
            cancellationToken);
        var loginName = user.LoginName(mandant.Number);
        var password = InitialPassword.Create();
        string subject;
        try
        {
            subject = await cognito.SetPermanentPasswordAsync(loginName, password, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not RegistrationUnavailableException)
        {
            throw new RegistrationUnavailableException("The Cognito user could not be prepared.", exception);
        }

        user.LinkCognito(subject);
        var hasMembership = await db.TenantMemberships.AnyAsync(
            item => item.UserId == subject && item.TenantId == mandant.Id,
            cancellationToken);
        if (!hasMembership)
        {
            db.TenantMemberships.Add(TenantMembership.Grant(subject, mandant.Id, isDefault: true));
        }

        mandant.Activate(now);
        await db.SaveChangesAsync(cancellationToken);

        // A failed customer mail rolls back the activation; the next click sets a new password.
        await SendAsync(CredentialsMail(mandant, user, password), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Mandant {MandantNumber} was approved.", mandant.Number);
        return new ApprovalOutcome(ApprovalResult.Approved, mandant.Number);
    }

    private async Task SendAsync(OutgoingMail message, CancellationToken cancellationToken)
    {
        try
        {
            await mail.SendAsync(message, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not RegistrationUnavailableException)
        {
            throw new RegistrationUnavailableException("The mail could not be sent.", exception);
        }
    }

    private OutgoingMail ApproverMail(Mandant mandant, MandantUser user, string token)
    {
        if (string.IsNullOrWhiteSpace(settings.ApproverEmail))
        {
            throw new RegistrationUnavailableException("Registration:ApproverEmail is not configured.");
        }

        var link = $"{settings.PublicBaseUrl.TrimEnd('/')}/freigabe#token={token}";
        return new OutgoingMail(
            settings.ApproverEmail,
            $"Neuer Mandant {mandant.Number}: {mandant.Firmenname}",
            $"""
            Ein neuer Mandant wurde angefragt.

            Mandantennummer: {mandant.Number}
            Firmenname: {mandant.Firmenname}
            E-Mail: {user.Email}

            Freigeben: {link}

            Der Link ist bis {mandant.ApprovalExpiresAt:dd.MM.yyyy HH:mm} UTC gültig.
            """);
    }

    private static OutgoingMail CredentialsMail(Mandant mandant, MandantUser user, string password) =>
        new(
            user.Email,
            $"Ihr Zugang zu Northstar ERP (Mandant {mandant.Number})",
            $"""
            Ihr Mandant {mandant.Firmenname} wurde freigegeben.

            Mandantennummer: {mandant.Number}
            User: {user.UserName}
            Passwort: {password}

            Melden Sie sich mit Mandantennummer, User und Passwort an.
            """);
}
