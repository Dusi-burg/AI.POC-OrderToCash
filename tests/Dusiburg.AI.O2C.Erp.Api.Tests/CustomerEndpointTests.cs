using System.Net;
using System.Net.Http.Json;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Dusiburg.AI.O2C.Shared.Demo;
using Dusiburg.AI.O2C.Shared.Errors;

namespace Dusiburg.AI.O2C.Erp.Api.Tests;

public class CustomerEndpointTests : ErpApiTestBase
{
    [Test]
    public async Task GetCustomer_ByVatNumber_ReturnsCustomer()
    {
        //SETUP
        var expected = DemoCatalog.Customers.Single(c => c.IsBlocked);
        using var client = Factory.CreateClient();

        //SUT
        var customer = await client.GetFromJsonAsync<CustomerDto>($"/api/customers?vatNumber={expected.VatNumber}", CancellationToken);

        Assert.That(customer!.Name, Is.EqualTo(expected.Name));
        Assert.That(customer.IsBlocked, Is.True);
        Assert.That(customer.CreditLimit, Is.EqualTo(expected.CreditLimit));
    }

    [Test]
    public async Task GetCustomer_ByEmail_ReturnsCustomer()
    {
        //SETUP
        var expected = DemoCatalog.Customers[0];
        using var client = Factory.CreateClient();

        //SUT
        var customer = await client.GetFromJsonAsync<CustomerDto>($"/api/customers?email={Uri.EscapeDataString(expected.Email)}", CancellationToken);

        Assert.That(customer!.VatNumber, Is.EqualTo(expected.VatNumber));
    }

    [Test]
    public async Task GetCustomer_WithoutVatNumberAndEmail_ReturnsValidationError()
    {
        //SETUP
        using var client = Factory.CreateClient();

        //SUT
        using var response = await client.GetAsync("/api/customers", CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await ReadProblemCodeAsync(response), Is.EqualTo(ToolErrorCodes.ValidationError));
    }

    [Test]
    public async Task GetCustomer_Unknown_ReturnsNotFound()
    {
        //SETUP
        using var client = Factory.CreateClient();

        //SUT
        using var response = await client.GetAsync("/api/customers?vatNumber=IT99999999999", CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await ReadProblemCodeAsync(response), Is.EqualTo(ToolErrorCodes.NotFound));
    }

    [Test]
    public async Task CreateCustomer_New_ReturnsCreatedAndIsReadable()
    {
        //SETUP
        var company = DemoCatalog.Companies.Single(c => c.CompanyId == "C-04");
        using var client = Factory.CreateClient();

        //SUT
        using var response = await client.PostAsJsonAsync(
            "/api/customers",
            new CreateCustomerRequest(company.Name, company.VatNumber, company.Email, company.Address),
            CancellationToken);
        var created = await response.Content.ReadFromJsonAsync<CreateCustomerResponse>(CancellationToken);
        var customer = await client.GetFromJsonAsync<CustomerDto>($"/api/customers?vatNumber={company.VatNumber}", CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(customer!.CustomerId, Is.EqualTo(created!.CustomerId));
        Assert.That(customer.IsBlocked, Is.False);
    }

    [Test]
    public async Task CreateCustomer_ExistingVatNumber_ReturnsConflict()
    {
        //SETUP
        var existing = DemoCatalog.Customers[0];
        using var client = Factory.CreateClient();

        //SUT
        using var response = await client.PostAsJsonAsync(
            "/api/customers",
            new CreateCustomerRequest("Altro nome", existing.VatNumber, "altro@example.com", "Via Roma 1"),
            CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await ReadProblemCodeAsync(response), Is.EqualTo(ToolErrorCodes.Conflict));
    }

    [Test]
    public async Task CreateCustomer_MissingEmail_ReturnsValidationError()
    {
        //SETUP
        using var client = Factory.CreateClient();

        //SUT
        using var response = await client.PostAsJsonAsync(
            "/api/customers",
            new CreateCustomerRequest("Nuovo cliente", "IT55555555555", "", "Via Roma 1"),
            CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await ReadProblemCodeAsync(response), Is.EqualTo(ToolErrorCodes.ValidationError));
    }
}
