using System.Net;
using System.Net.Http.Json;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Dusiburg.AI.O2C.Shared.Demo;
using Dusiburg.AI.O2C.Shared.Errors;

namespace Dusiburg.AI.O2C.Erp.Api.Tests;

public class StockEndpointTests : ErpApiTestBase
{
    [Test]
    public async Task GetStock_KnownSku_ReturnsStockLevel()
    {
        //SETUP
        var product = DemoCatalog.Products.Single(p => p.Sku == "IND-BRG-001");
        using var client = Factory.CreateClient();

        //SUT
        var stock = await client.GetFromJsonAsync<StockCheckDto>($"/api/stock/{product.Sku}?quantity=40", CancellationToken);

        Assert.That(stock, Is.EqualTo(new StockCheckDto(product.Sku, true, product.OnHand, product.LeadTimeDays)));
    }

    [Test]
    public async Task GetStock_WithReservedQuantity_UsesOnHandMinusReserved()
    {
        //SETUP
        var product = DemoCatalog.Products.Single(p => p.Sku == "IND-MOT-002");
        var free = product.OnHand - product.Reserved;
        using var client = Factory.CreateClient();

        Assert.That(product.Reserved, Is.GreaterThan(0));

        //SUT
        var enough = await client.GetFromJsonAsync<StockCheckDto>($"/api/stock/{product.Sku}?quantity={free}", CancellationToken);
        var tooMany = await client.GetFromJsonAsync<StockCheckDto>($"/api/stock/{product.Sku}?quantity={free + 1}", CancellationToken);

        Assert.That(enough!.Available, Is.True);
        Assert.That(tooMany!.Available, Is.False);
        Assert.That(tooMany.OnHand, Is.EqualTo(product.OnHand));
    }

    [Test]
    public async Task GetStock_UnknownSku_ReturnsNotFound()
    {
        //SETUP
        using var client = Factory.CreateClient();

        //SUT
        using var response = await client.GetAsync("/api/stock/IND-XXX-000?quantity=1", CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await ReadProblemCodeAsync(response), Is.EqualTo(ToolErrorCodes.NotFound));
    }

    [TestCase("")]
    [TestCase("?quantity=0")]
    [TestCase("?quantity=-3")]
    public async Task GetStock_InvalidQuantity_ReturnsValidationError(string query)
    {
        //SETUP
        using var client = Factory.CreateClient();

        //SUT
        using var response = await client.GetAsync($"/api/stock/IND-BRG-001{query}", CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await ReadProblemCodeAsync(response), Is.EqualTo(ToolErrorCodes.ValidationError));
    }
}
