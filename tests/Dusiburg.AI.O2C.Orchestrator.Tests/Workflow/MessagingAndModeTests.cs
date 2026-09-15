using System.Text;
using Dusiburg.AI.O2C.Orchestrator.Agents;
using Dusiburg.AI.O2C.Orchestrator.Messaging;
using Dusiburg.AI.O2C.Shared.Contracts.Messaging;
using Microsoft.Extensions.Configuration;

namespace Dusiburg.AI.O2C.Orchestrator.Tests.Workflow;

/// <summary>Header dei messaggi RabbitMQ, identità dell'evento e modalità dell'agente (D47).</summary>
public class MessagingAndModeTests
{
    [Test]
    public void ReadText_ByteArrayOrStringHeader_ReturnsText()
    {
        //SETUP
        var headers = new Dictionary<string, object?> { ["a"] = Encoding.UTF8.GetBytes("corr-1"), ["b"] = "corr-2" };

        //SUT
        Assert.That((DealClosedWonConsumer.ReadText(headers, "a"), DealClosedWonConsumer.ReadText(headers, "b"), DealClosedWonConsumer.ReadText(headers, "c")),
            Is.EqualTo(("corr-1", "corr-2", (string?)null)));
    }

    [Test]
    public void ReadInt_RetryHeader_ReturnsCountOrZero()
    {
        //SETUP
        var headers = new Dictionary<string, object?> { [DealEventsTopology.RetryCountHeader] = 2 };

        //SUT
        Assert.That((DealClosedWonConsumer.ReadInt(headers, DealEventsTopology.RetryCountHeader), DealClosedWonConsumer.ReadInt(null, DealEventsTopology.RetryCountHeader)),
            Is.EqualTo((2, 0)));
    }

    [Test]
    public void DealClosedWon_MessageId_IsDealAndRevision()
    {
        //SUT
        Assert.That(new DealClosedWon("D-1001", 3, DateTimeOffset.UnixEpoch).MessageId, Is.EqualTo("D-1001:3"));
    }

    [TestCase(null, AgentModes.Multi)]
    [TestCase("single", AgentModes.Single)]
    [TestCase("MULTI", AgentModes.Multi)]
    public void AgentMode_FromConfiguration(string? value, string expected)
    {
        //SETUP
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [AgentModes.Setting] = value }).Build();

        //SUT
        Assert.That(AgentModes.FromConfiguration(configuration), Is.EqualTo(expected));
    }

    [Test]
    public void AgentMode_Unknown_Throws()
    {
        //SETUP
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [AgentModes.Setting] = "handoff" }).Build();

        //SUT
        Assert.That(() => AgentModes.FromConfiguration(configuration), Throws.InvalidOperationException);
    }
}
