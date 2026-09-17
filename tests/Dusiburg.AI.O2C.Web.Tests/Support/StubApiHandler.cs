using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Dusiburg.AI.O2C.Shared.Correlation;

namespace Dusiburg.AI.O2C.Web.Tests.Support;

/// <summary>Richiesta ricevuta dall'API finta, con il corpo già letto.</summary>
internal sealed record StubRequest(HttpMethod Method, string PathAndQuery, string? Body, string? CorrelationId);

/// <summary>
/// API a valle finta per le UI (6.11): risponde per metodo e percorso con JSON o ProblemDetails e registra le richieste.
/// Una rotta non configurata risponde 404, come un deal o un ordine che non esiste.
/// </summary>
internal sealed class StubApiHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, (HttpStatusCode Status, string Json)> _routes = new();

    public ConcurrentQueue<StubRequest> Requests { get; } = new();

    public void Clear()
    {
        _routes.Clear();
        Requests.Clear();
    }

    public void Json(HttpMethod method, string pathAndQuery, object body, HttpStatusCode status = HttpStatusCode.OK) =>
        _routes[Key(method, pathAndQuery)] = (status, JsonSerializer.Serialize(body, JsonSerializerOptions.Web));

    public void Problem(HttpMethod method, string pathAndQuery, HttpStatusCode status, string code, string detail) =>
        _routes[Key(method, pathAndQuery)] = (status, JsonSerializer.Serialize(new { status = (int)status, detail, code }, JsonSerializerOptions.Web));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string pathAndQuery = request.RequestUri!.PathAndQuery;
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        string? correlationId = request.Headers.TryGetValues(CorrelationId.HeaderName, out IEnumerable<string>? values) ? values.Single() : null;

        Requests.Enqueue(new StubRequest(request.Method, pathAndQuery, body, correlationId));

        (HttpStatusCode Status, string Json) route = _routes.TryGetValue(Key(request.Method, pathAndQuery), out var configured)
            ? configured
            : (HttpStatusCode.NotFound, """{ "status": 404, "detail": "Non trovato.", "code": "NOT_FOUND" }""");

        return new HttpResponseMessage(route.Status)
        {
            RequestMessage = request,
            Content = new StringContent(route.Json, System.Text.Encoding.UTF8, route.Status == HttpStatusCode.OK ? "application/json" : "application/problem+json")
        };
    }

    private static string Key(HttpMethod method, string pathAndQuery) => $"{method.Method} {pathAndQuery}";
}

internal static class HtmlForms
{
    /// <summary>Token antiforgery del primo form della pagina: serve alle POST verso le Razor Pages.</summary>
    public static async Task<string> ReadAntiforgeryTokenAsync(HttpClient client, string pageUrl, CancellationToken cancellationToken)
    {
        string page = await client.GetStringAsync(pageUrl, cancellationToken);
        System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(
            page, """name="__RequestVerificationToken" type="hidden" value="([^"]+)" """.TrimEnd());

        Assert.That(match.Success, Is.True, "la pagina deve contenere un form con il token antiforgery");

        return match.Groups[1].Value;
    }

    public static FormUrlEncodedContent Form(string token) =>
        new([new KeyValuePair<string, string>("__RequestVerificationToken", token)]);
}
