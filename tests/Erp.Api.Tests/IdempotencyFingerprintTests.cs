using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Erp.Api.Tests;

public sealed class IdempotencyFingerprintTests
{
    [Fact]
    public void Same_receipt_body_has_one_fingerprint()
    {
        var articleId = Guid.NewGuid();
        var first = new CreateStockReceiptRequest(articleId, 3m, "MAIN", "RCP-1");
        var second = new CreateStockReceiptRequest(articleId, 3m, "MAIN", "RCP-1");

        Assert.Equal(Idempotency.FingerprintOf(first), Idempotency.FingerprintOf(second));
    }

    [Fact]
    public void A_different_quantity_changes_the_fingerprint()
    {
        var articleId = Guid.NewGuid();
        var first = new CreateStockReceiptRequest(articleId, 3m, "MAIN", "RCP-1");
        var second = new CreateStockReceiptRequest(articleId, 4m, "MAIN", "RCP-1");

        Assert.NotEqual(Idempotency.FingerprintOf(first), Idempotency.FingerprintOf(second));
    }

    [Fact]
    public void Release_and_cancel_share_the_empty_body_fingerprint()
    {
        Assert.Equal(64, Idempotency.EmptyFingerprint.Length);
        Assert.Equal(Idempotency.EmptyFingerprint, Idempotency.FingerprintOf(new EmptyDocument()));
    }

    [Fact]
    public void Decimal_scale_stays_in_the_fingerprint()
    {
        var articleId = Guid.NewGuid();
        var whole = new CreateStockReceiptRequest(articleId, 1m, "MAIN", "RCP-1");
        var scaled = new CreateStockReceiptRequest(articleId, 1.0m, "MAIN", "RCP-1");

        Assert.NotEqual(Idempotency.FingerprintOf(whole), Idempotency.FingerprintOf(scaled));
    }

    [Fact]
    public void A_reused_key_is_an_unprocessable_problem()
    {
        var result = Assert.IsType<ProblemHttpResult>(Idempotency.Mismatch());

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, result.StatusCode);
        Assert.Equal("The idempotency key was reused with a different request.", result.ProblemDetails.Title);
        Assert.Equal("This Idempotency-Key was already used for a different request.", result.ProblemDetails.Detail);
        Assert.False(result.ProblemDetails.Extensions.ContainsKey("error"));
    }

    private sealed class EmptyDocument;

    [Fact]
    public void A_missing_fingerprint_still_matches_any_body()
    {
        Assert.False(Idempotency.Conflicts(null, "abc"));
        Assert.False(Idempotency.Conflicts(null, null));
    }

    [Fact]
    public void A_stored_fingerprint_rejects_a_different_or_missing_body()
    {
        Assert.False(Idempotency.Conflicts("abc", "abc"));
        Assert.True(Idempotency.Conflicts("abc", "def"));
        Assert.True(Idempotency.Conflicts("abc", null));
        Assert.True(Idempotency.Conflicts(string.Empty, null));
    }
}
