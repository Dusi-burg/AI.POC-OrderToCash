using System.Net;
using Dusiburg.AI.O2C.Crm.Web.Api;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Crm;
using Dusiburg.AI.O2C.Shared.Errors;
using Dusiburg.AI.O2C.Web.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dusiburg.AI.O2C.Web.Tests;

/// <summary>Pagine e comandi di <c>Crm.Web</c> (6.11) con le API di <c>Crm.Mcp</c> sostituite da un handler finto.</summary>
public class CrmWebTests
{
    private const string ErpWeb = "http://erp.test";
    private const string ApprovalsWeb = "http://approvals.test";

    private readonly StubApiHandler _crm = new();
    private WebApplicationFactory<CrmWebEntryPoint> _factory = null!;

    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [OneTimeSetUp]
    public void CreateFactory()
    {
        _factory = new WebApplicationFactory<CrmWebEntryPoint>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(CrmApiClient.BaseAddressConfigurationKey, "http://crm-api.test");
            builder.UseSetting("Links:ErpWeb", ErpWeb);
            builder.UseSetting("Links:ApprovalsWeb", ApprovalsWeb);

            builder.ConfigureServices(services =>
                services.AddHttpClient(CrmApiClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _crm));
        });
    }

    [SetUp]
    public void ClearApi() => _crm.Clear();

    [OneTimeTearDown]
    public async Task DisposeFactory() => await _factory.DisposeAsync();

    [Test]
    public async Task Home_ShowsCountsPerStageAndTheFlowsInProgress()
    {
        //SETUP
        _crm.Json(HttpMethod.Get, "/api/views/deals", new[]
        {
            Deal("D-1001", DealStage.ClosedWon, DealStatus.OrderCreated, "SO-2026-000001"),
            Deal("D-1002", DealStage.ClosedWon, DealStatus.ApprovalPending),
            Deal("D-1003", DealStage.ContractSent),
            Deal("D-1004", DealStage.ClosedWon)
        });
        _crm.Json(HttpMethod.Get, "/api/views/companies", new[] { new CompanySummaryView("C-01", "Officine", null, "a@b.it", 1) });
        using HttpClient client = _factory.CreateClient();

        //SUT
        string page = await client.GetStringAsync("/", CancellationToken);

        Assert.That(page, Does.Contain("Chiuso vinto").And.Contain("Contratto inviato"));
        Assert.That(page, Does.Contain("href=\"/deals/D-1002\"").And.Contain("href=\"/deals/D-1004\""));
        Assert.That(page, Does.Not.Contain("href=\"/deals/D-1001\""), "un deal con esito finale non è un flusso in corso");
    }

    [Test]
    public async Task Deals_PassesTheFiltersToTheApi()
    {
        //SETUP
        _crm.Json(HttpMethod.Get, "/api/views/deals?stage=ClosedWon&o2cStatus=ApprovalPending&companyId=C-02",
            new[] { Deal("D-1002", DealStage.ClosedWon, DealStatus.ApprovalPending) });
        using HttpClient client = _factory.CreateClient();

        //SUT
        string page = await client.GetStringAsync("/deals?Stage=ClosedWon&O2CStatus=ApprovalPending&CompanyId=C-02", CancellationToken);

        Assert.That(page, Does.Contain("<code>D-1002</code>").And.Contain("ApprovalPending"));
        Assert.That(_crm.Requests.Select(r => r.PathAndQuery),
            Is.EqualTo(new[] { "/api/views/deals?stage=ClosedWon&o2cStatus=ApprovalPending&companyId=C-02" }));
    }

    [Test]
    public async Task DealDetails_OpenDeal_ShowsTheCloseCommandsWithoutRefresh()
    {
        //SETUP
        GivenDeal(Deal("D-1001", DealStage.ContractSent));
        using HttpClient client = _factory.CreateClient();

        //SUT
        string page = await client.GetStringAsync("/deals/D-1001", CancellationToken);

        Assert.That(page, Does.Contain("id=\"close-commands\"").And.Contain("Chiudi vinto").And.Contain("Chiudi perso"));
        Assert.That(page, Does.Contain("IND-BRG-001"));
        Assert.That(page, Does.Not.Contain("http-equiv=\"refresh\"").And.Not.Contain("id=\"approvals-link\""));
    }

    [TestCase(null)]
    [TestCase(DealStatus.ApprovalPending)]
    public async Task DealDetails_WonDealInProgress_RefreshesAndHasNoCommands(DealStatus? status)
    {
        //SETUP
        GivenDeal(Deal("D-1002", DealStage.ClosedWon, status));
        using HttpClient client = _factory.CreateClient();

        //SUT
        string page = await client.GetStringAsync("/deals/D-1002", CancellationToken);

        Assert.That(page, Does.Contain($"<meta http-equiv=\"refresh\" content=\"{DealPresentation.RefreshSeconds}\" />"));
        Assert.That(page, Does.Contain("id=\"in-flight\"").And.Not.Contain("id=\"close-commands\""));
        Assert.That(page.Contains("id=\"approvals-link\"", StringComparison.Ordinal), Is.EqualTo(status is not null),
            "il link alle approvazioni compare solo quando O2C ha scritto sul deal");
    }

    [Test]
    public async Task DealDetails_CompletedDeal_StopsRefreshingAndLinksTheOrderAndTheApprovals()
    {
        //SETUP
        GivenDeal(Deal("D-1003", DealStage.ClosedWon, DealStatus.OrderCreated, "SO-2026-000007"),
            new DealNoteView(DealStatus.ApprovalPending, null, "Motivi: InsufficientStock", DateTimeOffset.UtcNow),
            new DealNoteView(DealStatus.OrderCreated, "SO-2026-000007", "IND-MOT-003: 2 PZ da ordinare", DateTimeOffset.UtcNow));
        using HttpClient client = _factory.CreateClient();

        //SUT
        string page = await client.GetStringAsync("/deals/D-1003", CancellationToken);

        Assert.That(page, Does.Not.Contain("http-equiv=\"refresh\"").And.Not.Contain("id=\"close-commands\""));
        Assert.That(page, Does.Contain($"href=\"{ErpWeb}/orders/SO-2026-000007\""));
        Assert.That(page, Does.Contain($"href=\"{ApprovalsWeb}/approvals?all=true&amp;dealId=D-1003\""));
        Assert.That(page, Does.Contain("Motivi: InsufficientStock").And.Contain("IND-MOT-003: 2 PZ da ordinare"));
    }

    [TestCase("/deals/D-9999")]
    [TestCase("/companies/C-99")]
    public async Task Details_Unknown_IsNotFound(string path)
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();

        //SUT
        using HttpResponseMessage response = await client.GetAsync(path, CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task CompanyDetails_ShowsTheRegistryAndItsDeals()
    {
        //SETUP
        _crm.Json(HttpMethod.Get, "/api/views/companies/C-02", new CompanyDetailView(
            "C-02", "Cartiera del Brenta S.p.A.", "IT04567890280", "approvvigionamenti@cartierabrenta.it", "Via Riviera 3",
            [Deal("D-1002", DealStage.ContractSent)]));
        using HttpClient client = _factory.CreateClient();

        //SUT
        string page = await client.GetStringAsync("/companies/C-02", CancellationToken);

        Assert.That(page, Does.Contain("Cartiera del Brenta S.p.A.").And.Contain("IT04567890280").And.Contain("href=\"/deals/D-1002\""));
    }

    [TestCase("Won", "chiuso come vinto", "alert-success")]
    [TestCase("Lost", "chiuso come perso", "alert-success")]
    public async Task Close_SendsTheOutcomeAndShowsTheResult(string handler, string expectedMessage, string expectedAlert)
    {
        //SETUP
        GivenDeal(Deal("D-1001", DealStage.ContractSent));
        _crm.Json(HttpMethod.Post, "/api/deals/D-1001/close", Detail(Deal("D-1001", DealStage.ClosedWon)));
        using HttpClient client = _factory.CreateClient();
        string token = await HtmlForms.ReadAntiforgeryTokenAsync(client, "/deals/D-1001", CancellationToken);

        //SUT
        using HttpResponseMessage response = await client.PostAsync($"/deals/D-1001?handler={handler}", HtmlForms.Form(token), CancellationToken);

        string page = await response.Content.ReadAsStringAsync(CancellationToken);
        StubRequest post = _crm.Requests.Single(r => r.Method == HttpMethod.Post);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(page, Does.Contain(expectedMessage).And.Contain(expectedAlert));
        Assert.That(post.Body, Does.Contain($"\"outcome\":\"{handler}\""));
        Assert.That(post.CorrelationId, Is.Not.Null.And.Not.Empty, "il correlation id della richiesta web diventa quello del workflow");
    }

    [Test]
    public async Task Close_AlreadyClosed_ShowsTheCrmMessageAsAWarning()
    {
        //SETUP
        GivenDeal(Deal("D-1001", DealStage.ContractSent));
        _crm.Problem(HttpMethod.Post, "/api/deals/D-1001/close", HttpStatusCode.Conflict, ToolErrorCodes.Conflict, "Il deal D-1001 è già chiuso: nessuna modifica.");
        using HttpClient client = _factory.CreateClient();
        string token = await HtmlForms.ReadAntiforgeryTokenAsync(client, "/deals/D-1001", CancellationToken);

        //SUT
        using HttpResponseMessage response = await client.PostAsync("/deals/D-1001?handler=Won", HtmlForms.Form(token), CancellationToken);

        string page = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.That(page, Does.Contain("alert-warning").And.Contain("è già chiuso"));
    }

    [Test]
    public async Task Close_EventNotPublished_ShowsAnErrorAndIsNotRetried()
    {
        //SETUP
        GivenDeal(Deal("D-1001", DealStage.ContractSent));
        _crm.Problem(HttpMethod.Post, "/api/deals/D-1001/close", HttpStatusCode.ServiceUnavailable, ToolErrorCodes.UpstreamUnavailable,
            "Il deal D-1001 è chiuso come vinto, ma l'evento non è stato pubblicato: il flusso non è partito.");
        using HttpClient client = _factory.CreateClient();
        string token = await HtmlForms.ReadAntiforgeryTokenAsync(client, "/deals/D-1001", CancellationToken);

        //SUT
        using HttpResponseMessage response = await client.PostAsync("/deals/D-1001?handler=Won", HtmlForms.Form(token), CancellationToken);

        string page = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.That(page, Does.Contain("alert-danger").And.Contain("il flusso non è partito"));
        Assert.That(_crm.Requests.Count(r => r.Method == HttpMethod.Post), Is.EqualTo(1), "la chiusura non si ritenta (G6.3)");
    }

    [Test]
    public async Task Close_UnknownDeal_IsNotFound()
    {
        //SETUP
        GivenDeal(Deal("D-1001", DealStage.ContractSent));
        using HttpClient client = _factory.CreateClient();
        string token = await HtmlForms.ReadAntiforgeryTokenAsync(client, "/deals/D-1001", CancellationToken);

        //SUT
        using HttpResponseMessage response = await client.PostAsync("/deals/D-9999?handler=Won", HtmlForms.Form(token), CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Close_WithoutAntiforgeryToken_IsRejected()
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();

        //SUT
        using HttpResponseMessage response = await client.PostAsync("/deals/D-1001?handler=Won", content: null, CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(_crm.Requests, Is.Empty);
    }

    private void GivenDeal(DealSummaryView deal, params DealNoteView[] notes) =>
        _crm.Json(HttpMethod.Get, $"/api/views/deals/{deal.DealId}", Detail(deal, notes));

    private static DealDetailView Detail(DealSummaryView deal, params DealNoteView[] notes) =>
        new(deal, notes.LastOrDefault()?.Note, [new DealLineView("IND-BRG-001", 40, 12.00m)], notes);

    private static DealSummaryView Deal(string dealId, DealStage stage, DealStatus? status = null, string? orderNumber = null) =>
        new(dealId, $"Deal {dealId}", "C-01", "Officine Meccaniche Brambilla S.r.l.", 480m, "EUR", stage, 1, status, orderNumber, DateTimeOffset.UtcNow);
}
