using Erp.Platform.Domain;

namespace Erp.Domain.Tests;

public sealed class IdempotencyRecordTests
{
    [Fact]
    public void Create_without_a_fingerprint_leaves_it_null()
    {
        var record = IdempotencyRecord.Create(Guid.NewGuid(), "key", "CreateSalesOrder", Guid.NewGuid(), null);

        Assert.Null(record.Fingerprint);
    }

    [Fact]
    public void Create_stores_a_supplied_fingerprint()
    {
        const string fingerprint = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        var record = IdempotencyRecord.Create(Guid.NewGuid(), "key", "StockGoodsReceipt", Guid.NewGuid(), fingerprint);

        Assert.Equal(fingerprint, record.Fingerprint);
    }
}
