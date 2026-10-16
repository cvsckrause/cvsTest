using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;

namespace Erp.Api.Registration;

internal sealed class AwsCognitoUserAdmin(IAmazonCognitoIdentityProvider cognito, string userPoolId) : ICognitoUserAdmin
{
    public async Task<string> SetPermanentPasswordAsync(string username, string password, CancellationToken cancellationToken)
    {
        try
        {
            // No email attribute: the mailbox is contact data and may back several logins.
            await cognito.AdminCreateUserAsync(
                new AdminCreateUserRequest
                {
                    UserPoolId = userPoolId,
                    Username = username,
                    MessageAction = MessageActionType.SUPPRESS,
                    TemporaryPassword = password,
                },
                cancellationToken);
        }
        catch (UsernameExistsException)
        {
        }

        await cognito.AdminSetUserPasswordAsync(
            new AdminSetUserPasswordRequest
            {
                UserPoolId = userPoolId,
                Username = username,
                Password = password,
                Permanent = true,
            },
            cancellationToken);

        var user = await cognito.AdminGetUserAsync(
            new AdminGetUserRequest { UserPoolId = userPoolId, Username = username },
            cancellationToken);
        return user.UserAttributes?.FirstOrDefault(attribute => attribute.Name == "sub")?.Value
            ?? throw new RegistrationUnavailableException($"Cognito returned no subject for {username}.");
    }
}

internal sealed class SesMailSender(IAmazonSimpleEmailServiceV2 ses, string fromAddress) : IMailSender
{
    public async Task SendAsync(OutgoingMail mail, CancellationToken cancellationToken)
    {
        await ses.SendEmailAsync(
            new SendEmailRequest
            {
                FromEmailAddress = fromAddress,
                Destination = new Destination { ToAddresses = [mail.To] },
                Content = new EmailContent
                {
                    Simple = new Message
                    {
                        Subject = new Content { Data = mail.Subject, Charset = "UTF-8" },
                        Body = new Body { Text = new Content { Data = mail.Body, Charset = "UTF-8" } },
                    },
                },
            },
            cancellationToken);
    }
}
