using System.Text.Json;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Dusiburg.AI.O2C.Shared.Errors;

namespace Dusiburg.AI.O2C.Mcp.Tests;

public class ContractSerializationTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Test]
    public void ToolErrorResponse_SerializesAsErrorEnvelope()
    {
        var json = JsonSerializer.Serialize(ToolErrorResponse.Create(ToolErrorCodes.NotFound, "Deal D-9999 non trovato"), Web);

        Assert.That(json, Is.EqualTo("""{"error":{"code":"NOT_FOUND","message":"Deal D-9999 non trovato"}}"""));
    }

    [Test]
    public void UpdateDealRequest_SerializesStatusByName()
    {
        var json = JsonSerializer.Serialize(new UpdateDealRequest("D-1001", "SO-2026-000001", DealStatus.OrderCreated, null), Web);

        Assert.That(json, Does.Contain("\"status\":\"OrderCreated\""));
    }

    [TestCase("""{"dealId":"D-1001","status":"Shipped"}""")]
    [TestCase("""{"dealId":"D-1001","status":1}""")]
    public void UpdateDealRequest_StatusOutsideEnum_IsRejected(string json)
    {
        Assert.That(() => JsonSerializer.Deserialize<UpdateDealRequest>(json, Web), Throws.TypeOf<JsonException>());
    }

    [Test]
    public void DealDto_ExposesRevision()
    {
        var deal = new DealDto("D-1001", 3, "Cuscinetti linea 2", 4200m, "EUR", "ClosedWon", "C-01", [new DealLineItemDto("IND-BRG-001", 10, 420m)]);

        var json = JsonSerializer.Serialize(deal, Web);

        Assert.That(json, Does.Contain("\"revision\":3"));
    }

    [Test]
    public void CreateOrderResponse_SerializesStatusByName()
    {
        var json = JsonSerializer.Serialize(new CreateOrderResponse(Guid.CreateVersion7(), "SO-2026-000001", 4200m, OrderStatus.Backorder), Web);

        Assert.That(json, Does.Contain("\"status\":\"Backorder\""));
    }
}
