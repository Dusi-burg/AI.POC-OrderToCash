using System.Diagnostics;
using System.Net;
using Dusiburg.AI.O2C.Mcp.Tests.Support;
using Dusiburg.AI.O2C.Shared.Contracts;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Dusiburg.AI.O2C.Shared.Demo;
using Dusiburg.AI.O2C.Shared.Errors;
using Dusiburg.AI.O2C.Shared.Idempotency;

namespace Dusiburg.AI.O2C.Mcp.Tests;

/// <summary>Tool di <c>erp-mcp</c> (2.6) su dati demo, errori strutturati, API key e correlazione.</summary>
public class ErpMcpToolTests : ErpMcpTestBase
{
    private static readonly DemoDeal Deal1001 = DemoCatalog.Deals.Single(d => d.DealId == "D-1001");

    [Test]
    public async Task ListTools_ReturnsExactlyTheErpToolsWithRequiredParameters()
    {
        //SUT
        var tools = await McpTestClient.ListToolsAsync(ErpMcp, ApiKey, CancellationToken);

        Assert.That(tools.Keys, Is.EquivalentTo(new[]
        {
            ErpToolNames.GetCustomer, ErpToolNames.CreateCustomer, ErpToolNames.CheckStock, ErpToolNames.CreateOrder, ErpToolNames.GetOrder
        }));
        Assert.That(tools[ErpToolNames.GetCustomer].RequiredParameters(), Is.Empty);
        Assert.That(tools[ErpToolNames.CreateCustomer].RequiredParameters(), Is.EquivalentTo(new[] { "name", "vatNumber", "email", "address" }));
        Assert.That(tools[ErpToolNames.CheckStock].RequiredParameters(), Is.EquivalentTo(new[] { "sku", "quantity" }));
        Assert.That(tools[ErpToolNames.CreateOrder].RequiredParameters(), Is.EquivalentTo(new[] { "customerId", "lines", "externalRef", "idempotencyKey" }));
        Assert.That(tools[ErpToolNames.GetOrder].RequiredParameters(), Is.EquivalentTo(new[] { "orderId" }));
        Assert.That(tools.Values.Select(t => t.ProtocolTool.OutputSchema), Is.All.Not.Null);
    }

    [Test]
    public async Task ListTools_MarksOnlyCreateOrderAsSensitive()
    {
        //SUT
        var tools = await McpTestClient.ListToolsAsync(ErpMcp, ApiKey, CancellationToken);
        var createOrder = tools[ErpToolNames.CreateOrder].ProtocolTool;

        Assert.That(createOrder.Annotations?.DestructiveHint, Is.True);
        Assert.That(createOrder.Meta?[ToolMetadata.Sensitive]?.GetValue<bool>(), Is.True);
        Assert.That(tools.Values.Where(t => t.ProtocolTool.Meta?[ToolMetadata.Sensitive] is not null).Select(t => t.Name), Is.EqualTo(new[] { ErpToolNames.CreateOrder }));
        Assert.That(tools[ErpToolNames.GetCustomer].ProtocolTool.Annotations?.ReadOnlyHint, Is.True);
    }

    [Test]
    public async Task GetCustomer_ByVatNumber_ReturnsDemoCustomer()
    {
        //SETUP
        var expected = DemoCatalog.Customers[0];

        //SUT
        var customer = (await CallAsync(ErpToolNames.GetCustomer, new() { ["vatNumber"] = expected.VatNumber }))
            .ReadStructured<GetCustomerResponse>().Customer;

        Assert.That(customer, Is.Not.Null);
        Assert.That(customer!.CustomerId, Is.Positive);
        Assert.That((customer.Name, customer.VatNumber, customer.Email, customer.CreditLimit, customer.IsBlocked),
            Is.EqualTo((expected.Name, expected.VatNumber, expected.Email, expected.CreditLimit, expected.IsBlocked)));
    }

    [Test]
    public async Task GetCustomer_BlockedByEmail_ReturnsIsBlocked()
    {
        //SETUP
        var blocked = DemoCatalog.Customers.Single(c => c.IsBlocked);

        //SUT
        var customer = (await CallAsync(ErpToolNames.GetCustomer, new() { ["email"] = blocked.Email }))
            .ReadStructured<GetCustomerResponse>().Customer;

        Assert.That(customer?.IsBlocked, Is.True);
    }

