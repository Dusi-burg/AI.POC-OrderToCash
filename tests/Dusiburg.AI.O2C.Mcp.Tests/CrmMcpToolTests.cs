using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Dusiburg.AI.O2C.Crm.Data;
using Dusiburg.AI.O2C.Crm.Data.Seed;
using Dusiburg.AI.O2C.Crm.Mcp.Crm;
using Dusiburg.AI.O2C.Crm.Mcp.Tools;
using Dusiburg.AI.O2C.DbInit;
using Dusiburg.AI.O2C.Mcp.Tests.Support;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Demo;
using Dusiburg.AI.O2C.Shared.Errors;
using Dusiburg.AI.O2C.Shared.Telemetry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace Dusiburg.AI.O2C.Mcp.Tests;

/// <summary>Tool di <c>crm-mcp</c> (2.6) su un database dedicato alla classe, riportato ai dati demo prima di ogni test.</summary>
public class CrmMcpToolTests
{
    private const string ApiKey = "test-crm-mcp-key";

    private string _connectionString = null!;
    private WebApplicationFactory<CrmTools> _factory = null!;

    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [OneTimeSetUp]
    public async Task CreateDatabaseAndFactory()
    {
        _connectionString = O2CDatabaseInitializer.LocalConnectionStringFor($"O2C_Test_{Guid.NewGuid():N}");

        await O2CDatabaseInitializer.RecreateAsync(_connectionString, seed: true, CancellationToken);

        _factory = new WebApplicationFactory<CrmTools>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:sql", _connectionString);
            builder.UseSetting("CRM_MCP_API_KEY", ApiKey);
        });
    }

    [SetUp]
    public async Task ResetCrm()
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        await CrmSeeder.ResetAsync(scope.ServiceProvider.GetRequiredService<CrmDbContext>(), DateTimeOffset.UtcNow, CancellationToken);
    }

    [OneTimeTearDown]
    public async Task DisposeFactoryAndDropDatabase()
    {
        await _factory.DisposeAsync();
        await O2CDatabaseInitializer.DropAsync(_connectionString);
    }

    [Test]
    public async Task ListTools_ReturnsExactlyTheCrmToolsWithRequiredParameters()
    {
        //SUT
        var tools = await McpTestClient.ListToolsAsync(_factory, ApiKey, CancellationToken);
        var status = tools[CrmToolNames.UpdateDeal].ProtocolTool.InputSchema.GetProperty("properties").GetProperty("status");

        Assert.That(tools.Keys, Is.EquivalentTo(new[] { CrmToolNames.GetDeal, CrmToolNames.GetCompany, CrmToolNames.UpdateDeal }));
        Assert.That(tools[CrmToolNames.GetDeal].RequiredParameters(), Is.EquivalentTo(new[] { "dealId" }));
        Assert.That(tools[CrmToolNames.GetCompany].RequiredParameters(), Is.EquivalentTo(new[] { "companyId" }));
        Assert.That(tools[CrmToolNames.UpdateDeal].RequiredParameters(), Is.EquivalentTo(new[] { "dealId", "status" }));
        Assert.That(status.GetProperty("enum").EnumerateArray().Select(e => e.GetString()), Is.EquivalentTo(Enum.GetNames<DealStatus>()));
        Assert.That(tools.Values.Select(t => t.ProtocolTool.OutputSchema), Is.All.Not.Null);
    }

    [Test]
    public async Task GetDeal_Existing_ReturnsLinesRevisionAndStage()
    {
        //SETUP
        var expected = DemoCatalog.Deals.Single(d => d.DealId == "D-1001");

        //SUT
        var deal = (await CallAsync(CrmToolNames.GetDeal, new() { ["dealId"] = expected.DealId })).ReadStructured<DealDto>();

        Assert.That((deal.DealId, deal.Revision, deal.Stage, deal.CompanyId, deal.Currency, deal.Amount),
            Is.EqualTo((expected.DealId, 1, "ContractSent", expected.CompanyId, expected.Currency, expected.Amount)));
        Assert.That(deal.LineItems, Is.EqualTo(expected.Lines.Select(l => new DealLineItemDto(l.Sku, l.Quantity, l.UnitPrice))));
    }

    [Test]
    public async Task GetDeal_Unknown_ReturnsNotFound()
    {
        //SUT
        var error = (await CallAsync(CrmToolNames.GetDeal, new() { ["dealId"] = "D-9999" })).ReadError();

        Assert.That(error, Is.EqualTo(new ToolError(ToolErrorCodes.NotFound, "Deal D-9999 non trovato.")));
    }

    [Test]
    public async Task GetDeal_MissingDealId_ReturnsValidationError()
    {
        //SUT
        var error = (await CallAsync(CrmToolNames.GetDeal, [])).ReadError();

        Assert.That(error.Code, Is.EqualTo(ToolErrorCodes.ValidationError));
    }

    [Test]
    public async Task GetCompany_Existing_ReturnsCompany()
    {
        //SETUP
        var expected = DemoCatalog.Companies.Single(c => c.CompanyId == "C-04");

        //SUT
        var company = (await CallAsync(CrmToolNames.GetCompany, new() { ["companyId"] = expected.CompanyId })).ReadStructured<CompanyDto>();

        Assert.That(company, Is.EqualTo(new CompanyDto(expected.CompanyId, expected.Name, expected.VatNumber, expected.Email, expected.Address)));
    }

    [Test]
    public async Task GetCompany_Unknown_ReturnsNotFound()
    {
        //SUT
        var error = (await CallAsync(CrmToolNames.GetCompany, new() { ["companyId"] = "C-99" })).ReadError();

        Assert.That(error.Code, Is.EqualTo(ToolErrorCodes.NotFound));
    }

    [Test]
    public async Task UpdateDeal_Valid_WritesStatusAndKeepsRevision()
    {
        //SUT
        var response = (await CallAsync(CrmToolNames.UpdateDeal, new()
        {
            ["dealId"] = "D-1001",
            ["status"] = nameof(DealStatus.OrderCreated),
            ["erpOrderNumber"] = "SO-2026-000001",
            ["note"] = "Ordine creato"
        })).ReadStructured<UpdateDealResponse>();

        var deal = (await CallAsync(CrmToolNames.GetDeal, new() { ["dealId"] = "D-1001" })).ReadStructured<DealDto>();

        await using var scope = _factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<CrmDbContext>().Deals
            .AsNoTracking()
            .SingleAsync(d => d.Code == "D-1001", CancellationToken);

        Assert.That(response, Is.EqualTo(new UpdateDealResponse(true)));
        Assert.That(deal.Revision, Is.EqualTo(1));
        Assert.That((stored.O2CStatus, stored.ErpOrderNumber, stored.LastNote), Is.EqualTo(((DealStatus?)DealStatus.OrderCreated, "SO-2026-000001", "Ordine creato")));
    }

    [Test]
    public async Task UpdateDeal_WithoutOptionalFields_IsAccepted()
    {
        //SUT
        var response = (await CallAsync(CrmToolNames.UpdateDeal, new() { ["dealId"] = "D-1006", ["status"] = nameof(DealStatus.Discarded) }))
            .ReadStructured<UpdateDealResponse>();

        Assert.That(response.Ok, Is.True);
    }

    [TestCase("Shipped")]
    [TestCase(1)]
    public async Task UpdateDeal_StatusOutsideEnum_ReturnsValidationError(object status)
    {
        //SUT
        var error = (await CallAsync(CrmToolNames.UpdateDeal, new() { ["dealId"] = "D-1001", ["status"] = status })).ReadError();

        Assert.That(error.Code, Is.EqualTo(ToolErrorCodes.ValidationError));
    }

    [Test]
    public async Task UpdateDeal_NoteTooLong_ReturnsValidationError()
    {
        //SUT
        var error = (await CallAsync(CrmToolNames.UpdateDeal, new()
        {
            ["dealId"] = "D-1001",
            ["status"] = nameof(DealStatus.Failed),
            ["note"] = new string('x', MockCrmClient.NoteMaxLength + 1)
        })).ReadError();

        Assert.That(error.Code, Is.EqualTo(ToolErrorCodes.ValidationError));
    }

    [Test]
    public async Task UpdateDeal_Unknown_ReturnsNotFound()
    {
        //SUT
        var error = (await CallAsync(CrmToolNames.UpdateDeal, new() { ["dealId"] = "D-9999", ["status"] = nameof(DealStatus.Failed) })).ReadError();

        Assert.That(error.Code, Is.EqualTo(ToolErrorCodes.NotFound));
    }

    [Test]
    public async Task CallTool_EmitsSpanWithToolNameCorrelationIdAndOutcome()
    {
        //SETUP
        var spans = new ConcurrentQueue<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == O2CTelemetry.Sources.McpCrm,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Enqueue
        };

        ActivitySource.AddActivityListener(listener);

        //SUT
        await CallAsync(CrmToolNames.GetDeal, new() { ["dealId"] = "D-1001" }, "fase2-span-ok");
        await CallAsync(CrmToolNames.GetDeal, new() { ["dealId"] = "D-9999" }, "fase2-span-ko");

        Assert.That(
            spans.Select(s => (s.DisplayName, Tag(s, O2CTelemetry.Attributes.ToolName), Tag(s, O2CTelemetry.Attributes.CorrelationId), Tag(s, O2CTelemetry.Attributes.ToolOutcome))),
            Is.EqualTo(new[]
            {
                ("mcp.tool get_deal", "get_deal", "fase2-span-ok", "ok"),
                ("mcp.tool get_deal", "get_deal", "fase2-span-ko", "error:NOT_FOUND")
            }));
        Assert.That(spans.Select(s => s.Duration), Is.All.GreaterThan(TimeSpan.Zero));
    }

    [TestCase(null)]
    [TestCase("chiave-errata")]
    public async Task Mcp_WithoutValidApiKey_ReturnsUnauthorized(string? apiKey)
    {
        //SUT
        var (status, code) = await McpTestClient.PostToolsListAsync(_factory, apiKey, CancellationToken);

        Assert.That(status, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(code, Is.EqualTo(ToolErrorCodes.Unauthorized));
    }

    private Task<CallToolResult> CallAsync(string toolName, Dictionary<string, object?> arguments, string? correlationId = null) =>
        McpTestClient.CallToolAsync(_factory, ApiKey, toolName, arguments, correlationId, CancellationToken);

    private static string? Tag(Activity activity, string name) => activity.GetTagItem(name) as string;
}
