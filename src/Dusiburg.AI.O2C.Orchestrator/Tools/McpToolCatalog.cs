using System.Text.Json.Nodes;
using Dusiburg.AI.O2C.Shared.Contracts;
using ModelContextProtocol.Client;

namespace Dusiburg.AI.O2C.Orchestrator.Tools;

/// <summary>
/// Tool dei server MCP della Fase 2 (3.2). I client usano gli HttpClient di ServiceDefaults: correlation id del run,
/// resilienza e service discovery (<c>http://erp-mcp</c> sotto l'AppHost, porte fisse da appsettings.Development in CLI).
/// </summary>
public sealed class McpToolCatalog(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILoggerFactory loggerFactory)
    : IToolCatalog, IAsyncDisposable
{
    /// <summary>Header dell'API key dei server MCP (G2.3).</summary>
    public const string ApiKeyHeaderName = "X-Api-Key";

    private static readonly McpServerSettings[] Servers =
    [
        new(AgentToolNames.Erp, "ERP_MCP_URL", "ERP_MCP_API_KEY"),
        new(AgentToolNames.Crm, "CRM_MCP_URL", "CRM_MCP_API_KEY"),
    ];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<McpClient> _clients = [];
    private IReadOnlyList<AgentTool>? _tools;

    public async Task<IReadOnlyList<AgentTool>> GetToolsAsync(CancellationToken cancellationToken)
    {
        if (_tools is not null)
        {
            return _tools;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_tools is null)
            {
                var tools = new List<AgentTool>();

                foreach (var server in Servers)
                {
                    var client = await ConnectAsync(server, cancellationToken);
                    _clients.Add(client);

                    foreach (var tool in await client.ListToolsAsync(cancellationToken: cancellationToken))
                    {
                        tools.Add(new AgentTool($"{server.Prefix}.{tool.Name}", tool, IsSensitive(tool)));
                    }
                }

                _tools = tools;
            }

            return _tools;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }

        _gate.Dispose();
    }

    private async Task<McpClient> ConnectAsync(McpServerSettings server, CancellationToken cancellationToken)
    {
        var url = configuration[server.UrlSetting];
        var apiKey = configuration[server.ApiKeySetting];

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException($"{server.UrlSetting} e {server.ApiKeySetting} sono obbligatorie per il server MCP '{server.Prefix}'.");
        }

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(url),
                Name = $"{server.Prefix}-mcp",
                AdditionalHeaders = new Dictionary<string, string> { [ApiKeyHeaderName] = apiKey }
            },
            httpClientFactory.CreateClient($"{server.Prefix}-mcp"),
            loggerFactory,
            false);

        return await McpClient.CreateAsync(transport, null, loggerFactory, cancellationToken);
    }

    private static bool IsSensitive(McpClientTool tool) =>
        tool.ProtocolTool.Meta?[ToolMetadata.Sensitive] is JsonValue value && value.TryGetValue<bool>(out var sensitive) && sensitive;

    private sealed record McpServerSettings(string Prefix, string UrlSetting, string ApiKeySetting);
}