    [Test]
    public async Task GetCustomer_Unknown_ReturnsNullCustomerWithoutError()
    {
        //SUT
        var result = await CallAsync(ErpToolNames.GetCustomer, new() { ["vatNumber"] = "IT99999999999" });

        Assert.That(result.ReadStructured<GetCustomerResponse>(), Is.EqualTo(new GetCustomerResponse(null)));
        Assert.That(result.Text(), Is.EqualTo("""{"customer":null}"""));
    }

    [Test]
    public async Task GetCustomer_WithoutParameters_ReturnsValidationError()
    {
        //SUT
        var error = (await CallAsync(ErpToolNames.GetCustomer, new() { ["vatNumber"] = " " })).ReadError();

        Assert.That(error.Code, Is.EqualTo(ToolErrorCodes.ValidationError));
    }

    [Test]
    public async Task CreateCustomer_NewCompany_IsThenFoundByGetCustomer()
    {
        //SETUP
        var company = DemoCatalog.Companies.Single(c => c.CompanyId == "C-04");

        //SUT
        var created = (await CallAsync(ErpToolNames.CreateCustomer, new()
        {
            ["name"] = company.Name,
            ["vatNumber"] = company.VatNumber,
            ["email"] = company.Email,
            ["address"] = company.Address
        })).ReadStructured<CreateCustomerResponse>();

        var found = (await CallAsync(ErpToolNames.GetCustomer, new() { ["vatNumber"] = company.VatNumber }))
            .ReadStructured<GetCustomerResponse>().Customer;

        Assert.That(created.CustomerId, Is.Positive);
        Assert.That(found?.CustomerId, Is.EqualTo(created.CustomerId));
    }

    [Test]
    public async Task CreateCustomer_ExistingVatNumber_ReturnsConflict()
    {
        //SETUP
        var existing = DemoCatalog.Customers[0];

        //SUT
        var error = (await CallAsync(ErpToolNames.CreateCustomer, new()
        {
            ["name"] = existing.Name,
            ["vatNumber"] = existing.VatNumber,
            ["email"] = existing.Email,
            ["address"] = existing.Address
        })).ReadError();

        Assert.That(error.Code, Is.EqualTo(ToolErrorCodes.Conflict));
        Assert.That(error.Message, Does.Contain(existing.VatNumber));
    }

    [Test]
    public async Task CreateCustomer_InvalidEmail_ReturnsValidationErrorFromErp()
    {
        //SUT
        var error = (await CallAsync(ErpToolNames.CreateCustomer, new()
        {
            ["name"] = "Cliente di prova",
            ["vatNumber"] = "IT00000000001",
            ["email"] = "senza-chiocciola",
            ["address"] = "Via Roma 1"
        })).ReadError();

        Assert.That(error.Code, Is.EqualTo(ToolErrorCodes.ValidationError));
        Assert.That(error.Message, Does.Contain("email"));
    }

    [TestCase("IND-BRG-001", 40, true)]
    [TestCase("IND-MOT-004", 1, false)]
    public async Task CheckStock_DemoProduct_MatchesSeed(string sku, int quantity, bool available)
    {
        //SETUP
        var product = DemoCatalog.Products.Single(p => p.Sku == sku);

        //SUT
        var stock = (await CallAsync(ErpToolNames.CheckStock, new() { ["sku"] = sku, ["quantity"] = quantity }))
            .ReadStructured<StockCheckDto>();

        Assert.That(stock, Is.EqualTo(new StockCheckDto(sku, available, product.OnHand, product.LeadTimeDays)));
    }

    [Test]
    public async Task CheckStock_UnknownSku_ReturnsNotFound()
    {
        //SUT
        var error = (await CallAsync(ErpToolNames.CheckStock, new() { ["sku"] = "IND-SEN-999", ["quantity"] = 1 })).ReadError();

        Assert.That(error.Code, Is.EqualTo(ToolErrorCodes.NotFound));
    }

