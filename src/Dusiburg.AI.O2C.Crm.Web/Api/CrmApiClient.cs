using System.Net;
using System.Net.Http.Json;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Crm;
using Microsoft.AspNetCore.WebUtilities;

namespace Dusiburg.AI.O2C.Crm.Web.Api;

public enum CloseDealStatus
{
    Closed,
    NotFound,
    AlreadyClosed,
    EventNotPublished
}

/// <summary>Esito del comando di chiusura come lo mostra la UI; <see cref="Message"/> è il dettaglio scritto dal CRM.</summary>
public sealed record CloseDealResult(CloseDealStatus Status, string Message);

/// <summary>
/// Client tipizzato verso le API utente di <c>Crm.Mcp</c> (6.4): viste in lettura e comando di chiusura. Il correlation id
/// della richiesta web viaggia con il <c>CorrelationIdDelegatingHandler</c> di ServiceDefaults e diventa quello del workflow.
/// Un 404 sulle letture è un <c>null</c>; gli altri errori sono eccezioni, mostrate dalla pagina di errore.
/// </summary>
public sealed class CrmApiClient(HttpClient http)
{
    /// <summary>Nome del client HTTP, da cui dipende anche il nome delle opzioni di resilienza.</summary>
    public const string HttpClientName = "crm-api";

    public const string BaseAddressConfigurationKey = "CrmApi:BaseAddress";

    /// <summary>Risorsa <c>crm-mcp</c> dell'AppHost, risolta dalla service discovery.</summary>
    public const string DefaultBaseAddress = "https+http://crm-mcp";

    public async Task<IReadOnlyList<CompanySummaryView>> ListCompaniesAsync(CancellationToken cancellationToken) =>
        await http.GetFromJsonAsync<List<CompanySummaryView>>("api/views/companies", cancellationToken) ?? [];

    public Task<CompanyDetailView?> FindCompanyAsync(string companyId, CancellationToken cancellationToken) =>
        FindAsync<CompanyDetailView>($"api/views/companies/{Uri.EscapeDataString(companyId)}", cancellationToken);

    public async Task<IReadOnlyList<DealSummaryView>> ListDealsAsync(
        DealStage? stage, DealStatus? o2cStatus, string? companyId, CancellationToken cancellationToken)
    {
        string uri = QueryHelpers.AddQueryString("api/views/deals", new Dictionary<string, string?>
        {
            ["stage"] = stage?.ToString(),
            ["o2cStatus"] = o2cStatus?.ToString(),
            ["companyId"] = string.IsNullOrWhiteSpace(companyId) ? null : companyId
        }.Where(p => p.Value is not null));

        return await http.GetFromJsonAsync<List<DealSummaryView>>(uri, cancellationToken) ?? [];
    }

    public Task<DealDetailView?> FindDealAsync(string dealId, CancellationToken cancellationToken) =>
        FindAsync<DealDetailView>($"api/views/deals/{Uri.EscapeDataString(dealId)}", cancellationToken);

    public async Task<CloseDealResult> CloseDealAsync(string dealId, DealCloseOutcome outcome, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(
            $"api/deals/{Uri.EscapeDataString(dealId)}/close", new CloseDealRequest(outcome), cancellationToken);

        CloseDealStatus status = response.StatusCode switch
        {
            HttpStatusCode.OK => CloseDealStatus.Closed,
            HttpStatusCode.NotFound => CloseDealStatus.NotFound,
            HttpStatusCode.Conflict => CloseDealStatus.AlreadyClosed,
            HttpStatusCode.ServiceUnavailable => CloseDealStatus.EventNotPublished,
            _ => throw new HttpRequestException($"Chiusura del deal {dealId} non riuscita (HTTP {(int)response.StatusCode}).", null, response.StatusCode)
        };

        string message = status == CloseDealStatus.Closed
            ? outcome == DealCloseOutcome.Won
                ? $"Deal {dealId} chiuso come vinto: il flusso Order-to-Cash è partito."
                : $"Deal {dealId} chiuso come perso: nessun ordine verrà generato."
            : await ReadProblemDetailAsync(response, cancellationToken) ?? $"Chiusura del deal {dealId} non riuscita.";

        return new CloseDealResult(status, message);
    }

    private async Task<T?> FindAsync<T>(string uri, CancellationToken cancellationToken) where T : class
    {
        using HttpResponseMessage response = await http.GetAsync(uri, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private static async Task<string?> ReadProblemDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            ProblemBody? problem = await response.Content.ReadFromJsonAsync<ProblemBody>(cancellationToken);

            return string.IsNullOrWhiteSpace(problem?.Detail) ? null : problem.Detail;
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed record ProblemBody(string? Detail);
}
