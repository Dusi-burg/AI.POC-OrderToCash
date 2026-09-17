using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dusiburg.AI.O2C.Approvals.Web.Approvals;
using Dusiburg.AI.O2C.DbInit;
using Dusiburg.AI.O2C.Orchestration.Data;
using Dusiburg.AI.O2C.Orchestration.Data.Entities;
using Dusiburg.AI.O2C.Shared.Contracts.Approvals;
using Dusiburg.AI.O2C.Shared.Contracts.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dusiburg.AI.O2C.Orchestrator.Tests.Approvals;

/// <summary>Publisher in memoria: i test non hanno bisogno di un broker, e la decisione resta comunque scritta (G5.3).</summary>
internal sealed class RecordingApprovalDecisionPublisher : IApprovalDecisionPublisher
{
    public List<ApprovalDecided> Published { get; } = [];

    public Task PublishAsync(ApprovalDecided message, CancellationToken cancellationToken)
    {
        Published.Add(message);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Coda, dettaglio e callback di <c>Approvals.Web</c> su un database dedicato di <c>(localdb)\localdev</c> (5.9):
/// la decisione si registra una volta sola e la seconda riceve 409.
/// </summary>
public class ApprovalsWebTests
{
    private const string Approver = "approver@test.local";
    private const string CrmWeb = "http://crm.test";

    private string _connectionString = null!;
    private RecordingApprovalDecisionPublisher _publisher = null!;
    private WebApplicationFactory<ApprovalsWebEntryPoint> _factory = null!;

    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [OneTimeSetUp]
    public async Task CreateDatabaseAndFactory()
    {
        _connectionString = O2CDatabaseInitializer.LocalConnectionStringFor($"O2C_Test_{Guid.NewGuid():N}");

        await O2CDatabaseInitializer.RecreateAsync(_connectionString, seed: false, CancellationToken);

        _publisher = new RecordingApprovalDecisionPublisher();

        _factory = new WebApplicationFactory<ApprovalsWebEntryPoint>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:sql", _connectionString);

            // Il client RabbitMQ dell'integrazione Aspire pretende una connection string anche se il publisher è sostituito.
            builder.UseSetting("ConnectionStrings:rabbitmq", "amqp://guest:guest@localhost:5672/o2c");
            builder.UseSetting(ApprovalDecisionService.ApproverSetting, Approver);
            builder.UseSetting("Links:CrmWeb", CrmWeb);

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IApprovalDecisionPublisher>();
                services.AddSingleton<IApprovalDecisionPublisher>(_publisher);
            });
        });
    }

    [OneTimeTearDown]
    public async Task DropDatabase()
    {
        await _factory.DisposeAsync();
        await O2CDatabaseInitializer.DropAsync(_connectionString);
    }

    [SetUp]
    public async Task ClearRequests()
    {
        _publisher.Published.Clear();

        await WithDbAsync(async db =>
        {
            await db.ApprovalRequests.ExecuteDeleteAsync(CancellationToken);
            await db.WorkflowStates.ExecuteDeleteAsync(CancellationToken);
        });
    }

    [Test]
    public async Task Decision_OnPendingRequest_IsRecordedAndPublished()
    {
        //SETUP
        var approvalId = await GivenPendingRequestAsync();
        using var client = _factory.CreateClient();

        //SUT
        var response = await client.PostAsJsonAsync(
            $"/api/approvals/{approvalId}/decision", new ApprovalDecisionRequest(true, "Va bene."), CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var decision = await response.Content.ReadFromJsonAsync<ApprovalDecisionResponse>(CancellationToken);

        Assert.That(decision!.Status, Is.EqualTo(ApprovalStatus.Approved));
        Assert.That(decision.DecidedBy, Is.EqualTo(Approver));

        var stored = await WithDbAsync(db => db.ApprovalRequests.SingleAsync(r => r.PublicId == approvalId, CancellationToken));

        Assert.That(stored.Status, Is.EqualTo(ApprovalStatus.Approved));
        Assert.That(stored.DecidedBy, Is.EqualTo(Approver));
        Assert.That(stored.DecisionNote, Is.EqualTo("Va bene."));
        Assert.That(stored.DecidedAt, Is.Not.Null);

        var published = _publisher.Published.Single();

        Assert.That(published.ApprovalId, Is.EqualTo(approvalId));
        Assert.That(published.Decision, Is.EqualTo(ApprovalStatus.Approved));
        Assert.That(published.CorrelationId, Is.EqualTo(stored.CorrelationId));
    }

    [Test]
    public async Task Decision_Twice_IsRejectedWithConflict()
    {
        //SETUP
        var approvalId = await GivenPendingRequestAsync();
        using var client = _factory.CreateClient();

        await client.PostAsJsonAsync($"/api/approvals/{approvalId}/decision", new ApprovalDecisionRequest(true, null), CancellationToken);

        //SUT
        var second = await client.PostAsJsonAsync(
            $"/api/approvals/{approvalId}/decision", new ApprovalDecisionRequest(false, "Ci ho ripensato."), CancellationToken);

        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        var stored = await WithDbAsync(db => db.ApprovalRequests.SingleAsync(r => r.PublicId == approvalId, CancellationToken));

        Assert.That(stored.Status, Is.EqualTo(ApprovalStatus.Approved), "la prima decisione resta valida");
        Assert.That(_publisher.Published, Has.Count.EqualTo(1), "nessun secondo messaggio per una decisione non applicata");
    }

    [Test]
    public async Task Decision_OnUnknownRequest_IsNotFound()
    {
        //SETUP
        using var client = _factory.CreateClient();

        //SUT
        var response = await client.PostAsJsonAsync(
            $"/api/approvals/{Guid.CreateVersion7()}/decision", new ApprovalDecisionRequest(true, null), CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Queue_ShowsPendingRequestsWithReasonsAndTotal()
    {
        //SETUP
        await GivenPendingRequestAsync();
        using var client = _factory.CreateClient();

        //SUT
        var page = await client.GetStringAsync("/approvals", CancellationToken);

        Assert.That(page, Does.Contain("D-1002").And.Contain("OverThreshold").And.Contain(Approver));
    }

    [Test]
    public async Task Details_ShowsTheProposalAndTheDecisionActions()
    {
        //SETUP
        var approvalId = await GivenPendingRequestAsync();
        using var client = _factory.CreateClient();

        //SUT
        var page = await client.GetStringAsync($"/approvals/{approvalId}", CancellationToken);

        Assert.That(page, Does
            .Contain("IND-MOT-002").And
            .Contain("Approva").And
            .Contain("Rifiuta").And
            .Contain("o2c-D-1002-r1"));
    }

    [Test]
    public async Task Details_OfUnknownRequest_IsNotFound()
    {
        //SETUP
        using var client = _factory.CreateClient();

        //SUT
        var response = await client.GetAsync($"/approvals/{Guid.CreateVersion7()}", CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Queue_FilteredByDeal_ShowsOnlyThatDeal()
    {
        //SETUP
        await GivenPendingRequestAsync("D-1002");
        await GivenPendingRequestAsync("D-1004");
        using var client = _factory.CreateClient();

        //SUT
        string filtered = await client.GetStringAsync("/approvals?all=true&dealId=D-1004", CancellationToken);
        string unfiltered = await client.GetStringAsync("/approvals", CancellationToken);

        Assert.That(filtered, Does.Contain("<code>D-1004</code>").And.Not.Contain("<code>D-1002</code>"));
        Assert.That(filtered, Does.Contain("deal D-1004"));
        Assert.That(unfiltered, Does.Contain("<code>D-1004</code>").And.Contain("<code>D-1002</code>"));
    }

    [Test]
    public async Task Details_LinksTheDealPageInCrmWeb()
    {
        //SETUP
        var approvalId = await GivenPendingRequestAsync();
        using var client = _factory.CreateClient();

        //SUT
        string page = await client.GetStringAsync($"/approvals/{approvalId}", CancellationToken);

        Assert.That(page, Does.Contain($"href=\"{CrmWeb}/deals/D-1002\"").And.Not.Contain("/dev/deals/"));
    }

    /// <summary>Una richiesta pendente come quella che l'orchestratore scrive alla sospensione di D-1002.</summary>
    private async Task<Guid> GivenPendingRequestAsync(string dealId = "D-1002")
    {
        var approvalId = Guid.CreateVersion7();
        var correlationId = $"corr-{approvalId:N}";

        var payload = new ApprovalPayload(
            dealId, 1, "Automazione impianto confezionamento", "C-02", "Cartiera del Brenta S.p.A.",
            4, "Cartiera del Brenta S.p.A.", false, false,
            [new ApprovalLine("IND-MOT-002", 8, 575m, true, 12, 14)],
            4_600m,
            $"o2c-{dealId}-r1");

        await WithDbAsync(async db =>
        {
            db.WorkflowStates.Add(new WorkflowState
            {
                CorrelationId = correlationId,
                DealId = dealId,
                DealRevision = 1,
                Phase = WorkflowPhase.AwaitingApproval,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            db.ApprovalRequests.Add(new ApprovalRequest
            {
                PublicId = approvalId,
                CorrelationId = correlationId,
                DealId = dealId,
                DealRevision = 1,
                PayloadJson = JsonSerializer.Serialize(payload, JsonSerializerOptions.Web),
                ReasonsJson = JsonSerializer.Serialize(new[] { ApprovalReason.OverThreshold }, JsonSerializerOptions.Web),
                Total = payload.Total,
                Status = ApprovalStatus.Pending,
                RequestedAt = DateTimeOffset.UtcNow,
                CheckpointId = "checkpoint-di-prova"
            });

            await db.SaveChangesAsync(CancellationToken);
        });

        return approvalId;
    }

    private async Task WithDbAsync(Func<OrchestrationDbContext, Task> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        await action(scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>());
    }

    private async Task<T> WithDbAsync<T>(Func<OrchestrationDbContext, Task<T>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        return await action(scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>());
    }
}
