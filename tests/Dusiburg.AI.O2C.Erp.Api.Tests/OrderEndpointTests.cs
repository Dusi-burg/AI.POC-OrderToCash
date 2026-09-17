using System.Net;
using System.Net.Http.Json;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Dusiburg.AI.O2C.Shared.Demo;
using Dusiburg.AI.O2C.Shared.Errors;
using Dusiburg.AI.O2C.Shared.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.O2C.Erp.Api.Tests;

public class OrderEndpointTests : ErpApiTestBase
{
    private static readonly DemoDeal Deal1001 = DemoCatalog.Deals.Single(d => d.DealId == "D-1001");

    [Test]
    public async Task CreateOrder_AllLinesAvailable_CreatesConfirmedOrderAndReservesStock()
    {
        //SETUP
        using var client = Factory.CreateClient();
        var request = await OrderForDealAsync(client, Deal1001);
        var reservedBefore = await ReservedAsync("IND-BRG-001");

        //SUT
        using var response = await client.PostAsJsonAsync("/api/orders", request, CancellationToken);
        var created = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(CancellationToken);
        var order = await client.GetFromJsonAsync<OrderDto>($"/api/orders/{created!.OrderId}", CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(created.Status, Is.EqualTo(OrderStatus.Confirmed));
        Assert.That(created.Total, Is.EqualTo(Deal1001.Amount));
        Assert.That(created.OrderNumber, Does.Match(@"^SO-\d{4}-000001$"));
        Assert.That(order!.ExternalRef, Is.EqualTo(Deal1001.DealId));
        Assert.That(order.IdempotencyKey, Is.EqualTo(request.IdempotencyKey));
        Assert.That(order.Lines.Select(l => (l.Sku, l.Quantity, l.UnitPrice)), Is.EqualTo(Deal1001.Lines.Select(l => (l.Sku, l.Quantity, l.UnitPrice))));
        Assert.That(await ReservedAsync("IND-BRG-001"), Is.EqualTo(reservedBefore + 40));
    }

    [Test]
    public async Task CreateOrder_LineNotAvailable_CreatesBackorder()
    {
        //SETUP
        using var client = Factory.CreateClient();
        var request = await OrderForDealAsync(client, DemoCatalog.Deals.Single(d => d.DealId == "D-1003"));

        //SUT
        using var response = await client.PostAsJsonAsync("/api/orders", request, CancellationToken);
        var created = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(created!.Status, Is.EqualTo(OrderStatus.Backorder));

        // D-1003 chiede 5 IND-MOT-003 con 3 disponibili: due da approvvigionare (M25).
        Assert.That(created.BackorderNote, Is.EqualTo("IND-MOT-003: 2 PZ da ordinare"));
    }

    [Test]
    public async Task CreateOrder_Backorder_KeepsTheNoteOnTheOrderAndOnARetry()
    {
        //SETUP
        using var client = Factory.CreateClient();
        var request = await OrderForDealAsync(client, DemoCatalog.Deals.Single(d => d.DealId == "D-1003"));

        //SUT: la riserva è già stata fatta, quindi la nota non sarebbe più ricavabile dalla giacenza corrente.
        using var first = await client.PostAsJsonAsync("/api/orders", request, CancellationToken);
        using var retry = await client.PostAsJsonAsync("/api/orders", request, CancellationToken);

        var created = await first.Content.ReadFromJsonAsync<CreateOrderResponse>(CancellationToken);
        var repeated = await retry.Content.ReadFromJsonAsync<CreateOrderResponse>(CancellationToken);

        Assert.That(repeated!.BackorderNote, Is.EqualTo(created!.BackorderNote));

        using var fetched = await client.GetAsync($"/api/orders/{created.OrderId}", CancellationToken);
        var order = await fetched.Content.ReadFromJsonAsync<OrderDto>(CancellationToken);

        Assert.That(order!.BackorderNote, Is.EqualTo(created.BackorderNote));
    }

    [Test]
    public async Task CreateOrder_AllLinesAvailable_HasNoBackorderNote()
    {
        //SETUP
        using var client = Factory.CreateClient();
        var request = await OrderForDealAsync(client, Deal1001);

        //SUT
        using var response = await client.PostAsJsonAsync("/api/orders", request, CancellationToken);
        var created = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(CancellationToken);

        Assert.That(created!.Status, Is.EqualTo(OrderStatus.Confirmed));
        Assert.That(created.BackorderNote, Is.Null);
    }

    [Test]
    public async Task CreateOrder_SameKeyTwice_ReturnsSameOrderAndReservesOnce()
    {
        //SETUP
        using var client = Factory.CreateClient();
        var request = await OrderForDealAsync(client, Deal1001);
        var reservedBefore = await ReservedAsync("IND-BRG-001");

        //SUT
        using var first = await client.PostAsJsonAsync("/api/orders", request, CancellationToken);
        using var second = await client.PostAsJsonAsync("/api/orders", request, CancellationToken);
        var firstOrder = await first.Content.ReadFromJsonAsync<CreateOrderResponse>(CancellationToken);
        var secondOrder = await second.Content.ReadFromJsonAsync<CreateOrderResponse>(CancellationToken);

        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(secondOrder, Is.EqualTo(firstOrder));
        Assert.That(await OrderCountAsync(request.IdempotencyKey), Is.EqualTo(1));
        Assert.That(await ReservedAsync("IND-BRG-001"), Is.EqualTo(reservedBefore + 40));
    }

    [Test]
    public async Task CreateOrder_ParallelRequestsWithSameKey_CreateSingleOrder()
    {
        //SETUP
        using var client = Factory.CreateClient();
        var request = await OrderForDealAsync(client, Deal1001);

        //SUT
        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => client.PostAsJsonAsync("/api/orders", request, CancellationToken)));