    [Test]
    public async Task CheckStock_ZeroQuantity_ReturnsValidationError()
    {
        //SUT
        var error = (await CallAsync(ErpToolNames.CheckStock, new() { ["sku"] = "IND-BRG-001", ["quantity"] = 0 })).ReadError();

        Assert.That(error.Code, Is.EqualTo(ToolErrorCodes.ValidationError));
    }

    [Test]
    public async Task CreateOrder_Deal1001_CreatesConfirmedOrderReadableWithGetOrder()
    {
        //SETUP
        var arguments = await OrderArgumentsAsync(Deal1001);

        //SUT
        var created = (await CallAsync(ErpToolNames.CreateOrder, arguments)).ReadStructured<CreateOrderResponse>();
        var order = (await CallAsync(ErpToolNames.GetOrder, new() { ["orderId"] = created.OrderId })).ReadStructured<OrderDto>();

        Assert.That(created.Status, Is.EqualTo(OrderStatus.Confirmed));
        Assert.That(created.Total, Is.EqualTo(Deal1001.Amount));
        Assert.That(created.OrderNumber, Does.Match(@"^SO-\d{4}-000001$"));
        Assert.That(order.OrderNumber, Is.EqualTo(created.OrderNumber));
        Assert.That(order.ExternalRef, Is.EqualTo(Deal1001.DealId));
        Assert.That(order.IdempotencyKey, Is.EqualTo(arguments["idempotencyKey"]));
        Assert.That(order.Lines.Select(l => (l.Sku, l.Quantity, l.UnitPrice)), Is.EqualTo(Deal1001.Lines.Select(l => (l.Sku, l.Quantity, l.UnitPrice))));
    }

    [Test]
    public async Task CreateOrder_LineNotAvailable_CreatesBackorder()
    {
        //SETUP
        var arguments = await OrderArgumentsAsync(DemoCatalog.Deals.Single(d => d.DealId == "D-1003"));

        //SUT
        var created = (await CallAsync(ErpToolNames.CreateOrder, arguments)).ReadStructured<CreateOrderResponse>();

        Assert.That(created.Status, Is.EqualTo(OrderStatus.Backorder));
    }

    [Test]
    public async Task CreateOrder_SameKeyTwice_ReturnsSameOrder()
    {
        //SETUP
        var arguments = await OrderArgumentsAsync(Deal1001);

        //SUT
        var first = (await CallAsync(ErpToolNames.CreateOrder, arguments)).ReadStructured<CreateOrderResponse>();
        var second = (await CallAsync(ErpToolNames.CreateOrder, arguments)).ReadStructured<CreateOrderResponse>();

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public async Task CreateOrder_UnknownSku_ReturnsNotFound()
    {
        //SETUP
        var arguments = await OrderArgumentsAsync(DemoCatalog.Deals.Single(d => d.DealId == "D-1007"));

        //SUT
        var error = (await CallAsync(ErpToolNames.CreateOrder, arguments)).ReadError();

        Assert.That(error.Code, Is.EqualTo(ToolErrorCodes.NotFound));
        Assert.That(error.Message, Does.Contain("IND-SEN-999"));
    }

    [Test]
    public async Task CreateOrder_MissingLines_ReturnsValidationError()
    {
        //SETUP
        var arguments = await OrderArgumentsAsync(Deal1001);
        arguments.Remove("lines");

        //SUT
        var error = (await CallAsync(ErpToolNames.CreateOrder, arguments)).ReadError();

        Assert.That(error.Code, Is.EqualTo(ToolErrorCodes.ValidationError));
        Assert.That(error.Message, Does.Contain("lines"));
    }

    [Test]
    public async Task CreateOrder_EmptyLines_ReturnsValidationErrorFromErp()
    {
        //SETUP
        var arguments = await OrderArgumentsAsync(Deal1001);
        arguments["lines"] = Array.Empty<OrderLineInput>();

        //SUT
        var error = (await CallAsync(ErpToolNames.CreateOrder, arguments)).ReadError();

        Assert.That(error.Code, Is.EqualTo(ToolErrorCodes.ValidationError));
    }

    [Test]
    public async Task GetOrder_Unknown_ReturnsNotFound()
    {
        //SUT
        var error = (await CallAsync(ErpToolNames.GetOrder, new() { ["orderId"] = Guid.NewGuid() })).ReadError();

        Assert.That(error.Code, Is.EqualTo(ToolErrorCodes.NotFound));
    }

    [Test]
    public async Task GetOrder_NotAGuid_ReturnsValidationError()
    {
        //SUT
        var error = (await CallAsync(ErpToolNames.GetOrder, new() { ["orderId"] = "SO-2026-000001" })).ReadError();

        Assert.That(error.Code, Is.EqualTo(ToolErrorCodes.ValidationError));
    }

    [Test]
    public async Task CheckStock_ErpApiServerError_ReturnsUpstreamUnavailable()
    {
        //SETUP
        Upstream.Simulate = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        //SUT
        var error = (await CallAsync(ErpToolNames.CheckStock, new() { ["sku"] = "IND-BRG-001", ["quantity"] = 1 })).ReadError();

        Assert.That(error.Code, Is.EqualTo(ToolErrorCodes.UpstreamUnavailable));
    }

    [Test]
    public async Task CheckStock_ErpApiTimeout_ReturnsUpstreamUnavailable()
    {
        //SETUP
        Upstream.Simulate = async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        };

        //SUT
        var error = (await CallAsync(ErpToolNames.CheckStock, new() { ["sku"] = "IND-BRG-001", ["quantity"] = 1 })).ReadError();

        Assert.That(error.Code, Is.EqualTo(ToolErrorCodes.UpstreamUnavailable));
    }

