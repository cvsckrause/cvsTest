using System.Collections.Concurrent;
using Erp.Api.Registration;

namespace Erp.Integration.Tests;

internal sealed class CapturingMailSender : IMailSender
{
    private readonly ConcurrentQueue<OutgoingMail> sent = new();
    private readonly ConcurrentDictionary<string, bool> failing = new(StringComparer.OrdinalIgnoreCase);

    public void FailFor(string address) => failing[address] = true;

    public void Restore(string address) => failing.TryRemove(address, out _);

    public IReadOnlyList<OutgoingMail> To(string address) =>
        sent.Where(mail => string.Equals(mail.To, address, StringComparison.OrdinalIgnoreCase)).ToArray();

    public IReadOnlyList<OutgoingMail> Mentioning(string text) =>
        sent.Where(mail => mail.Body.Contains(text, StringComparison.Ordinal)).ToArray();

    public Task SendAsync(OutgoingMail mail, CancellationToken cancellationToken)
    {
        if (failing.ContainsKey(mail.To))
        {
            throw new IOException($"Mail to {mail.To} bounced.");
        }

        sent.Enqueue(mail);
        return Task.CompletedTask;
    }
}

internal sealed class FakeCognitoUserAdmin : ICognitoUserAdmin
{
    private readonly ConcurrentDictionary<string, CognitoTestUser> users = new(StringComparer.Ordinal);

    public CognitoTestUser? Find(string username) => users.TryGetValue(username, out var user) ? user : null;

    public Task<string> SetPermanentPasswordAsync(string username, string password, CancellationToken cancellationToken)
    {
        var user = users.AddOrUpdate(
            username,
            name => new CognitoTestUser($"sub-{name}", password, 1),
            (_, existing) => existing with { Password = password, PasswordSets = existing.PasswordSets + 1 });
        return Task.FromResult(user.Subject);
    }
}

internal sealed record CognitoTestUser(string Subject, string Password, int PasswordSets);
