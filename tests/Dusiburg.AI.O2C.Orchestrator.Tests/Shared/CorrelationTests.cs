using Dusiburg.AI.O2C.Shared.Correlation;

namespace Dusiburg.AI.O2C.Orchestrator.Tests.Shared;

public class CorrelationTests
{
    [Fact]
    public void New_ReturnsValidVersion7Guid()
    {
        var id = CorrelationId.New();

        Assert.True(CorrelationId.IsValid(id));
        Assert.Equal(7, Guid.Parse(id).Version);
    }

    [Fact]
    public void New_ReturnsDistinctIds()
    {
        Assert.NotEqual(CorrelationId.New(), CorrelationId.New());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("contains space")]
    [InlineData("line\r\nbreak")]
    [InlineData("<script>")]
    public void IsValid_RejectsUnsafeValues(string? value)
    {
        Assert.False(CorrelationId.IsValid(value));
    }

    [Fact]
    public void IsValid_RejectsTooLongValue()
    {
        Assert.False(CorrelationId.IsValid(new string('a', CorrelationId.MaxLength + 1)));
    }

    [Fact]
    public void Begin_SetsCurrentAndRestoresPreviousOnDispose()
    {
        var context = new AsyncLocalCorrelationContext();

        using (context.Begin("outer"))
        {
            using (context.Begin("inner"))
            {
                Assert.Equal("inner", context.Current);
            }

            Assert.Equal("outer", context.Current);
        }

        Assert.Null(context.Current);
    }

    [Fact]
    public void Begin_InvalidId_Throws()
    {
        var context = new AsyncLocalCorrelationContext();

        Assert.Throws<ArgumentException>(() => context.Begin("not valid"));
    }
}