        try
        {
            var orders = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<CreateOrderResponse>(CancellationToken)));

            Assert.That(responses.Select(r => r.StatusCode), Is.All.AnyOf(HttpStatusCode.Created, HttpStatusCode.OK));
            Assert.That(responses.Count(r => r.StatusCode == HttpStatusCode.Created), Is.EqualTo(1));
            Assert.That(orders.Select(o => o!.OrderId).Distinct().Count(), Is.EqualTo(1));
            Assert.That(await OrderCountAsync(request.IdempotencyKey), Is.EqualTo(1));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Test]
    public async Task CreateOrder_UnknownSku_ReturnsNotFoundWithoutCreatingOrder()
    {
        //SETUP
        using var client = Factory.CreateClient();
        var request = await OrderForDealAsync(client, DemoCatalog.Deals.Single(d => d.DealId == "D-1007"));

        //SUT
        using var response = await client.PostAsJsonAsync("/api/orders", request, CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await ReadProblemCodeAsync(response), Is.EqualTo(ToolErrorCodes.NotFound));
        Assert.That(await OrderCountAsync(request.IdempotencyKey), Is.Zero);
    }

    [Test]
    public async Task CreateOrder_UnknownCustomer_ReturnsNotFound()
    {
        //SETUP
        using var client = Factory.CreateClient();
        var request = await OrderForDealAsync(client, Deal1001) with { CustomerId = 999_999 };

        //SUT
        using var response = await client.PostAsJsonAsync("/api/orders", request, CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await ReadProblemCodeAsync(response), Is.EqualTo(ToolErrorCodes.NotFound));
    }

    [TestCase("no-lines")]
    [TestCase("zero-quantity")]
    [TestCase("negative-price")]
    [TestCase("three-decimals")]
    [TestCase("missing-key")]
    [TestCase("missing-external-ref")]
    public async Task CreateOrder_InvalidRequest_ReturnsValidationError(string invalid)
    {
        //SETUP
        using var client = Factory.CreateClient();
        var valid = new CreateOrderRequest(1, [new OrderLineInput("IND-BRG-001", 1, 12.00m)], "D-1001", "o2c-D-1001-r1");
        var request = invalid switch
        {
            "no-lines" => valid with { Lines = [] },
            "zero-quantity" => valid with { Lines = [new OrderLineInput("IND-BRG-001", 0, 12.00m)] },
            "negative-price" => valid with { Lines = [new OrderLineInput("IND-BRG-001", 1, -1.00m)] },
            "three-decimals" => valid with { Lines = [new OrderLineInput("IND-BRG-001", 1, 12.001m)] },
            "missing-key" => valid with { IdempotencyKey = "" },
            "missing-external-ref" => valid with { ExternalRef = " " },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid))
        };

        //SUT
        using var response = await client.PostAsJsonAsync("/api/orders", request, CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await ReadProblemCodeAsync(response), Is.EqualTo(ToolErrorCodes.ValidationError));
    }

    [Test]
    public async Task GetOrder_Unknown_ReturnsNotFound()
    {
        //SETUP
        using var client = Factory.CreateClient();

        //SUT
        using var response = await client.GetAsync($"/api/orders/{Guid.NewGuid()}", CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await ReadProblemCodeAsync(response), Is.EqualTo(ToolErrorCodes.NotFound));
    }

    [Test]
    public async Task DevReset_AfterOrder_RemovesOrdersAndRestoresStock()
    {
        //SETUP
        using var client = Factory.CreateClient();
        var request = await OrderForDealAsync(client, Deal1001);
        using var order = await client.PostAsJsonAsync("/api/orders", request, CancellationToken);

        //SUT
        using var reset = await client.PostAsync("/dev/reset", content: null, CancellationToken);

        Assert.That(order.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(reset.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(await OrderCountAsync(request.IdempotencyKey), Is.Zero);
        Assert.That(await ReservedAsync("IND-BRG-001"), Is.EqualTo(DemoCatalog.Products.Single(p => p.Sku == "IND-BRG-001").Reserved));
    }

    [Test]
    public async Task CreateOrder_UnreadableJson_ReturnsValidationErrorInsteadOfServerError()
    {
        //SETUP
        using HttpClient client = Factory.CreateClient();
        using var content = new StringContent("""{ "customerId": "uno" }""", System.Text.Encoding.UTF8, "application/json");

        //SUT
        using HttpResponseMessage response = await client.PostAsync("/api/orders", content, CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await ReadProblemCodeAsync(response), Is.EqualTo(ToolErrorCodes.ValidationError));
    }

    private static async Task<CreateOrderRequest> OrderForDealAsync(HttpClient client, DemoDeal deal)
    {
        var company = DemoCatalog.Companies.Single(c => c.CompanyId == deal.CompanyId);
        var customer = await client.GetFromJsonAsync<CustomerDto>($"/api/customers?vatNumber={company.VatNumber}", CancellationToken);

        return new CreateOrderRequest(
            customer!.CustomerId,
            deal.Lines.Select(l => new OrderLineInput(l.Sku, l.Quantity, l.UnitPrice)).ToList(),
            deal.DealId,
            IdempotencyKey.From(deal.DealId, 1));
    }

    private Task<int> ReservedAsync(string sku) =>
        WithDbAsync(db => db.StockLevels.Where(s => s.Product.Sku == sku).Select(s => s.Reserved).SingleAsync(CancellationToken));

    private Task<int> OrderCountAsync(string idempotencyKey) =>
        WithDbAsync(db => db.Orders.CountAsync(o => o.IdempotencyKey == idempotencyKey, CancellationToken));
}
