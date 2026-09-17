using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Erp;
using Microsoft.AspNetCore.WebUtilities;

namespace Dusiburg.AI.O2C.Erp.Web.Api;

/// <summary>
/// Client tipizzato, in sola lettura, verso le viste di <c>Erp.Api</c> (6.5). Un 404 è un <c>null</c>; gli altri errori
/// sono eccezioni, mostrate dalla pagina di errore.
/// </summary>
public sealed class ErpApiClient(HttpClient http)
{
    public const string BaseAddressConfigurationKey = "ErpApi:BaseAddress";

    /// <summary>Risorsa <c>erp-api</c> dell'AppHost, risolta dalla service discovery.</summary>
    public const string DefaultBaseAddress = "https+http://erp-api";

    public async Task<IReadOnlyList<CustomerSummaryView>> ListCustomersAsync(CancellationToken cancellationToken) =>
        await http.GetFromJsonAsync<List<CustomerSummaryView>>("api/views/customers", cancellationToken) ?? [];

    public Task<CustomerDetailView?> FindCustomerAsync(int customerId, CancellationToken cancellationToken) =>
        FindAsync<CustomerDetailView>(string.Create(CultureInfo.InvariantCulture, $"api/views/customers/{customerId}"), cancellationToken);

    public async Task<IReadOnlyList<StockItemView>> ListStockAsync(bool shortOnly, CancellationToken cancellationToken) =>
        await http.GetFromJsonAsync<List<StockItemView>>(shortOnly ? "api/views/stock?shortOnly=true" : "api/views/stock", cancellationToken) ?? [];

    public async Task<IReadOnlyList<OrderSummaryView>> ListOrdersAsync(OrderStatus? status, int? customerId, CancellationToken cancellationToken)
    {
        string uri = QueryHelpers.AddQueryString("api/views/orders", new Dictionary<string, string?>
        {
            ["status"] = status?.ToString(),
            ["customerId"] = customerId?.ToString(CultureInfo.InvariantCulture)
        }.Where(p => p.Value is not null));

        return await http.GetFromJsonAsync<List<OrderSummaryView>>(uri, cancellationToken) ?? [];
    }

    public Task<OrderDetailView?> FindOrderAsync(string orderNumber, CancellationToken cancellationToken) =>
        FindAsync<OrderDetailView>($"api/views/orders/{Uri.EscapeDataString(orderNumber)}", cancellationToken);

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
}
