using System.Net;
using Dusiburg.AI.O2C.Erp.Web.Api;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Erp;
using Dusiburg.AI.O2C.Web.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dusiburg.AI.O2C.Web.Tests;

/// <summary>Pagine in sola lettura di <c>Erp.Web</c> (6.11) con le viste di <c>Erp.Api</c> sostituite da un handler finto.</summary>
public class ErpWebTests
{
    private const string CrmWeb = "http://crm.test";

    private readonly StubApiHandler _erp = new();
    private WebApplicationFactory<ErpWebEntryPoint> _factory = null!;

    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [OneTimeSetUp]
    public void CreateFactory()
    {
        _factory = new WebApplicationFactory<ErpWebEntryPoint>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(ErpApiClient.BaseAddressConfigurationKey, "http://erp-api.test");
            builder.UseSetting("Links:CrmWeb", CrmWeb);

            builder.ConfigureServices(services =>
                services.AddHttpClient(nameof(ErpApiClient)).ConfigurePrimaryHttpMessageHandler(() => _erp));
        });
    }

    [SetUp]
    public void ClearApi() => _erp.Clear();

    [OneTimeTearDown]
    public async Task DisposeFactory() => await _factory.DisposeAsync();

    [Test]
    public async Task Home_ShowsCustomersOrdersAndShortStock()
    {
        //SETUP
        _erp.Json(HttpMethod.Get, "/api/views/customers", new[]
        {
            new CustomerSummaryView(1, "Officine Meccaniche Brambilla S.r.l.", "IT01234560157", "a@b.it", 50_000m, false, 1),
            new CustomerSummaryView(5, "Fonderia Emiliana S.r.l.", "IT05678900375", "c@d.it", 30_000m, true, 0)
        });
        _erp.Json(HttpMethod.Get, "/api/views/orders", new[] { Order("SO-2026-000001", OrderStatus.Confirmed, "D-1001") });
        _erp.Json(HttpMethod.Get, "/api/views/stock?shortOnly=true", new[] { Stock("IND-MOT-004", onHand: 0, reserved: 0) });
        using HttpClient client = _factory.CreateClient();

        //SUT
        string page = await client.GetStringAsync("/", CancellationToken);

        Assert.That(page, Does.Contain("Clienti (1 bloccati)").And.Contain("Ordini Confirmed").And.Contain("Prodotti sotto scorta"));
        Assert.That(page, Does.Contain("href=\"/orders/SO-2026-000001\"").And.Contain($"href=\"{CrmWeb}/deals/D-1001\""));
    }

    [Test]
    public async Task Stock_HighlightsShortAndOverReservedProducts()
    {
        //SETUP
        _erp.Json(HttpMethod.Get, "/api/views/stock", new[]
        {
            Stock("IND-BRG-001", onHand: 500, reserved: 20),
            Stock("IND-MOT-003", onHand: 3, reserved: 5),
            Stock("IND-MOT-004", onHand: 0, reserved: 0)
        });
        using HttpClient client = _factory.CreateClient();

        //SUT
        string page = await client.GetStringAsync("/stock", CancellationToken);

        Assert.That(page, Does.Contain("<tr class=\"\" data-sku=\"IND-BRG-001\">"));
        Assert.That(page, Does.Contain("<tr class=\"table-danger\" data-sku=\"IND-MOT-003\">"));
        Assert.That(page, Does.Contain("<tr class=\"table-warning\" data-sku=\"IND-MOT-004\">"));
        Assert.That(page, Does.Contain(">-2<"));
    }

    [Test]
    public async Task Stock_ShortOnly_AsksTheApiForTheShortProducts()
    {
        //SETUP
        _erp.Json(HttpMethod.Get, "/api/views/stock?shortOnly=true", Array.Empty<StockItemView>());
        using HttpClient client = _factory.CreateClient();

        //SUT
        string page = await client.GetStringAsync("/stock?shortOnly=true", CancellationToken);

        Assert.That(page, Does.Contain("Nessun prodotto sotto scorta"));
        Assert.That(_erp.Requests.Select(r => r.PathAndQuery), Is.EqualTo(new[] { "/api/views/stock?shortOnly=true" }));
    }

    [Test]
    public async Task Orders_PassesTheFiltersToTheApi()
    {
        //SETUP
        _erp.Json(HttpMethod.Get, "/api/views/orders?status=Backorder&customerId=3", new[] { Order("SO-2026-000002", OrderStatus.Backorder, "D-1003") });
        using HttpClient client = _factory.CreateClient();

        //SUT
        string page = await client.GetStringAsync("/orders?Status=Backorder&CustomerId=3", CancellationToken);

        Assert.That(page, Does.Contain("<code>SO-2026-000002</code>").And.Contain("text-bg-warning"));
        Assert.That(_erp.Requests.Select(r => r.PathAndQuery), Is.EqualTo(new[] { "/api/views/orders?status=Backorder&customerId=3" }));
    }

    [Test]
    public async Task OrderDetails_ShowsLinesBackorderNoteAndTheDealLink()
    {
        //SETUP
        _erp.Json(HttpMethod.Get, "/api/views/orders/SO-2026-000002", new OrderDetailView(
            Order("SO-2026-000002", OrderStatus.Backorder, "D-1003"),
            "o2c-D-1003-r1",
            "IND-MOT-003: 2 PZ da ordinare",
            [new OrderLineView("IND-MOT-003", "Motore brushless 750 W con encoder", "PZ", 5, 830m)]));
        using HttpClient client = _factory.CreateClient();

        //SUT
        string page = await client.GetStringAsync("/orders/SO-2026-000002", CancellationToken);

        Assert.That(page, Does.Contain("id=\"backorder-note\"").And.Contain("IND-MOT-003: 2 PZ da ordinare"));
        Assert.That(page, Does.Contain("Motore brushless 750 W con encoder").And.Contain("o2c-D-1003-r1"));
        Assert.That(page, Does.Contain($"href=\"{CrmWeb}/deals/D-1003\""));
    }

    [Test]
    public async Task CustomerDetails_BlockedCustomer_ShowsTheBadgeAndItsOrders()
    {
        //SETUP
        _erp.Json(HttpMethod.Get, "/api/views/customers/5", new CustomerDetailView(
            5, "Fonderia Emiliana S.r.l.", "IT05678900375", "acquisti@fonderiaemiliana.it", "Via Emilia Ovest 210", 30_000m, true,
            [Order("SO-2026-000003", OrderStatus.Confirmed, "D-1005")]));
        using HttpClient client = _factory.CreateClient();

        //SUT
        string page = await client.GetStringAsync("/customers/5", CancellationToken);

        Assert.That(page, Does.Contain("id=\"blocked\"").And.Contain("Via Emilia Ovest 210").And.Contain("SO-2026-000003"));
    }

    [TestCase("/customers/99")]
    [TestCase("/orders/SO-2026-999999")]
    public async Task Details_Unknown_IsNotFound(string path)
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();

        //SUT
        using HttpResponseMessage response = await client.GetAsync(path, CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [TestCase("/")]
    [TestCase("/customers")]
    [TestCase("/stock")]
    [TestCase("/orders/SO-2026-000001")]
    public async Task Post_IsNotAllowed(string path)
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();
        using var content = new FormUrlEncodedContent([]);

        //SUT
        using HttpResponseMessage response = await client.PostAsync(path, content, CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
        Assert.That(response.Content.Headers.Allow, Is.EquivalentTo(new[] { "GET", "HEAD" }).Or.Empty);
        Assert.That(_erp.Requests, Is.Empty, "una richiesta di scrittura non arriva mai all'ERP");
    }

    private static OrderSummaryView Order(string orderNumber, OrderStatus status, string dealId) =>
        new(Guid.CreateVersion7(), orderNumber, 3, "Automazioni Nord-Est S.r.l.", 4_375m, status, dealId,
            status == OrderStatus.Backorder, DateTimeOffset.UtcNow);

    private static StockItemView Stock(string sku, int onHand, int reserved) =>
        new(sku, $"Prodotto {sku}", "PZ", 10m, onHand, reserved, onHand - reserved, 7);
}
