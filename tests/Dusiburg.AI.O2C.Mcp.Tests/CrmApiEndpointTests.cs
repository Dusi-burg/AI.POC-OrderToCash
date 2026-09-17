using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dusiburg.AI.O2C.Crm.Data;
using Dusiburg.AI.O2C.Crm.Data.Seed;
using Dusiburg.AI.O2C.Crm.Mcp.Crm;
using Dusiburg.AI.O2C.Crm.Mcp.Deals;
using Dusiburg.AI.O2C.Crm.Mcp.Messaging;
using Dusiburg.AI.O2C.Crm.Mcp.Tools;
using Dusiburg.AI.O2C.DbInit;
using Dusiburg.AI.O2C.Mcp.Tests.Support;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Contracts.Messaging;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Crm;
using Dusiburg.AI.O2C.Shared.Correlation;
using Dusiburg.AI.O2C.Shared.Demo;
using Dusiburg.AI.O2C.Shared.Errors;
using Dusiburg.AI.O2C.Shared.Telemetry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dusiburg.AI.O2C.Mcp.Tests;

/// <summary>
/// Publisher in memoria: registra gli eventi con il correlation id del contesto (come il publisher RabbitMQ) e può
/// simulare un broker irraggiungibile.
/// </summary>
internal sealed class RecordingDealEventPublisher(ICorrelationContext correlationContext) : IDealEventPublisher
{
    public ConcurrentQueue<(DealClosedWon Message, string CorrelationId)> Published { get; } = new();

    public bool Fail { get; set; }

    public Task<string> PublishClosedWonAsync(DealClosedWon message, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new InvalidOperationException("Broker non raggiungibile (simulato).");
        }

        string correlationId = correlationContext.Current ?? CorrelationId.New();

        Published.Enqueue((message, correlationId));

        return Task.FromResult(correlationId);
    }
}

/// <summary>
/// API utente di <c>crm-mcp</c> (6.9): viste in lettura e comando di chiusura, su un database dedicato alla classe
/// riportato ai dati demo prima di ogni test, con il publisher degli eventi in memoria.
/// </summary>
public class CrmApiEndpointTests
{
    private const string ApiKey = "test-crm-mcp-key";

    private string _connectionString = null!;
    private WebApplicationFactory<CrmTools> _factory = null!;

    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    private RecordingDealEventPublisher Publisher => (RecordingDealEventPublisher)_factory.Services.GetRequiredService<IDealEventPublisher>();

