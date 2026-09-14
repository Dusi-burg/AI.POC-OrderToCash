using Dusiburg.AI.O2C.Shared.Idempotency;

namespace Dusiburg.AI.O2C.Orchestrator.Tests.Shared;

public class IdempotencyKeyTests
{
    [Test]
    public void From_SameInput_ReturnsSameKey()
    {
        var first = IdempotencyKey.From("D-1001", 3);
        var second = IdempotencyKey.From("D-1001", 3);

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void From_ReturnsReadableKey()
    {
        Assert.That(IdempotencyKey.From("D-1001", 3), Is.EqualTo("o2c-D-1001-r3"));
    }

    [Test]
    public void From_DifferentRevision_ReturnsDifferentKey()
    {
        var revision3 = IdempotencyKey.From("D-1001", 3);
        var revision4 = IdempotencyKey.From("D-1001", 4);

        Assert.That(revision4, Is.Not.EqualTo(revision3));
    }

    [Test]
    public void From_DifferentDeal_ReturnsDifferentKey()
    {
        var deal1001 = IdempotencyKey.From("D-1001", 3);
        var deal1002 = IdempotencyKey.From("D-1002", 3);

        Assert.That(deal1002, Is.Not.EqualTo(deal1001));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void From_MissingDealId_Throws(string? dealId)
    {
        Assert.That(() => IdempotencyKey.From(dealId!, 1), Throws.InstanceOf<ArgumentException>());
    }

    [TestCase("D 1001")]
    [TestCase("D/1001")]
    [TestCase("D-1001\n")]
    [TestCase("D-1001è")]
    public void From_UnsafeDealId_Throws(string dealId)
    {
        Assert.That(() => IdempotencyKey.From(dealId, 1), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void From_NegativeRevision_Throws()
    {
        Assert.That(() => IdempotencyKey.From("D-1001", -1), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void From_KeyAtMaxLength_IsAccepted()
    {
        // "o2c-" + dealId + "-r1" = 100 caratteri
        var dealId = new string('D', IdempotencyKey.MaxLength - 7);

        Assert.That(IdempotencyKey.From(dealId, 1), Has.Length.EqualTo(IdempotencyKey.MaxLength));
    }

    [Test]
    public void From_KeyOverMaxLength_Throws()
    {
        var dealId = new string('D', IdempotencyKey.MaxLength - 6);

        Assert.That(() => IdempotencyKey.From(dealId, 1), Throws.TypeOf<ArgumentException>());
    }
}
