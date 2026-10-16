using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace Erp.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed partial class RegistrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Registration_mails_only_the_approver_and_creates_no_login()
    {
        var registrant = Registrant();

        var response = await RegisterAsync(registrant.Firmenname, registrant.Email);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var approverMail = Assert.Single(fixture.Mail.Mentioning(registrant.Firmenname));
        Assert.Equal(PostgresFixture.ApproverEmail, approverMail.To);
        Assert.Contains("https://erp.example.test/freigabe#token=", approverMail.Body, StringComparison.Ordinal);
        Assert.Empty(fixture.Mail.To(registrant.Email));
        var number = NumberFrom(approverMail.Subject);
        Assert.True(number >= 1001);
        Assert.Null(fixture.Cognito.Find($"{number}.kunde"));
        Assert.Equal(("Pending", 0), await StateAsync(number));
    }

    [Fact]
    public async Task First_approval_enables_kunde_and_mails_the_credentials()
    {
        var registrant = Registrant();
        var (number, token) = await RegisterOkAsync(registrant);

        var response = await ApproveAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApprovalBody>();
        Assert.Equal(new ApprovalBody(number, false), body);
        var cognitoUser = fixture.Cognito.Find($"{number}.kunde");
        Assert.NotNull(cognitoUser);
        Assert.Equal(1, cognitoUser.PasswordSets);
        var credentials = Assert.Single(fixture.Mail.To(registrant.Email));
        Assert.Contains($"Mandantennummer: {number}", credentials.Body, StringComparison.Ordinal);
        Assert.Contains("User: kunde", credentials.Body, StringComparison.Ordinal);
        Assert.Contains($"Passwort: {cognitoUser.Password}", credentials.Body, StringComparison.Ordinal);
        Assert.Equal(("Active", 1), await StateAsync(number));

        using var api = fixture.OpenSession(cognitoUser.Subject, tenantId: null);
        var session = await api.GetAsync("/api/v1/session");
        Assert.Equal(HttpStatusCode.OK, session.Status);
        using var json = JsonDocument.Parse(session.Body);
        Assert.Equal(number, json.RootElement.GetProperty("mandantNumber").GetInt64());
        Assert.Equal(registrant.Firmenname, json.RootElement.GetProperty("firmenname").GetString());
    }

    [Fact]
    public async Task Second_approval_does_not_rotate_the_password()
    {
        var registrant = Registrant();
        var (number, token) = await RegisterOkAsync(registrant);
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(token)).StatusCode);

        var again = await ApproveAsync(token);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(new ApprovalBody(number, true), await again.Content.ReadFromJsonAsync<ApprovalBody>());
        Assert.Equal(1, fixture.Cognito.Find($"{number}.kunde")?.PasswordSets);
        Assert.Single(fixture.Mail.To(registrant.Email));
    }

    [Fact]
    public async Task Failed_customer_mail_keeps_the_Mandant_pending_until_a_retry_succeeds()
    {
        var registrant = Registrant();
        var (number, token) = await RegisterOkAsync(registrant);
        fixture.Mail.FailFor(registrant.Email);

        var failed = await ApproveAsync(token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal(("Pending", 0), await StateAsync(number));
        var firstPassword = fixture.Cognito.Find($"{number}.kunde")?.Password;

        fixture.Mail.Restore(registrant.Email);
        var retried = await ApproveAsync(token);

        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        var cognitoUser = fixture.Cognito.Find($"{number}.kunde");
        Assert.NotNull(cognitoUser);
        Assert.Equal(2, cognitoUser.PasswordSets);
        Assert.NotEqual(firstPassword, cognitoUser.Password);
        var credentials = Assert.Single(fixture.Mail.To(registrant.Email));
        Assert.Contains($"Passwort: {cognitoUser.Password}", credentials.Body, StringComparison.Ordinal);
        Assert.Equal(("Active", 1), await StateAsync(number));
    }

    [Fact]
    public async Task Expired_link_is_rejected()
    {
        var registrant = Registrant();
        var (number, token) = await RegisterOkAsync(registrant);
        await ExecuteAdminAsync(
            """update erp.mandanten set "ApprovalExpiresAt" = now() - interval '1 minute' where "Number" = @number""",
            number);

        var response = await ApproveAsync(token);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Null(fixture.Cognito.Find($"{number}.kunde"));
        Assert.Equal(("Pending", 0), await StateAsync(number));
    }

    [Fact]
    public async Task Unknown_link_is_not_found()
    {
        var response = await ApproveAsync("not-a-real-token");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("", "info@example.test")]
    [InlineData("Firma GmbH", "")]
    [InlineData("Firma GmbH", "not-an-address")]
    public async Task Invalid_registration_is_a_bad_request(string firmenname, string email)
    {
        var response = await RegisterAsync(firmenname, email);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task One_mailbox_can_register_two_Mandanten()
    {
        var email = $"shared-{Guid.NewGuid():N}@example.test";

        var first = await RegisterOkAsync(new RegistrantData($"Erste {Guid.NewGuid():N}", email));
        var second = await RegisterOkAsync(new RegistrantData($"Zweite {Guid.NewGuid():N}", email));

        Assert.NotEqual(first.Number, second.Number);
    }

    [Fact]
    public async Task Other_api_routes_still_require_a_login()
    {
        using var client = fixture.CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static RegistrantData Registrant()
    {
        var id = Guid.NewGuid().ToString("N");
        return new RegistrantData($"Firma {id}", $"kunde-{id}@example.test");
    }

    private async Task<HttpResponseMessage> RegisterAsync(string firmenname, string email)
    {
        using var client = fixture.CreateAnonymousClient();
        return await client.PostAsJsonAsync("/api/v1/registrations", new { firmenname, email });
    }

    private async Task<(long Number, string Token)> RegisterOkAsync(RegistrantData registrant)
    {
        var response = await RegisterAsync(registrant.Firmenname, registrant.Email);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var mail = Assert.Single(fixture.Mail.Mentioning(registrant.Firmenname));
        return (NumberFrom(mail.Subject), TokenPattern().Match(mail.Body).Groups[1].Value);
    }

    private async Task<HttpResponseMessage> ApproveAsync(string token)
    {
        using var client = fixture.CreateAnonymousClient();
        return await client.PostAsJsonAsync("/api/v1/registrations/approve", new { token });
    }

    private async Task<(string Status, int Memberships)> StateAsync(long number)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select m."Status",
                   (select count(*)::int from erp.tenant_memberships t where t."TenantId" = m."Id")
            from erp.mandanten m
            where m."Number" = @number
            """,
            connection);
        command.Parameters.AddWithValue("number", number);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"Mandant {number} was not stored.");
        return (reader.GetString(0), reader.GetInt32(1));
    }

    private async Task ExecuteAdminAsync(string sql, long number)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("number", number);
        await command.ExecuteNonQueryAsync();
    }

    private static long NumberFrom(string subject) => long.Parse(NumberPattern().Match(subject).Groups[1].Value);

    [GeneratedRegex(@"Neuer Mandant (\d+):")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"#token=(\S+)")]
    private static partial Regex TokenPattern();

    private sealed record RegistrantData(string Firmenname, string Email);

    private sealed record ApprovalBody(long MandantNumber, bool AlreadyApproved);
}
