using Dusiburg.AI.O2C.Orchestration.Data;
using Dusiburg.AI.O2C.ServiceDefaults.Problems;
using Dusiburg.AI.O2C.Shared.Contracts.Approvals;
using Microsoft.AspNetCore.Mvc;

namespace Dusiburg.AI.O2C.Approvals.Web.Approvals;

/// <summary>
/// Endpoint di callback unico della decisione (5.4): lo usa la UI di questo servizio e, in futuro, l'Adaptive Card di
/// Teams (§7). Le transizioni sono ammesse solo da <c>Pending</c>: una seconda decisione riceve 409.
/// </summary>
public static class ApprovalEndpoints
{
    public const string DecisionRoute = "/api/approvals/{id:guid}/decision";

    public static IEndpointRouteBuilder MapApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(DecisionRoute, DecideAsync)
            .WithName("ApprovalDecision")
            .Produces<ApprovalDecisionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> DecideAsync(
        Guid id,
        [FromBody] ApprovalDecisionRequest request,
        ApprovalDecisionService approvals,
        CancellationToken cancellationToken)
    {
        var (transition, response) = await approvals.DecideAsync(id, request.Approved, request.Note, cancellationToken);

        return transition switch
        {
            ApprovalTransition.Applied => Results.Ok(response),
            ApprovalTransition.NotFound => ToolProblems.NotFound($"Richiesta di approvazione '{id}' non trovata."),
            _ => ToolProblems.Conflict($"Richiesta di approvazione '{id}' già decisa.")
        };
    }
}