    [Test]
    public async Task CheckStock_UnexpectedException_ReturnsInternalWithoutDetails()
    {
        //SETUP
        Upstream.Simulate = (_, _) => throw new InvalidOperationException("dettaglio interno riservato");

        //SUT
        var error = (await CallAsync(ErpToolNames.CheckStock, new() { ["sku"] = "IND-BRG-001", ["quantity"] = 1 })).ReadError();

        Assert.That(error.Code, Is.EqualTo(ToolErrorCodes.Internal));
        Assert.That(error.Message, Does.Not.Contain("dettaglio interno"));
    }

    [Test]
    public async Task CheckStock_ForwardsCorrelationIdToErpApi()
    {
        //SETUP
        const string correlationId = "fase2-correlazione-erp-001";

        //SUT
        var result = await CallAsync(ErpToolNames.CheckStock, new() { ["sku"] = "IND-BRG-001", ["quantity"] = 1 }, correlationId);

        Assert.That(result.IsError, Is.Not.True);
        Assert.That(Upstream.CorrelationIds, Is.EqualTo(new[] { correlationId }));
    }

    [TestCase(null)]
    [TestCase("chiave-errata")]
    public async Task Mcp_WithoutValidApiKey_ReturnsUnauthorized(string? apiKey)
    {
        //SUT
        var (status, code) = await McpTestClient.PostToolsListAsync(ErpMcp, apiKey, CancellationToken);

        Assert.That(status, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(code, Is.EqualTo(ToolErrorCodes.Unauthorized));
    }

    /// <summary>Argomenti di <c>create_order</c> per un deal demo, con il cliente cercato tramite <c>get_customer</c>.</summary>
    private async Task<Dictionary<string, object?>> OrderArgumentsAsync(DemoDeal deal)
    {
        var company = DemoCatalog.Companies.Single(c => c.CompanyId == deal.CompanyId);
        var customer = (await CallAsync(ErpToolNames.GetCustomer, new() { ["vatNumber"] = company.VatNumber }))
            .ReadStructured<GetCustomerResponse>().Customer;

        Assert.That(customer, Is.Not.Null, $"Cliente ERP per {company.CompanyId} non trovato.");

        return new()
        {
            ["customerId"] = customer!.CustomerId,
            ["lines"] = deal.Lines.Select(l => new OrderLineInput(l.Sku, l.Quantity, l.UnitPrice)).ToList(),
            ["externalRef"] = deal.DealId,
            ["idempotencyKey"] = IdempotencyKey.From(deal.DealId, 1)
        };
    }
}
