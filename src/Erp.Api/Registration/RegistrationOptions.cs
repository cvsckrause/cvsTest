namespace Erp.Api.Registration;

public sealed class RegistrationOptions
{
    public const string Section = "Registration";
    public const string RateLimitPolicy = "registration";

    public string ApproverEmail { get; set; } = string.Empty;
    public string PublicBaseUrl { get; set; } = string.Empty;
    public int ApprovalLifetimeDays { get; set; } = 7;
}
