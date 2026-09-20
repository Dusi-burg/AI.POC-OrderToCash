extern alias ErpApi;

using Dusiburg.AI.O2C.DbInit;
using Dusiburg.AI.O2C.Erp.Data;
using Dusiburg.AI.O2C.Erp.Data.Seed;
using Dusiburg.AI.O2C.Erp.Mcp.Erp;
using Dusiburg.AI.O2C.Erp.Mcp.Tools;
using Dusiburg.AI.O2C.Mcp.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using ModelContextProtocol.Protocol;

namespace Dusiburg.AI.O2C.Mcp.Tests;

/// <summary>
/// <c>Erp.Mcp</c> sopra un <c>Erp.Api</c> reale in-process (D36), con un database dedicato alla classe riportato al seed prima di ogni test;
/// <see cref="Upstream"/> permette di simulare i guasti di <c>Erp.Api</c>.
/// </summary>
public abstract class ErpMcpTestBase
{
    protected const string ApiKey = "test-erp-mcp-key";

    private string _connectionString = null!;
    private WebApplicationFactory<ErpApi::Program> _erpApi = null!;

    protected WebApplicationFactory<ErpTools> ErpMcp { get; private set; } = null!;

    internal UpstreamHandler Upstream { get; private set; } = null!;

    protected static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [OneTimeSetUp]
    public async Task CreateDatabaseAndFactories()
    {
        _connectionString = O2CDatabaseInitializer.LocalConnectionStringFor($"O2C_Test_{Guid.NewGuid():N}");

        await O2CDatabaseInitializer.RecreateAsync(_connectionString, seed: true, CancellationToken);

        _erpApi = new WebApplicationFactory<ErpApi::Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:sql", _connectionString));

        Upstream = new UpstreamHandler(_erpApi.Server.CreateHandler());

        ErpMcp = new WebApplicationFactory<ErpTools>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ERP_MCP_API_KEY", ApiKey);
            builder.UseSetting(ErpApiClient.BaseAddressConfigurationKey, "http://erp-api.test");

            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<ErpApiClient>().ConfigurePrimaryHttpMessageHandler(() => Upstream);

                // Stessa pipeline di resilienza di ServiceDefaults, con attese brevi: 500 e timeout non devono rallentare i test.
                // Va configurata con ConfigureAll, come in Crm.Web: la pipeline nasce da ConfigureHttpClientDefaults, dove il
                // builder non ha il nome del client, quindi le sue opzioni non stanno sotto il nome del client tipizzato.
                services.ConfigureAll<HttpStandardResilienceOptions>(options =>
                {
                    options.Retry.MaxRetryAttempts = 1;
                    options.Retry.Delay = TimeSpan.Zero;
                    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
                    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
                });
            });
        });
    }

    [SetUp]
    public async Task ResetErp()
    {
        Upstream.Reset();

        await using var scope = _erpApi.Services.CreateAsyncScope();

        await ErpSeeder.ResetAsync(scope.ServiceProvider.GetRequiredService<ErpDbContext>(), CancellationToken);
    }

    [OneTimeTearDown]
    public async Task DisposeFactoriesAndDropDatabase()
    {
        await ErpMcp.DisposeAsync();
        Upstream.Dispose();
        await _erpApi.DisposeAsync();
        await O2CDatabaseInitializer.DropAsync(_connectionString);
    }

    protected Task<CallToolResult> CallAsync(string toolName, Dictionary<string, object?> arguments, string? correlationId = null) =>
        McpTestClient.CallToolAsync(ErpMcp, ApiKey, toolName, arguments, correlationId, CancellationToken);
}
