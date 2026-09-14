using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Dusiburg.AI.O2C.Shared.Correlation;

namespace Dusiburg.AI.O2C.Erp.Api.Tests;

public class RootEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetRoot_WithCorrelationId_ReturnsOkAndEchoesIt()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(CorrelationId.HeaderName, "test-corr-1");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("test-corr-1", Assert.Single(response.Headers.GetValues(CorrelationId.HeaderName)));
    }

    [Fact]
    public async Task GetRoot_WithoutCorrelationId_ReturnsGeneratedId()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(CorrelationId.IsValid(Assert.Single(response.Headers.GetValues(CorrelationId.HeaderName))));
    }

    [Fact]
    public async Task GetHealth_ReturnsOk()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
