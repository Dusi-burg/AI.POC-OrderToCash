using Dusiburg.AI.O2C.Crm.Mcp.Deals;
using Dusiburg.AI.O2C.ServiceDefaults.Problems;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Contracts.Views.Crm;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Dusiburg.AI.O2C.Crm.Mcp.Views;

/// <summary>
/// API degli utenti del CRM usate da <c>Crm.Web</c> (6.2): letture su <c>/api/views</c> e comando di chiusura. Mappate in
/// tutti gli ambienti e senza API key (G6.1, G6.5): l'API key protegge solo <c>/mcp</c>.
/// </summary>
internal static class CrmApiEndpoints
{
    public static IEndpointRouteBuilder MapCrmApiEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/views/companies", ListCompaniesAsync);
        api.MapGet("/views/companies/{companyId}", GetCompanyAsync);
        api.MapGet("/views/deals", ListDealsAsync);
        api.MapGet("/views/deals/{dealId}", GetDealAsync);
        api.MapPost("/deals/{dealId}/close", CloseDealAsync);

        return app;
    }

    private static async Task<Ok<IReadOnlyList<CompanySummaryView>>> ListCompaniesAsync(
        CrmViewQueries queries, CancellationToken cancellationToken) =>
        TypedResults.Ok(await queries.ListCompaniesAsync(cancellationToken));

    private static async Task<Results<Ok<CompanyDetailView>, ProblemHttpResult>> GetCompanyAsync(
        string companyId, CrmViewQueries queries, CancellationToken cancellationToken)
    {
        CompanyDetailView? company = await queries.FindCompanyAsync(companyId, cancellationToken);

        return company is null ? ToolProblems.NotFound($"Azienda {companyId} non trovata.") : TypedResults.Ok(company);
    }

    private static async Task<Results<Ok<IReadOnlyList<DealSummaryView>>, ProblemHttpResult>> ListDealsAsync(
        string? stage, string? o2cStatus, string? companyId, CrmViewQueries queries, CancellationToken cancellationToken)
    {
        if (!TryParseFilter(stage, out DealStage? stageFilter) || !TryParseFilter(o2cStatus, out DealStatus? statusFilter))
        {
            return ToolProblems.Validation(
                $"Filtro non valido: stage ammessi {string.Join(", ", Enum.GetNames<DealStage>())}, stati O2C {string.Join(", ", Enum.GetNames<DealStatus>())}.");
        }

        return TypedResults.Ok(await queries.ListDealsAsync(new DealListFilter(stageFilter, statusFilter, companyId), cancellationToken));
    }

    private static async Task<Results<Ok<DealDetailView>, ProblemHttpResult>> GetDealAsync(
        string dealId, CrmViewQueries queries, CancellationToken cancellationToken)
    {
        DealDetailView? deal = await queries.FindDealAsync(dealId, cancellationToken);

        return deal is null ? ToolProblems.NotFound($"Deal {dealId} non trovato.") : TypedResults.Ok(deal);
    }

    /// <summary>200 con il deal aggiornato · 400 esito non valido · 404 · 409 deal già chiuso · 503 evento non pubblicato (G6.3).</summary>
    private static async Task<Results<Ok<DealDetailView>, ProblemHttpResult>> CloseDealAsync(
        string dealId, CloseDealRequest request, DealClosingService closing, CrmViewQueries queries, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Outcome))
        {
            return ToolProblems.Validation("outcome deve essere Won o Lost.");
        }

        DealCloseOutcomeResult result = await closing.CloseAsync(dealId, request.Outcome, cancellationToken);

        return await ToHttpResultAsync(result, dealId, queries, cancellationToken);
    }

    /// <summary>Traduzione comune degli esiti di chiusura, usata anche dall'endpoint dev <c>close-won</c>.</summary>
    public static async Task<Results<Ok<DealDetailView>, ProblemHttpResult>> ToHttpResultAsync(
        DealCloseOutcomeResult result, string dealId, CrmViewQueries queries, CancellationToken cancellationToken) =>
        result.Result switch
        {
            DealCloseResult.Closed or DealCloseResult.Republished => TypedResults.Ok((await queries.FindDealAsync(dealId, cancellationToken))!),
            DealCloseResult.NotFound => ToolProblems.NotFound(result.Message),
            DealCloseResult.AlreadyClosed => ToolProblems.Conflict(result.Message),
            _ => ToolProblems.UpstreamUnavailable(result.Message)
        };

    private static bool TryParseFilter<TEnum>(string? value, out TEnum? filter) where TEnum : struct, Enum
    {
        filter = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (Enum.TryParse(value, ignoreCase: true, out TEnum parsed) && Enum.IsDefined(parsed) && !int.TryParse(value, out _))
        {
            filter = parsed;

            return true;
        }

        return false;
    }
}
