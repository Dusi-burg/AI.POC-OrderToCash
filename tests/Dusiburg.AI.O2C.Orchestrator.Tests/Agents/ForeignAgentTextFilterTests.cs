using Dusiburg.AI.O2C.Orchestrator.Agents;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.O2C.Orchestrator.Tests.Agents;

/// <summary>
/// Filtro della catena fra agenti (D62): l'agente vede chiamate e risultati degli altri agenti, non il loro testo.
/// La conversazione è quella catturata dal vivo su D-1003 prima della richiesta a FulfillmentAgent.
/// </summary>
public class ForeignAgentTextFilterTests
{
    private const string Fulfillment = "FulfillmentAgent";
    private const string Intake = "IntakeAgent";

    [Test]
    public void Filter_RemovesTheTextOfOtherAgentsAndKeepsTheirToolFacts()
    {
        //SETUP
        List<ChatMessage> conversation = HandoffConversation();

        //SUT
        List<ChatMessage> filtered = ForeignAgentTextFilter.Filter(conversation, Fulfillment, WorkflowAgents.Names);

        Assert.That(filtered.Select(m => (m.Role, m.AuthorName)), Is.EqualTo(new (ChatRole, string?)[]
        {
            (ChatRole.User, null),
            (ChatRole.Assistant, Intake),
            (ChatRole.Tool, Intake),
            (ChatRole.Assistant, Intake),
            (ChatRole.Tool, Intake)
        }), "il messaggio con il solo testo di Intake sparisce");
        Assert.That(filtered.SelectMany(m => m.Contents).OfType<TextContent>().Select(t => t.Text), Is.EqualTo(new[] { "Process CRM deal D-1003." }));
        Assert.That(filtered.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Select(c => c.Name), Is.EqualTo(new[] { "get_deal", "get_company" }));
        Assert.That(filtered.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Select(r => r.CallId), Is.EqualTo(new[] { "c1", "c2" }));
    }

    [Test]
    public void Filter_KeepsTheOwnTextAndMessagesFromOutsideTheWorkflow()
    {
        //SETUP
        List<ChatMessage> conversation =
        [
            new(ChatRole.User, "Process CRM deal D-1003."),
            new(ChatRole.Assistant, "I checked the stock.") { AuthorName = Fulfillment },
            new(ChatRole.User, "User did not respond. Continue assisting autonomously."),
            new(ChatRole.User, "Note from someone else.") { AuthorName = "Operator" }
        ];

        //SUT
        List<ChatMessage> filtered = ForeignAgentTextFilter.Filter(conversation, Fulfillment, WorkflowAgents.Names);

        Assert.That(filtered, Is.EqualTo(conversation), "nessun messaggio cambia: si filtrano solo gli altri agenti del workflow");
    }

    [Test]
    public void Filter_DoesNotChangeTheOriginalConversation()
    {
        //SETUP
        List<ChatMessage> conversation = HandoffConversation();
        int[] contentCounts = [.. conversation.Select(m => m.Contents.Count)];

        //SUT
        ForeignAgentTextFilter.Filter(conversation, Fulfillment, WorkflowAgents.Names);

        Assert.That(conversation.Select(m => m.Contents.Count), Is.EqualTo(contentCounts), "lo stato del workflow resta completo");
        Assert.That(conversation[^1].Text, Does.Contain("I'll hand off to FulfillmentAgent immediately."));
    }

    [Test]
    public async Task Client_SendsTheFilteredConversationToTheModel()
    {
        //SETUP
        var model = new RecordingChatClient();
        using var client = new ForeignAgentTextFilter(model, Fulfillment, WorkflowAgents.Names);

        //SUT
        await client.GetResponseAsync(HandoffConversation());
        await foreach (ChatResponseUpdate _ in client.GetStreamingResponseAsync(HandoffConversation()))
        {
        }

        Assert.That(model.Requests, Has.Count.EqualTo(2));
        Assert.That(model.Requests.Select(r => r.Count), Is.All.EqualTo(5));
        Assert.That(model.Requests.SelectMany(r => r).Select(m => m.Text), Has.None.Contains("hand off"));
    }

    /// <summary>Conversazione vista da FulfillmentAgent dopo l'handoff: fatti di Intake più il suo testo finale come messaggio utente.</summary>
    private static List<ChatMessage> HandoffConversation() =>
    [
        new(ChatRole.User, "Process CRM deal D-1003."),
        new(ChatRole.Assistant, [new FunctionCallContent("c1", "get_deal", new Dictionary<string, object?> { ["dealId"] = "D-1003" }), new TextContent("")]) { AuthorName = Intake },
        new(ChatRole.Tool, [new FunctionResultContent("c1", "{\"dealId\":\"D-1003\"}")]) { AuthorName = Intake },
        new(ChatRole.Assistant, [new TextContent("Now I need the company."), new FunctionCallContent("c2", "get_company", new Dictionary<string, object?> { ["companyId"] = "C-03" })]) { AuthorName = Intake },
        new(ChatRole.Tool, [new FunctionResultContent("c2", "{\"companyId\":\"C-03\"}")]) { AuthorName = Intake },
        new(ChatRole.User, [new TextContent("5×830 + 5×185 + 5×45 = 5300 ✓\n\nThe deal is valid. I'll hand off to FulfillmentAgent immediately."), new TextContent("")]) { AuthorName = Intake }
    ];

    private sealed class RecordingChatClient : IChatClient
    {
        public List<List<ChatMessage>> Requests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Requests.Add([.. messages]);

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add([.. messages]);
            await Task.Yield();

            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
