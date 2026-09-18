using Dusiburg.AI.O2C.Orchestrator.Agents;

namespace Dusiburg.AI.O2C.Orchestrator.Tests.Agents;

/// <summary>
/// Specifiche degli agenti come risorse dell'assembly (D63). Con il testo fuori dal codice il compilatore non fa più
/// da rete: questi test prendono il suo posto e fanno fallire la build se una specifica è incompleta o malformata.
/// </summary>
public class AgentSpecTests
{
    private static readonly string[] WorkflowAgentNames = ["IntakeAgent", "FulfillmentAgent", "OrderAgent"];

    [Test]
    public void LoadAll_FindsEverySpecIncludedInTheAssembly()
    {
        //SUT
        IReadOnlyList<AgentSpec> specs = AgentSpec.LoadAll();

        Assert.That(specs.Select(s => s.Name), Is.EquivalentTo(new[] { "IntakeAgent", "FulfillmentAgent", "OrderAgent", "SingleOrderAgent" }));
        Assert.That(specs.Select(s => s.Description), Has.All.Not.Empty, "ogni specifica ha la citazione sotto il titolo");
        Assert.That(specs.Select(s => s.Instructions), Has.All.Not.Empty);
    }

    [TestCaseSource(nameof(WorkflowAgentNames))]
    public void Load_GivesTheAgentItsNameDescriptionAndInstructions(string agentName)
    {
        //SUT
        AgentSpec spec = AgentSpec.Load(agentName);

        Assert.That(spec.Name, Is.EqualTo(agentName), "il titolo del file è il nome dell'agente");
        Assert.That(spec.Instructions, Does.StartWith($"You are {agentName} in an order-to-cash workflow."));
        Assert.That(spec.Instructions, Does.Not.Contain("\r"), "i fine riga sono normalizzati come nei letterali grezzi");
    }

    [Test]
    public void WorkflowAgents_TakeTheirTextFromTheSpecs()
    {
        //SUT
        IReadOnlyList<AgentDefinition> agents = WorkflowAgents.All;

        Assert.That(agents.Select(a => a.Name), Is.EqualTo(WorkflowAgentNames), "nomi e ordine della catena");
        Assert.That(agents.Select(a => a.Instructions), Is.EqualTo(WorkflowAgentNames.Select(n => AgentSpec.Load(n).Instructions)));
        Assert.That(agents.Select(a => a.Description), Is.EqualTo(WorkflowAgentNames.Select(n => AgentSpec.Load(n).Description)));
    }

    [Test]
    public void Handoff_IsOnlyOnTheAgentsThatPassTheirTurn()
    {
        //SUT
        AgentSpec intake = AgentSpec.Load("IntakeAgent");
        AgentSpec fulfillment = AgentSpec.Load("FulfillmentAgent");
        AgentSpec order = AgentSpec.Load("OrderAgent");

        Assert.That(intake.Handoff, Is.EqualTo(WorkflowAgents.IntakeHandoffCondition));
        Assert.That(fulfillment.Handoff, Is.EqualTo(WorkflowAgents.FulfillmentHandoffCondition));
        Assert.That(order.Handoff, Is.Null, "OrderAgent è terminale");
        Assert.That(intake.Handoff, Does.StartWith("Use only after"), "condizione d'uso, non fatto già avvenuto (D62)");
        Assert.That(fulfillment.Handoff, Does.StartWith("Use only after"));
    }

    [Test]
    public void Notes_StayOutOfEveryTextSentToTheModel()
    {
        //SETUP
        IReadOnlyList<AgentSpec> specs = AgentSpec.LoadAll();

        //SUT
        IEnumerable<string> sentToTheModel = specs.SelectMany(s => new[] { s.Description, s.Instructions, s.Handoff ?? string.Empty });

        Assert.That(sentToTheModel, Has.None.Contains(AgentSpec.NotesSection));
        Assert.That(sentToTheModel, Has.None.Contains("non inviate al modello"));
    }

    [Test]
    public void Section_ExplainsItselfWhenTheSectionIsMissing()
    {
        //SETUP
        AgentSpec order = AgentSpec.Load("OrderAgent");

        //SUT
        var exception = Assert.Throws<InvalidOperationException>(() => order.Section(AgentSpec.HandoffSection));

        Assert.That(exception!.Message, Does.Contain("OrderAgent").And.Contain(AgentSpec.HandoffSection));
    }

    [Test]
    public void Load_ExplainsItselfWhenTheSpecIsNotIncludedAsAResource()
    {
        //SUT
        var exception = Assert.Throws<InvalidOperationException>(() => AgentSpec.Load("NoSuchAgent"));

        Assert.That(exception!.Message, Does.Contain("NoSuchAgent.agent.md"));
    }

    [TestCase("Manca il titolo", "> Descrizione.\n\n## Instructions\n\nDo something.\n", "# NomeAgente")]
    [TestCase("Manca la descrizione", "# A\n\n## Instructions\n\nDo something.\n", "descrizione")]
    [TestCase("Mancano le istruzioni", "# A\n\n> Descrizione.\n\n## Handoff\n\nUse only after.\n", "## Instructions")]
    [TestCase("Sezione vuota", "# A\n\n> Descrizione.\n\n## Instructions\n\n## Handoff\n\nUse only after.\n", "vuota")]
    public void Parse_RefusesAMalformedSpec(string caso, string content, string expectedInMessage)
    {
        //SUT
        var exception = Assert.Throws<InvalidOperationException>(() => AgentSpec.Parse("prova.agent.md", content));

        Assert.That(exception!.Message, Does.Contain(expectedInMessage), caso);
    }

    [Test]
    public void Parse_KeepsTheIndentationInsideASection()
    {
        //SETUP
        const string content = "# A\r\n\r\n> Descrizione.\r\n\r\n## Instructions\r\n\r\n1. Prima riga,\r\n   che continua qui.\r\n2. Seconda.\r\n";

        //SUT
        AgentSpec spec = AgentSpec.Parse("prova.agent.md", content);

        Assert.That(spec.Instructions, Is.EqualTo("1. Prima riga,\n   che continua qui.\n2. Seconda."));
    }
}
