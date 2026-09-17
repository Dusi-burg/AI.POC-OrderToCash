using System.Net;
using System.Net.Http.Json;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Erp;
using Dusiburg.AI.O2C.Shared.Demo;
using Dusiburg.AI.O2C.Shared.Errors;
using Dusiburg.AI.O2C.Shared.Idempotency;

namespace Dusiburg.AI.O2C.Erp.Api.Tests;

/// <summary>Viste in sola lettura per <c>Erp.Web</c> (6.10): clienti, magazzino, ordini ricevuti.</summary>
public class ErpViewEndpointTests : ErpApiTestBase
{
    [Test]
    public async Task ListCustomers_ReturnsTheSeedWithBlockFlagAndOrderCount()
    {
        //SETUP
        using HttpClient client = Factory.CreateClient();

        await CreateOrderForDealAsync(client, "D-1001");

        //SUT
        List<CustomerSummaryView> customers = (await client.GetFromJsonAsync<List<CustomerSummaryView>>("/api/views/customers", CancellationToken))!;

        Assert.That(customers.Select(c => c.Name), Is.EquivalentTo(DemoCatalog.Customers.Select(c => c.Name)));
        Assert.That(customers.Select(c => c.Name), Is.Ordered.Using((IComparer<string>)StringComparer.CurrentCulture));
        Assert.That(customers.Where(c => c.IsBlocked).Select(c => c.Name), Is.EqualTo(new[] { "Fonderia Emiliana S.r.l." }));
        Assert.That(customers.Single(c => c.VatNumber == CompanyVat("D-1001")).OrderCount, Is.EqualTo(1));
        Assert.That(customers.Sum(c => c.OrderCount), Is.EqualTo(1));
    }

    [Test]
    public async Task GetCustomer_Existing_ReturnsTheRegistryAndItsOrders()
    {
        //SETUP
        using HttpClient client = Factory.CreateClient();
        CreateOrderResponse order = await CreateOrderForDealAsync(client, "D-1001");
        CustomerDto? customer = await client.GetFromJsonAsync<CustomerDto>($"/api/customers?vatNumber={CompanyVat("D-1001")}", CancellationToken);
        DemoCustomer expected = DemoCatalog.Customers.Single(c => c.VatNumber == CompanyVat("D-1001"));

        //SUT
        CustomerDetailView? detail = await client.GetFromJsonAsync<CustomerDetailView>($"/api/views/customers/{customer!.CustomerId}", CancellationToken);

        Assert.That((detail!.Name, detail.Email, detail.Address, detail.CreditLimit, detail.IsBlocked),
            Is.EqualTo((expected.Name, expected.Email, expected.Address, expected.CreditLimit, expected.IsBlocked)));
        Assert.That(detail.Orders.Select(o => (o.OrderNumber, o.ExternalRef, o.Total)), Is.EqualTo(new[] { (order.OrderNumber, "D-1001", order.Total) }));
    }

    [TestCase("/api/views/customers/99999")]
    [TestCase("/api/views/orders/SO-2026-999999")]
    public async Task GetView_Unknown_ReturnsNotFound(string path)
    {
        //SETUP
        using HttpClient client = Factory.CreateClient();

        //SUT
        using HttpResponseMessage response = await client.GetAsync(path, CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await ReadProblemCodeAsync(response), Is.EqualTo(ToolErrorCodes.NotFound));
    }

    [Test]
    public async Task ListStock_ReturnsEveryProductWithAvailableAndTheShortFilter()
    {
        //SETUP
        using HttpClient client = Factory.CreateClient();
        string[] expectedShort = [.. DemoCatalog.Products.Where(p => p.OnHand - p.Reserved <= 0).Select(p => p.Sku).Order(StringComparer.Ordinal)];

        //SUT
        List<StockItemView> stock = (await client.GetFromJsonAsync<List<StockItemView>>("/api/views/stock", CancellationToken))!;
        List<StockItemView> shortStock = (await client.GetFromJsonAsync<List<StockItemView>>("/api/views/stock?shortOnly=true", CancellationToken))!;

        Assert.That(stock.Select(s => s.Sku), Is.EqualTo(DemoCatalog.Products.Select(p => p.Sku).Order(StringComparer.Ordinal)));
        Assert.That(stock.Select(s => (s.Sku, s.Description, s.Uom, s.ListPrice, s.OnHand, s.Reserved, s.Available, s.LeadTimeDays)),
            Is.EquivalentTo(DemoCatalog.Products.Select(p =>
                (p.Sku, p.Description, p.Uom, p.ListPrice, p.OnHand, p.Reserved, p.OnHand - p.Reserved, p.LeadTimeDays))));
        Assert.That(shortStock.Select(s => s.Sku), Is.EqualTo(expectedShort));
        Assert.That(expectedShort, Is.Not.Empty);
    }

    [Test]
    public async Task ListStock_AfterABackorder_ShowsNegativeAvailable()
    {
        //SETUP
        using HttpClient client = Factory.CreateClient();

        // D-1003 chiede 5 IND-MOT-003 con 3 in giacenza: la riserva è incondizionata (D55).
        await CreateOrderForDealAsync(client, "D-1003");

        //SUT
        List<StockItemView> shortStock = (await client.GetFromJsonAsync<List<StockItemView>>("/api/views/stock?shortOnly=true", CancellationToken))!;

        StockItemView motor = shortStock.Single(s => s.Sku == "IND-MOT-003");

        Assert.That((motor.OnHand, motor.Reserved, motor.Available), Is.EqualTo((3, 5, -2)));
    }

