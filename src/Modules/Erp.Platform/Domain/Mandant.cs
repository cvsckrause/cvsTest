using System.Text.RegularExpressions;

namespace Erp.Platform.Domain;

public enum MandantStatus
{
    Pending,
    Active,
}

public sealed partial class Mandant
{
    public const int FirmennameMaxLength = 240;
    public const long FirstRegisteredNumber = 1001;
    public const int StreetMaxLength = 240;
    public const int PostalCodeMaxLength = 20;
    public const int CityMaxLength = 120;
    public const int SteuernummerMaxLength = 40;
    public const int UstIdNrMaxLength = 14;

    private Mandant()
    {
    }

    private Mandant(Guid id, long number, string firmenname, MandantStatus status, DateTimeOffset now)
    {
        Id = id;
        Number = number;
        Firmenname = firmenname;
        Status = status;
        CreatedAt = now;
        ActivatedAt = status == MandantStatus.Active ? now : null;
    }

    public Guid Id { get; private set; }
    public long Number { get; private set; }
    public string Firmenname { get; private set; } = string.Empty;
    public MandantStatus Status { get; private set; }
    public string? ApprovalTokenHash { get; private set; }
    public DateTimeOffset? ApprovalExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ActivatedAt { get; private set; }
    public string? SellerStreet { get; private set; }
    public string? SellerPostalCode { get; private set; }
    public string? SellerCity { get; private set; }
    public string? SellerCountryCode { get; private set; }
    public string? SellerAdditionalLine { get; private set; }
    public string? Steuernummer { get; private set; }
    public string? UstIdNr { get; private set; }

    public string Label => $"Mandant {Number} · {Firmenname}";

    // Seeded Mandanten keep numbers below the registration sequence, so they never collide with it.
    public static Mandant Seed(Guid id, long number, string firmenname, DateTimeOffset now)
    {
        if (id == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(id));
        if (number is <= 0 or >= FirstRegisteredNumber)
        {
            throw new ArgumentOutOfRangeException(
                nameof(number),
                $"Seeded Mandant numbers must be between 1 and {FirstRegisteredNumber - 1}.");
        }

        return new Mandant(id, number, RequireFirmenname(firmenname), MandantStatus.Active, now);
    }

    // The number comes from erp.mandant_number_seq when the row is inserted.
    public static Mandant Register(string firmenname, string approvalTokenHash, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(approvalTokenHash))
        {
            throw new ArgumentException("Approval token is required.", nameof(approvalTokenHash));
        }

