using O2C.Shared.Idempotency;

namespace O2C.Orchestrator.Tests.Shared;

public class IdempotencyKeyTests
{
    [Fact]
    public void From_SameInput_ReturnsSameKey()
    {
        Assert.Equal(IdempotencyKey.From("D-1001", 3), IdempotencyKey.From("D-1001", 3));
    }

    [Fact]
    public void From_ReturnsReadableKey()
    {
        Assert.Equal("o2c-D-1001-r3", IdempotencyKey.From("D-1001", 3));
    }

    [Fact]
    public void From_DifferentRevision_ReturnsDifferentKey()
    {
        Assert.NotEqual(IdempotencyKey.From("D-1001", 3), IdempotencyKey.From("D-1001", 4));
    }

    [Fact]
    public void From_DifferentDeal_ReturnsDifferentKey()
    {
        Assert.NotEqual(IdempotencyKey.From("D-1001", 3), IdempotencyKey.From("D-1002", 3));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void From_MissingDealId_Throws(string? dealId)
    {
        Assert.ThrowsAny<ArgumentException>(() => IdempotencyKey.From(dealId!, 1));
    }

    [Theory]
    [InlineData("D 1001")]
    [InlineData("D/1001")]
    [InlineData("D-1001\n")]
    [InlineData("D-1001è")]
    public void From_UnsafeDealId_Throws(string dealId)
    {
        Assert.Throws<ArgumentException>(() => IdempotencyKey.From(dealId, 1));
    }

    [Fact]
    public void From_NegativeRevision_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IdempotencyKey.From("D-1001", -1));
    }

    [Fact]
    public void From_KeyAtMaxLength_IsAccepted()
    {
        // "o2c-" + dealId + "-r1" = 100 caratteri
        var dealId = new string('D', IdempotencyKey.MaxLength - 7);

        Assert.Equal(IdempotencyKey.MaxLength, IdempotencyKey.From(dealId, 1).Length);
    }

    [Fact]
    public void From_KeyOverMaxLength_Throws()
    {
        var dealId = new string('D', IdempotencyKey.MaxLength - 6);

        Assert.Throws<ArgumentException>(() => IdempotencyKey.From(dealId, 1));
    }
}