    [OneTimeSetUp]
    public async Task CreateDatabaseAndFactory()
    {
        _connectionString = O2CDatabaseInitializer.LocalConnectionStringFor($"O2C_Test_{Guid.NewGuid():N}");

        await O2CDatabaseInitializer.RecreateAsync(_connectionString, seed: true, CancellationToken);

        _factory = new WebApplicationFactory<CrmTools>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:sql", _connectionString);

            // Il client RabbitMQ dell'integrazione Aspire pretende una connection string anche se il publisher è sostituito.
            builder.UseSetting("ConnectionStrings:rabbitmq", "amqp://guest:guest@localhost:5672/o2c");
            builder.UseSetting("CRM_MCP_API_KEY", ApiKey);

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDealEventPublisher>();
                services.AddSingleton<IDealEventPublisher, RecordingDealEventPublisher>();
            });
        });
    }

    [SetUp]
    public async Task ResetCrmAndPublisher()
    {
        await WithDbAsync(db => CrmSeeder.ResetAsync(db, DateTimeOffset.UtcNow, CancellationToken));

        Publisher.Published.Clear();
        Publisher.Fail = false;
    }

    [OneTimeTearDown]
    public async Task DisposeFactoryAndDropDatabase()
    {
        await _factory.DisposeAsync();
        await O2CDatabaseInitializer.DropAsync(_connectionString);
    }

    [Test]
    public async Task ListDeals_WithoutFilters_ReturnsTheSeedOrderedByCode()
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();

        //SUT
        List<DealSummaryView> deals = (await client.GetFromJsonAsync<List<DealSummaryView>>("/api/views/deals", CancellationToken))!;

        Assert.That(deals.Select(d => d.DealId), Is.EqualTo(DemoCatalog.Deals.Select(d => d.DealId).Order(StringComparer.Ordinal)));
        Assert.That(deals.Select(d => (d.Stage, d.Revision, d.O2CStatus)), Is.All.EqualTo((DealStage.ContractSent, 1, (DealStatus?)null)));

        DealSummaryView first = deals[0];
        DemoDeal expected = DemoCatalog.Deals.Single(d => d.DealId == first.DealId);

        Assert.That((first.CompanyId, first.CompanyName, first.Amount, first.Currency),
            Is.EqualTo((expected.CompanyId, DemoCatalog.Companies.Single(c => c.CompanyId == expected.CompanyId).Name, expected.Amount, expected.Currency)));
    }

    [Test]
    public async Task ListDeals_WithFilters_ReturnsOnlyTheMatchingDeals()
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();

        await CloseAsync(client, "D-1001", DealCloseOutcome.Won);
        await CloseAsync(client, "D-1002", DealCloseOutcome.Won);
        await CloseAsync(client, "D-1003", DealCloseOutcome.Lost);
        await UpdateDealAsync(new UpdateDealRequest("D-1002", null, DealStatus.ApprovalPending, "In attesa."));

        //SUT
        List<DealSummaryView> won = (await client.GetFromJsonAsync<List<DealSummaryView>>("/api/views/deals?stage=ClosedWon", CancellationToken))!;
        List<DealSummaryView> pending = (await client.GetFromJsonAsync<List<DealSummaryView>>("/api/views/deals?o2cStatus=ApprovalPending", CancellationToken))!;
        List<DealSummaryView> lostOfC03 = (await client.GetFromJsonAsync<List<DealSummaryView>>("/api/views/deals?stage=closedlost&companyId=C-03", CancellationToken))!;
        List<DealSummaryView> lostOfC01 = (await client.GetFromJsonAsync<List<DealSummaryView>>("/api/views/deals?stage=ClosedLost&companyId=C-01", CancellationToken))!;

        Assert.That(won.Select(d => d.DealId), Is.EqualTo(new[] { "D-1001", "D-1002" }));
        Assert.That(pending.Select(d => d.DealId), Is.EqualTo(new[] { "D-1002" }));
        Assert.That(lostOfC03.Select(d => d.DealId), Is.EqualTo(new[] { "D-1003" }));
        Assert.That(lostOfC01, Is.Empty);
    }

    [TestCase("stage=Won")]
    [TestCase("stage=2")]
    [TestCase("o2cStatus=Sconosciuto")]
    public async Task ListDeals_InvalidFilter_ReturnsValidationError(string query)
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();

        //SUT
        using HttpResponseMessage response = await client.GetAsync($"/api/views/deals?{query}", CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await ReadProblemCodeAsync(response), Is.EqualTo(ToolErrorCodes.ValidationError));
    }

    [Test]
    public async Task GetDeal_Existing_ReturnsLinesAndTheO2CHistory()
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();
        DemoDeal expected = DemoCatalog.Deals.Single(d => d.DealId == "D-1003");

        await UpdateDealAsync(new UpdateDealRequest("D-1003", null, DealStatus.ApprovalPending, "Motivi: InsufficientStock"));
        await UpdateDealAsync(new UpdateDealRequest("D-1003", "SO-2026-000001", DealStatus.OrderCreated, "Ordine creato."));

        //SUT
        DealDetailView? detail = await client.GetFromJsonAsync<DealDetailView>("/api/views/deals/D-1003", CancellationToken);

        Assert.That(detail!.Deal.DealId, Is.EqualTo("D-1003"));
        Assert.That(detail.Deal.O2CStatus, Is.EqualTo(DealStatus.OrderCreated));
        Assert.That(detail.Deal.ErpOrderNumber, Is.EqualTo("SO-2026-000001"));
        Assert.That(detail.LastNote, Is.EqualTo("Ordine creato."));
        Assert.That(detail.Lines.Select(l => (l.Sku, l.Quantity, l.UnitPrice)), Is.EqualTo(expected.Lines.Select(l => (l.Sku, l.Quantity, l.UnitPrice))));
        Assert.That(detail.Lines.Sum(l => l.LineTotal), Is.EqualTo(expected.Amount));
        Assert.That(detail.Notes.Select(n => (n.Status, n.ErpOrderNumber)), Is.EqualTo(new[]
        {
            (DealStatus.ApprovalPending, (string?)null),
            (DealStatus.OrderCreated, "SO-2026-000001")
        }));
    }

    [TestCase("/api/views/deals/D-9999")]
    [TestCase("/api/views/companies/C-99")]
    public async Task GetView_Unknown_ReturnsNotFound(string path)
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();

        //SUT
        using HttpResponseMessage response = await client.GetAsync(path, CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await ReadProblemCodeAsync(response), Is.EqualTo(ToolErrorCodes.NotFound));
    }

    [Test]
    public async Task Companies_ListAndDetail_ReturnSeedDataWithDeals()
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();
        DemoCompany expected = DemoCatalog.Companies.Single(c => c.CompanyId == "C-02");

        //SUT
        List<CompanySummaryView> companies = (await client.GetFromJsonAsync<List<CompanySummaryView>>("/api/views/companies", CancellationToken))!;
        CompanyDetailView? company = await client.GetFromJsonAsync<CompanyDetailView>("/api/views/companies/C-02", CancellationToken);

        Assert.That(companies.Select(c => c.CompanyId), Is.EqualTo(DemoCatalog.Companies.Select(c => c.CompanyId).Order(StringComparer.Ordinal)));
        Assert.That(companies.Sum(c => c.DealCount), Is.EqualTo(DemoCatalog.Deals.Count));
        Assert.That((company!.Name, company.VatNumber, company.Email, company.Address),
            Is.EqualTo((expected.Name, expected.VatNumber, expected.Email, expected.Address)));
        Assert.That(company.Deals.Select(d => d.DealId),
            Is.EqualTo(DemoCatalog.Deals.Where(d => d.CompanyId == "C-02").Select(d => d.DealId).Order(StringComparer.Ordinal)));
    }

    [Test]
    public async Task Close_Won_ClosesTheDealAndPublishesOneEventWithTheCallerCorrelationId()
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(CorrelationId.HeaderName, "fase6-close-won");

        //SUT
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/deals/D-1001/close", new CloseDealRequest(DealCloseOutcome.Won), CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        DealDetailView? detail = await response.Content.ReadFromJsonAsync<DealDetailView>(CancellationToken);

        Assert.That((detail!.Deal.Stage, detail.Deal.Revision, detail.Deal.O2CStatus), Is.EqualTo((DealStage.ClosedWon, 1, (DealStatus?)null)));
        Assert.That(Publisher.Published.Select(p => (p.Message.DealId, p.Message.Revision, p.CorrelationId)),
            Is.EqualTo(new[] { ("D-1001", 1, "fase6-close-won") }));
    }

    [Test]
    public async Task Close_Lost_ClosesTheDealWithoutPublishing()
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();

        //SUT
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/deals/D-1004/close", new CloseDealRequest(DealCloseOutcome.Lost), CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await StageAsync("D-1004"), Is.EqualTo(DealStage.ClosedLost));
        Assert.That(Publisher.Published, Is.Empty);
    }

    [TestCase(DealCloseOutcome.Won, DealCloseOutcome.Won)]
    [TestCase(DealCloseOutcome.Won, DealCloseOutcome.Lost)]
    [TestCase(DealCloseOutcome.Lost, DealCloseOutcome.Won)]
    public async Task Close_AlreadyClosedDeal_ReturnsConflictWithoutChanges(DealCloseOutcome first, DealCloseOutcome second)
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();

        await CloseAsync(client, "D-1002", first);

        DealStage stageAfterFirst = await StageAsync("D-1002");
        int eventsAfterFirst = Publisher.Published.Count;

        //SUT
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/deals/D-1002/close", new CloseDealRequest(second), CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await ReadProblemCodeAsync(response), Is.EqualTo(ToolErrorCodes.Conflict));
        Assert.That(await StageAsync("D-1002"), Is.EqualTo(stageAfterFirst));
        Assert.That(Publisher.Published, Has.Count.EqualTo(eventsAfterFirst));
    }

    [Test]
    public async Task Close_ConcurrentWonRequests_PublishOnlyOnce()
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();

        //SUT
        HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            client.PostAsJsonAsync("/api/deals/D-1005/close", new CloseDealRequest(DealCloseOutcome.Won), CancellationToken)));

        Assert.That(responses.Count(r => r.StatusCode == HttpStatusCode.OK), Is.EqualTo(1));
        Assert.That(responses.Count(r => r.StatusCode == HttpStatusCode.Conflict), Is.EqualTo(4));
        Assert.That(Publisher.Published, Has.Count.EqualTo(1));

        foreach (HttpResponseMessage response in responses)
        {
            response.Dispose();
        }
    }

    [TestCase("""{ "outcome": "Maybe" }""")]
    [TestCase("""{ "outcome": 1 }""")]
    [TestCase("{}")]
    public async Task Close_InvalidOutcome_ReturnsBadRequestWithoutChanges(string body)
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        //SUT
        using HttpResponseMessage response = await client.PostAsync("/api/deals/D-1001/close", content, CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await StageAsync("D-1001"), Is.EqualTo(DealStage.ContractSent));
        Assert.That(Publisher.Published, Is.Empty);
    }

    [Test]
    public async Task Close_UnknownDeal_ReturnsNotFound()
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();

        //SUT
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/deals/D-9999/close", new CloseDealRequest(DealCloseOutcome.Won), CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await ReadProblemCodeAsync(response), Is.EqualTo(ToolErrorCodes.NotFound));
    }

    [Test]
    public async Task Close_WonWithBrokerDown_KeepsTheDealWonAndReturnsServiceUnavailable()
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();
        Publisher.Fail = true;

        //SUT
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/deals/D-1001/close", new CloseDealRequest(DealCloseOutcome.Won), CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(await ReadProblemCodeAsync(response), Is.EqualTo(ToolErrorCodes.UpstreamUnavailable));
        Assert.That(await StageAsync("D-1001"), Is.EqualTo(DealStage.ClosedWon));
    }

    [Test]
    public async Task DevCloseWon_OpenThenWonDeal_ClosesAndThenRepublishes()
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();

        //SUT
        using HttpResponseMessage first = await client.PostAsync("/dev/deals/D-1001/close-won", content: null, CancellationToken);
        using HttpResponseMessage second = await client.PostAsync("/dev/deals/D-1001/close-won", content: null, CancellationToken);

        Assert.That((first.StatusCode, second.StatusCode), Is.EqualTo((HttpStatusCode.OK, HttpStatusCode.OK)));
        Assert.That(await StageAsync("D-1001"), Is.EqualTo(DealStage.ClosedWon));
        Assert.That(Publisher.Published.Select(p => p.Message.DealId), Is.EqualTo(new[] { "D-1001", "D-1001" }));
    }

    [Test]
    public async Task DevCloseWon_LostDeal_ReturnsConflict()
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();

        await CloseAsync(client, "D-1001", DealCloseOutcome.Lost);

        //SUT
        using HttpResponseMessage response = await client.PostAsync("/dev/deals/D-1001/close-won", content: null, CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await StageAsync("D-1001"), Is.EqualTo(DealStage.ClosedLost));
        Assert.That(Publisher.Published, Is.Empty);
    }

    [TestCase("/dev/deals")]
    [TestCase("/dev/deals/D-1001")]
    public async Task DevDealReads_AreGone(string path)
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();

        //SUT
        using HttpResponseMessage response = await client.GetAsync(path, CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Api_IsReachableWithoutApiKey_WhileMcpStillRequiresIt()
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();

        //SUT
        using HttpResponseMessage api = await client.GetAsync("/api/views/deals", CancellationToken);
        (HttpStatusCode mcpStatus, string? _) = await McpTestClient.PostToolsListAsync(_factory, null, CancellationToken);

        Assert.That(api.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(mcpStatus, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Close_EmitsSpanWithDealOutcomeAndCorrelationId()
    {
        //SETUP
        var spans = new ConcurrentQueue<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == O2CTelemetry.Sources.McpCrm,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Enqueue
        };

        ActivitySource.AddActivityListener(listener);

        using HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(CorrelationId.HeaderName, "fase6-span");

        //SUT
        await CloseAsync(client, "D-1006", DealCloseOutcome.Won);
        await CloseAsync(client, "D-1006", DealCloseOutcome.Lost);

        Assert.That(
            spans.Where(s => s.DisplayName == DealClosingService.CloseActivityName)
                .Select(s => (Tag(s, O2CTelemetry.Attributes.DealId), Tag(s, O2CTelemetry.Attributes.DealCloseOutcome),
                    Tag(s, O2CTelemetry.Attributes.CorrelationId), Tag(s, O2CTelemetry.Attributes.ToolOutcome))),
            Is.EqualTo(new[]
            {
                ("D-1006", "Won", "fase6-span", "Closed"),
                ("D-1006", "Lost", "fase6-span", "AlreadyClosed")
            }));
    }

    private static async Task CloseAsync(HttpClient client, string dealId, DealCloseOutcome outcome)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/deals/{dealId}/close", new CloseDealRequest(outcome), CancellationToken);
    }

    private async Task UpdateDealAsync(UpdateDealRequest request)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<ICrmClient>().UpdateDealAsync(request, CancellationToken);
    }

    private Task<DealStage> StageAsync(string dealId) =>
        WithDbAsync(db => db.Deals.Where(d => d.Code == dealId).Select(d => d.Stage).SingleAsync(CancellationToken));

    private async Task WithDbAsync(Func<CrmDbContext, Task> action)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();

        await action(scope.ServiceProvider.GetRequiredService<CrmDbContext>());
    }

    private async Task<T> WithDbAsync<T>(Func<CrmDbContext, Task<T>> action)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();

        return await action(scope.ServiceProvider.GetRequiredService<CrmDbContext>());
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

        return document.RootElement.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
    }

    private static string? Tag(Activity activity, string name) => activity.GetTagItem(name) as string;
}
