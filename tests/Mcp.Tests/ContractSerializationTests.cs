using System.Text.Json;
using O2C.Shared.Contracts.Crm;
using O2C.Shared.Contracts.Erp;
using O2C.Shared.Errors;

namespace O2C.Mcp.Tests;

public class ContractSerializationTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ToolErrorResponse_SerializesAsErrorEnvelope()
    {
        var json = JsonSerializer.Serialize(ToolErrorResponse.Create(ToolErrorCodes.NotFound, "Deal D-9999 non trovato"), Web);

        Assert.Equal("""{"error":{"code":"NOT_FOUND","message":"Deal D-9999 non trovato"}}""", json);
    }

    [Fact]
    public void UpdateDealRequest_SerializesStatusByName()
    {
        var json = JsonSerializer.Serialize(new UpdateDealRequest("D-1001", "SO-2026-000001", DealStatus.OrderCreated, null), Web);

        Assert.Contains("\"status\":\"OrderCreated\"", json);
    }

    [Theory]
    [InlineData("""{"dealId":"D-1001","status":"Shipped"}""")]
    [InlineData("""{"dealId":"D-1001","status":1}""")]
    public void UpdateDealRequest_StatusOutsideEnum_IsRejected(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateDealRequest>(json, Web));
    }

    [Fact]
    public void DealDto_ExposesRevision()
    {
        var deal = new DealDto("D-1001", 3, "Cuscinetti linea 2", 4200m, "EUR", "ClosedWon", "C-01", [new DealLineItemDto("IND-BRG-001", 10, 420m)]);

        var json = JsonSerializer.Serialize(deal, Web);

        Assert.Contains("\"revision\":3", json);
    }

    [Fact]
    public void CreateOrderResponse_SerializesStatusByName()
    {
        var json = JsonSerializer.Serialize(new CreateOrderResponse(Guid.CreateVersion7(), "SO-2026-000001", 4200m, OrderStatus.Backorder), Web);

        Assert.Contains("\"status\":\"Backorder\"", json);
    }
}
