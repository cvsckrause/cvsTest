namespace Erp.Platform.Domain;

public enum MandantStatus
{
    Pending,
    Active,
}

public sealed class Mandant
{
    public const int FirmennameMaxLength = 240;
    public const long FirstRegisteredNumber = 1001;

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

        return new MandantUser(mandantId, name, mail);
    }
}
