using Dusiburg.AI.O2C.Shared.Correlation;

namespace Dusiburg.AI.O2C.Orchestrator.Tests.Shared;

public class CorrelationTests
{
    [Test]
    public void New_ReturnsValidVersion7Guid()
    {
        var id = CorrelationId.New();

        Assert.That(CorrelationId.IsValid(id), Is.True);
        Assert.That(Guid.Parse(id).Version, Is.EqualTo(7));
    }

    [Test]
    public void New_ReturnsDistinctIds()
    {
        var first = CorrelationId.New();
        var second = CorrelationId.New();

        Assert.That(second, Is.Not.EqualTo(first));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("contains space")]
    [TestCase("line\r\nbreak")]
    [TestCase("<script>")]
    public void IsValid_RejectsUnsafeValues(string? value)
    {
        Assert.That(CorrelationId.IsValid(value), Is.False);
    }

    [Test]
    public void IsValid_RejectsTooLongValue()
    {
        Assert.That(CorrelationId.IsValid(new string('a', CorrelationId.MaxLength + 1)), Is.False);
    }

    [Test]
    public void Begin_SetsCurrentAndRestoresPreviousOnDispose()
    {
        var context = new AsyncLocalCorrelationContext();

        using (context.Begin("outer"))
        {
            using (context.Begin("inner"))
            {
                Assert.That(context.Current, Is.EqualTo("inner"));
            }

            Assert.That(context.Current, Is.EqualTo("outer"));
        }

        Assert.That(context.Current, Is.Null);
    }

    [Test]
    public void Begin_InvalidId_Throws()
    {
        var context = new AsyncLocalCorrelationContext();

        Assert.That(() => context.Begin("not valid"), Throws.TypeOf<ArgumentException>());
    }
}
