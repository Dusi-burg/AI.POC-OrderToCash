using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dusiburg.AI.O2C.Mcp.Hosting;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Dusiburg.AI.O2C.Shared.Errors;
using Microsoft.AspNetCore.WebUtilities;
using Polly;

namespace Dusiburg.AI.O2C.Erp.Mcp.Erp;

/// <summary>
/// Client tipizzato verso <c>Erp.Api</c> (2.2). Il correlation id viaggia con il <c>CorrelationIdDelegatingHandler</c> di ServiceDefaults;
/// ProblemDetails, codici HTTP e mancate risposte diventano <see cref="ToolException"/> con i codici di §6.
/// </summary>
public sealed class ErpApiClient(HttpClient http)
{
    public const string BaseAddressConfigurationKey = "ErpApi:BaseAddress";

    /// <summary>Risorsa <c>erp-api</c> dell'AppHost, risolta dalla service discovery.</summary>
    public const string DefaultBaseAddress = "https+http://erp-api";

    /// <summary>Cliente per partita IVA oppure email; <c>null</c> se non esiste.</summary>
    public async Task<CustomerDto?> GetCustomerAsync(string? vatNumber, string? email, CancellationToken cancellationToken)
    {
        var uri = QueryHelpers.AddQueryString("api/customers", new Dictionary<string, string?>
        {
            ["vatNumber"] = vatNumber,
            ["email"] = email
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await SendAsync(request, cancellationToken);

        return response.StatusCode == HttpStatusCode.NotFound
            ? null
            : await ReadAsync<CustomerDto>(response, cancellationToken);
    }

    public async Task<CreateCustomerResponse> CreateCustomerAsync(CreateCustomerRequest customer, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/customers") { Content = JsonContent.Create(customer) };
        using var response = await SendAsync(request, cancellationToken);

        return await ReadAsync<CreateCustomerResponse>(response, cancellationToken);
    }

    public async Task<StockCheckDto> CheckStockAsync(string sku, int quantity, CancellationToken cancellationToken)
    {
        var uri = string.Create(CultureInfo.InvariantCulture, $"api/stock/{Uri.EscapeDataString(sku)}?quantity={quantity}");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await SendAsync(request, cancellationToken);

        return await ReadAsync<StockCheckDto>(response, cancellationToken);
    }

    public async Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest order, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/orders") { Content = JsonContent.Create(order) };
        using var response = await SendAsync(request, cancellationToken);

        return await ReadAsync<CreateOrderResponse>(response, cancellationToken);
    }

    public async Task<OrderDto> GetOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/orders/{orderId:D}");
        using var response = await SendAsync(request, cancellationToken);

        return await ReadAsync<OrderDto>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (IsUnavailable(exception, cancellationToken))
        {
            throw new ToolException(ToolErrorCodes.UpstreamUnavailable, "Erp.Api non raggiungibile o non ha risposto in tempo.", exception);
        }
    }

    /// <summary>Connessione fallita, timeout o circuito aperto dopo i tentativi della resilienza di ServiceDefaults.</summary>
    private static bool IsUnavailable(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or ExecutionRejectedException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await ToToolExceptionAsync(response, cancellationToken);
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                ?? throw new JsonException("Risposta vuota.");
        }
        catch (JsonException exception)
        {
            throw new ToolException(ToolErrorCodes.Internal, "Risposta non valida da Erp.Api.", exception);
        }
    }

    private static async Task<ToolException> ToToolExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;

        // Solo gli errori di dominio portano il dettaglio dell'ERP al modello; quelli di infrastruttura restano generici.
        return response.StatusCode switch
        {
            HttpStatusCode.BadRequest => new ToolException(
                ToolErrorCodes.ValidationError, await ReadDetailAsync(response, cancellationToken) ?? "Richiesta non valida."),
            HttpStatusCode.NotFound => new ToolException(
                ToolErrorCodes.NotFound, await ReadDetailAsync(response, cancellationToken) ?? "Risorsa non trovata."),
            HttpStatusCode.Conflict => new ToolException(
                ToolErrorCodes.Conflict, await ReadDetailAsync(response, cancellationToken) ?? "Conflitto con i dati esistenti."),
            >= HttpStatusCode.InternalServerError => new ToolException(
                ToolErrorCodes.UpstreamUnavailable, string.Create(CultureInfo.InvariantCulture, $"Erp.Api non disponibile (HTTP {status}).")),
            _ => new ToolException(
                ToolErrorCodes.Internal, string.Create(CultureInfo.InvariantCulture, $"Risposta inattesa da Erp.Api (HTTP {status})."))
        };
    }

    private static async Task<string?> ReadDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(cancellationToken);

            return string.IsNullOrWhiteSpace(problem?.Detail) ? null : problem.Detail;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed record ProblemBody(string? Detail);
}
