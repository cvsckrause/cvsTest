namespace Erp.Api.Registration;

public sealed record OutgoingMail(string To, string Subject, string Body);

public interface IMailSender
{
    Task SendAsync(OutgoingMail mail, CancellationToken cancellationToken);
}

public interface ICognitoUserAdmin
{
    // Creates the user without an invitation mail, or resets the password of an existing user,
    // and returns the Cognito subject.
    Task<string> SetPermanentPasswordAsync(string username, string password, CancellationToken cancellationToken);
}

public sealed class RegistrationUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

internal sealed class DevelopmentMailSender(ILogger<DevelopmentMailSender> logger) : IMailSender
{
    public Task SendAsync(OutgoingMail mail, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Development mail to {To}: {Subject}{NewLine}{Body}",
            mail.To,
            mail.Subject,
            Environment.NewLine,
            mail.Body);
        return Task.CompletedTask;
    }
}

internal sealed class UnconfiguredMailSender : IMailSender
{
    public Task SendAsync(OutgoingMail mail, CancellationToken cancellationToken) =>
        throw new RegistrationUnavailableException("No mail transport is configured.");
}

internal sealed class LocalCognitoUserAdmin : ICognitoUserAdmin
{
    public Task<string> SetPermanentPasswordAsync(string username, string password, CancellationToken cancellationToken) =>
        Task.FromResult($"local-{username}");
}

internal sealed class UnconfiguredCognitoUserAdmin : ICognitoUserAdmin
{
    public Task<string> SetPermanentPasswordAsync(string username, string password, CancellationToken cancellationToken) =>
        throw new RegistrationUnavailableException("Cognito user administration is not configured.");
}