    [Test]
    public async Task ListOrders_FiltersByStatusAndCustomer_NewestFirst()
    {
        //SETUP
        using HttpClient client = Factory.CreateClient();
        CreateOrderResponse confirmed = await CreateOrderForDealAsync(client, "D-1001");
        CreateOrderResponse backorder = await CreateOrderForDealAsync(client, "D-1003");
        CustomerDto? customer = await client.GetFromJsonAsync<CustomerDto>($"/api/customers?vatNumber={CompanyVat("D-1003")}", CancellationToken);

        //SUT
        List<OrderSummaryView> all = (await client.GetFromJsonAsync<List<OrderSummaryView>>("/api/views/orders", CancellationToken))!;
        List<OrderSummaryView> backorders = (await client.GetFromJsonAsync<List<OrderSummaryView>>("/api/views/orders?status=Backorder", CancellationToken))!;
        List<OrderSummaryView> ofCustomer = (await client.GetFromJsonAsync<List<OrderSummaryView>>($"/api/views/orders?customerId={customer!.CustomerId}", CancellationToken))!;
        List<OrderSummaryView>? confirmedOfCustomer = await client.GetFromJsonAsync<List<OrderSummaryView>>(
            $"/api/views/orders?status=Confirmed&customerId={customer.CustomerId}", CancellationToken);

        Assert.That(all.Select(o => o.OrderNumber), Is.EqualTo(new[] { backorder.OrderNumber, confirmed.OrderNumber }));
        Assert.That(all.Select(o => (o.Status, o.HasBackorder)), Is.EqualTo(new[] { (OrderStatus.Backorder, true), (OrderStatus.Confirmed, false) }));
        Assert.That(backorders.Select(o => o.OrderNumber), Is.EqualTo(new[] { backorder.OrderNumber }));
        Assert.That(ofCustomer.Select(o => o.OrderNumber), Is.EqualTo(new[] { backorder.OrderNumber }));
        Assert.That(confirmedOfCustomer, Is.Empty);
    }

    [TestCase("Shipped")]
    [TestCase("1")]
    public async Task ListOrders_InvalidStatus_ReturnsValidationError(string status)
    {
        //SETUP
        using HttpClient client = Factory.CreateClient();

        //SUT
        using HttpResponseMessage response = await client.GetAsync($"/api/views/orders?status={status}", CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await ReadProblemCodeAsync(response), Is.EqualTo(ToolErrorCodes.ValidationError));
    }

    [Test]
    public async Task GetOrder_ByNumber_ReturnsLinesWithDescriptionsAndTheBackorderNote()
    {
        //SETUP
        using HttpClient client = Factory.CreateClient();
        CreateOrderResponse created = await CreateOrderForDealAsync(client, "D-1003");
        DemoDeal deal = DemoCatalog.Deals.Single(d => d.DealId == "D-1003");

        //SUT
        OrderDetailView? detail = await client.GetFromJsonAsync<OrderDetailView>($"/api/views/orders/{created.OrderNumber}", CancellationToken);

        Assert.That((detail!.Order.OrderId, detail.Order.Status, detail.Order.ExternalRef, detail.Order.Total),
            Is.EqualTo((created.OrderId, OrderStatus.Backorder, "D-1003", deal.Amount)));
        Assert.That(detail.Order.CustomerName, Is.EqualTo(DemoCatalog.Companies.Single(c => c.CompanyId == deal.CompanyId).Name));
        Assert.That(detail.BackorderNote, Is.EqualTo("IND-MOT-003: 2 PZ da ordinare"));
        Assert.That(detail.IdempotencyKey, Is.EqualTo(IdempotencyKey.From("D-1003", 1)));
        Assert.That(detail.Lines.Select(l => (l.Sku, l.Description, l.Quantity, l.UnitPrice)), Is.EqualTo(deal.Lines.Select(l =>
            (l.Sku, DemoCatalog.Products.Single(p => p.Sku == l.Sku).Description, l.Quantity, l.UnitPrice))));
        Assert.That(detail.Lines.Sum(l => l.LineTotal), Is.EqualTo(deal.Amount));
    }

    private static string CompanyVat(string dealId)
    {
        DemoDeal deal = DemoCatalog.Deals.Single(d => d.DealId == dealId);

        return DemoCatalog.Companies.Single(c => c.CompanyId == deal.CompanyId).VatNumber;
    }

    private static async Task<CreateOrderResponse> CreateOrderForDealAsync(HttpClient client, string dealId)
    {
        DemoDeal deal = DemoCatalog.Deals.Single(d => d.DealId == dealId);
        CustomerDto? customer = await client.GetFromJsonAsync<CustomerDto>($"/api/customers?vatNumber={CompanyVat(dealId)}", CancellationToken);

        var request = new CreateOrderRequest(
            customer!.CustomerId,
            [.. deal.Lines.Select(l => new OrderLineInput(l.Sku, l.Quantity, l.UnitPrice))],
            deal.DealId,
            IdempotencyKey.From(deal.DealId, 1));

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/orders", request, CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreateOrderResponse>(CancellationToken))!;
    }
}
