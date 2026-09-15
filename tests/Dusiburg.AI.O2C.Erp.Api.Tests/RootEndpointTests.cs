using System.Net;
using Dusiburg.AI.O2C.Shared.Correlation;

namespace Dusiburg.AI.O2C.Erp.Api.Tests;

public class RootEndpointTests : ErpApiTestBase
{
    [Test]
    public async Task GetRoot_WithCorrelationId_ReturnsOkAndEchoesIt()
    {
        //SETUP
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(CorrelationId.HeaderName, "test-corr-1");

        //SUT
        using var response = await client.SendAsync(request, CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.GetValues(CorrelationId.HeaderName), Is.EqualTo(new[] { "test-corr-1" }));
    }

    [Test]
    public async Task GetRoot_WithoutCorrelationId_ReturnsGeneratedId()
    {
        //SETUP
        using var client = Factory.CreateClient();

        //SUT
        using var response = await client.GetAsync("/", CancellationToken);
        var values = response.Headers.GetValues(CorrelationId.HeaderName).ToList();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(values, Has.Count.EqualTo(1));
        Assert.That(CorrelationId.IsValid(values[0]), Is.True);
    }

    [Test]
    public async Task GetHealth_WithDatabase_ReturnsOk()
    {
        //SETUP
        using var client = Factory.CreateClient();

        //SUT
        using var response = await client.GetAsync("/health", CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
