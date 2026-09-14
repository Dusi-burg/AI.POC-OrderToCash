using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Dusiburg.AI.O2C.Shared.Correlation;

namespace Dusiburg.AI.O2C.Erp.Api.Tests;

public class RootEndpointTests
{
    private WebApplicationFactory<Program> _factory = null!;

    [OneTimeSetUp]
    public void CreateFactory()
    {
        _factory = new WebApplicationFactory<Program>();
    }

    [OneTimeTearDown]
    public void DisposeFactory()
    {
        _factory.Dispose();
    }

    [Test]
    public async Task GetRoot_WithCorrelationId_ReturnsOkAndEchoesIt()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(CorrelationId.HeaderName, "test-corr-1");

        using var response = await client.SendAsync(request, TestContext.CurrentContext.CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.GetValues(CorrelationId.HeaderName), Is.EqualTo(new[] { "test-corr-1" }));
    }

    [Test]
    public async Task GetRoot_WithoutCorrelationId_ReturnsGeneratedId()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/", TestContext.CurrentContext.CancellationToken);
        var values = response.Headers.GetValues(CorrelationId.HeaderName).ToList();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(values, Has.Count.EqualTo(1));
        Assert.That(CorrelationId.IsValid(values[0]), Is.True);
    }

    [Test]
    public async Task GetHealth_ReturnsOk()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.CurrentContext.CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