        if (expiresAt <= now) throw new ArgumentException("Approval must expire in the future.", nameof(expiresAt));
        return new Mandant(Guid.NewGuid(), 0, RequireFirmenname(firmenname), MandantStatus.Pending, now)
        {
            ApprovalTokenHash = approvalTokenHash,
            ApprovalExpiresAt = expiresAt,
        };
    }

    public bool ApprovalExpired(DateTimeOffset now) =>
        Status == MandantStatus.Pending && ApprovalExpiresAt is DateTimeOffset expiresAt && expiresAt <= now;

    public void Activate(DateTimeOffset now)
    {
        if (Status == MandantStatus.Active) return;
        if (ApprovalExpired(now)) throw new InvalidOperationException("The approval link has expired.");
        Status = MandantStatus.Active;
        ActivatedAt = now;
    }

    public void ChangeSellerDetails(
        string? street,
        string? postalCode,
        string? city,
        string? countryCode,
        string? additionalLine,
        string? steuernummer,
        string? ustIdNr)
    {
        var nextStreet = Require(street, StreetMaxLength, "Street", nameof(street));
        var nextPostalCode = Require(postalCode, PostalCodeMaxLength, "Postal code", nameof(postalCode));
        var nextCity = Require(city, CityMaxLength, "City", nameof(city));
        var nextCountry = countryCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (nextCountry.Length != 2 || !nextCountry.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException("Country code must be a 2-letter ISO code.", nameof(countryCode));
        }

        var nextAdditional = Optional(additionalLine, StreetMaxLength, "Additional line", nameof(additionalLine));
        var nextSteuernummer = Optional(steuernummer, SteuernummerMaxLength, "Steuernummer", nameof(steuernummer));
        var nextUstIdNr = NormalizeUstIdNr(ustIdNr);
        if (nextSteuernummer is null && nextUstIdNr is null)
        {
            throw new ArgumentException("Steuernummer or USt-IdNr is required.", nameof(steuernummer));
        }

        SellerStreet = nextStreet;
        SellerPostalCode = nextPostalCode;
        SellerCity = nextCity;
        SellerCountryCode = nextCountry;
        SellerAdditionalLine = nextAdditional;
        Steuernummer = nextSteuernummer;
        UstIdNr = nextUstIdNr;
    }

    public IReadOnlyList<string> MissingSellerFields()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(SellerStreet)) missing.Add("street");
        if (string.IsNullOrWhiteSpace(SellerPostalCode)) missing.Add("postalCode");
        if (string.IsNullOrWhiteSpace(SellerCity)) missing.Add("city");
        if (string.IsNullOrWhiteSpace(SellerCountryCode)) missing.Add("countryCode");
        if (string.IsNullOrWhiteSpace(Steuernummer) && string.IsNullOrWhiteSpace(UstIdNr)) missing.Add("steuernummerOrUstIdNr");
        return missing;
    }

    private static string? NormalizeUstIdNr(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = string.Concat(value.Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant();
        if (!UstIdNrPattern().IsMatch(normalized))
        {
            throw new ArgumentException(
                "USt-IdNr must be a 2-letter country prefix followed by 2 to 12 letters or digits.",
                "ustIdNr");
        }

        return normalized;
    }

    private static string Require(string? value, int maxLength, string label, string parameter) =>
        Optional(value, maxLength, label, parameter) ?? throw new ArgumentException($"{label} is required.", parameter);

    private static string? Optional(string? value, int maxLength, string label, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"{label} must be at most {maxLength} characters.", parameter);
        }

        return trimmed;
    }

    [GeneratedRegex("^[A-Z]{2}[A-Z0-9]{2,12}$")]
    private static partial Regex UstIdNrPattern();

    public static string RequireFirmenname(string firmenname)
    {
        var trimmed = firmenname?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) throw new ArgumentException("Firmenname is required.", nameof(firmenname));
        if (trimmed.Length > FirmennameMaxLength)
        {
            throw new ArgumentException(
                $"Firmenname must be at most {FirmennameMaxLength} characters.",
                nameof(firmenname));
        }

        return trimmed;
    }
}

public sealed class MandantUser
{
    public const string FirstUserName = "kunde";
    public const int EmailMaxLength = 320;

    private MandantUser()
    {
    }

    private MandantUser(Guid mandantId, string userName, string email)
    {
        Id = Guid.NewGuid();
        MandantId = mandantId;
        UserName = userName;
        Email = email;
    }

    public Guid Id { get; private set; }
    public Guid MandantId { get; private set; }
    public string UserName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string? CognitoSub { get; private set; }

    public string LoginName(long mandantNumber) => $"{mandantNumber}.{UserName}";

    public void LinkCognito(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) throw new ArgumentException("Cognito subject is required.", nameof(subject));
        CognitoSub = subject.Trim();
    }

    public static MandantUser Create(Guid mandantId, string userName, string email)
    {
        if (mandantId == Guid.Empty) throw new ArgumentException("Mandant is required.", nameof(mandantId));
        var name = userName?.Trim().ToLowerInvariant() ?? string.Empty;
        if (name.Length == 0) throw new ArgumentException("User is required.", nameof(userName));
        var mail = email?.Trim() ?? string.Empty;
        if (mail.Length == 0) throw new ArgumentException("Email is required.", nameof(email));
        if (mail.Length > EmailMaxLength)
        {
            throw new ArgumentException($"Email must be at most {EmailMaxLength} characters.", nameof(email));
        }

        if (!System.Net.Mail.MailAddress.TryCreate(mail, out var parsed) || parsed.Address != mail)
        {
            throw new ArgumentException("Email is not a valid address.", nameof(email));
        }

        return new MandantUser(mandantId, name, mail);
    }
}
