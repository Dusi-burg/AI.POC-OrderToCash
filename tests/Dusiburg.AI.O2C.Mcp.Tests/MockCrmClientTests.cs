using Dusiburg.AI.O2C.Crm.Data;
using Dusiburg.AI.O2C.Crm.Data.Seed;
using Dusiburg.AI.O2C.Crm.Mcp.Crm;
using Dusiburg.AI.O2C.DbInit;
using Dusiburg.AI.O2C.Shared.Contracts.Crm;
using Dusiburg.AI.O2C.Shared.Demo;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.O2C.Mcp.Tests;

/// <summary>CRM mock (1.11) su un database dedicato alla classe, riportato ai dati demo prima di ogni test.</summary>
public class MockCrmClientTests
{
    private string _connectionString = null!;
    private CrmDbContext _db = null!;
    private MockCrmClient _client = null!;

    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        _connectionString = O2CDatabaseInitializer.LocalConnectionStringFor($"O2C_Test_{Guid.NewGuid():N}");

        await O2CDatabaseInitializer.RecreateAsync(_connectionString, seed: true, CancellationToken);
    }

    [OneTimeTearDown]
    public async Task DropDatabase()
    {
        await O2CDatabaseInitializer.DropAsync(_connectionString);
    }

    [SetUp]
    public async Task ResetCrm()
    {
        _db = CreateContext();

        await CrmSeeder.ResetAsync(_db, DateTimeOffset.UtcNow, CancellationToken);

        _client = new MockCrmClient(_db, TimeProvider.System);
    }

    [TearDown]
    public async Task DisposeContext()
    {
        await _db.DisposeAsync();
    }

    [Test]
    public async Task GetDeal_Existing_ReturnsLinesRevisionAndStage()
    {
        var expected = DemoCatalog.Deals.Single(d => d.DealId == "D-1001");

        var deal = await _client.GetDealAsync(expected.DealId, CancellationToken);

        Assert.That(deal, Is.Not.Null);
        Assert.That(deal!.Revision, Is.EqualTo(1));
        Assert.That(deal.Stage, Is.EqualTo("ContractSent"));
        Assert.That(deal.CompanyId, Is.EqualTo(expected.CompanyId));
        Assert.That(deal.Amount, Is.EqualTo(expected.Amount));
        Assert.That(deal.LineItems, Is.EqualTo(expected.Lines.Select(l => new DealLineItemDto(l.Sku, l.Quantity, l.UnitPrice))));
    }

    [Test]
    public async Task GetDeal_Unknown_ReturnsNull()
    {
        Assert.That(await _client.GetDealAsync("D-9999", CancellationToken), Is.Null);
    }

    [Test]
    public async Task GetCompany_Existing_ReturnsCompany()
    {
        var expected = DemoCatalog.Companies.Single(c => c.CompanyId == "C-04");

        var company = await _client.GetCompanyAsync(expected.CompanyId, CancellationToken);

        Assert.That(company, Is.EqualTo(new CompanyDto(expected.CompanyId, expected.Name, expected.VatNumber, expected.Email, expected.Address)));
    }

    [Test]
    public async Task UpdateDeal_KeepsRevisionAndWritesNote()
    {
        var response = await _client.UpdateDealAsync(
            new UpdateDealRequest("D-1002", null, DealStatus.ApprovalPending, "In attesa di approvazione: sopra soglia"),
            CancellationToken);

        await using var check = CreateContext();
        var deal = await check.Deals.Include(d => d.Notes).SingleAsync(d => d.Code == "D-1002", CancellationToken);

        Assert.That(response, Is.EqualTo(new UpdateDealResponse(true)));
        Assert.That(deal.Revision, Is.EqualTo(1));
        Assert.That(deal.O2CStatus, Is.EqualTo(DealStatus.ApprovalPending));
        Assert.That(deal.LastNote, Is.EqualTo("In attesa di approvazione: sopra soglia"));
        Assert.That(deal.Notes.Select(n => n.Status), Is.EqualTo(new[] { DealStatus.ApprovalPending }));
    }

    [Test]
    public async Task UpdateDeal_TwiceWithOrderNumber_AppendsNotesAndKeepsOrderNumber()
    {
        await _client.UpdateDealAsync(new UpdateDealRequest("D-1002", null, DealStatus.ApprovalPending, "Sopra soglia"), CancellationToken);
        await _client.UpdateDealAsync(new UpdateDealRequest("D-1002", "SO-2026-000001", DealStatus.OrderCreated, "Ordine creato"), CancellationToken);

        await using var check = CreateContext();
        var deal = await check.Deals.Include(d => d.Notes).SingleAsync(d => d.Code == "D-1002", CancellationToken);

        Assert.That(deal.Revision, Is.EqualTo(1));
        Assert.That(deal.O2CStatus, Is.EqualTo(DealStatus.OrderCreated));
        Assert.That(deal.ErpOrderNumber, Is.EqualTo("SO-2026-000001"));
        Assert.That(deal.Notes.OrderBy(n => n.Id).Select(n => n.Status), Is.EqualTo(new[] { DealStatus.ApprovalPending, DealStatus.OrderCreated }));
    }

    [Test]
    public async Task UpdateDeal_Unknown_ReturnsNull()
    {
        var response = await _client.UpdateDealAsync(new UpdateDealRequest("D-9999", null, DealStatus.Failed, null), CancellationToken);

        Assert.That(response, Is.Null);
    }

    [Test]
    public void UpdateDeal_NoteTooLong_Throws()
    {
        var request = new UpdateDealRequest("D-1001", null, DealStatus.Failed, new string('x', MockCrmClient.NoteMaxLength + 1));

        Assert.That(() => _client.UpdateDealAsync(request, CancellationToken), Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public async Task Reset_AfterUpdate_RestoresDemoData()
    {
        await _client.UpdateDealAsync(new UpdateDealRequest("D-1001", "SO-2026-000001", DealStatus.OrderCreated, "Ordine creato"), CancellationToken);

        await CrmSeeder.ResetAsync(_db, DateTimeOffset.UtcNow, CancellationToken);

        await using var check = CreateContext();
        var deal = await check.Deals.SingleAsync(d => d.Code == "D-1001", CancellationToken);

        Assert.That(deal.O2CStatus, Is.Null);
        Assert.That(deal.ErpOrderNumber, Is.Null);
        Assert.That(await check.DealNotes.CountAsync(CancellationToken), Is.Zero);
        Assert.That(await check.Deals.CountAsync(CancellationToken), Is.EqualTo(DemoCatalog.Deals.Count));
    }

    private CrmDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<CrmDbContext>().UseSqlServer(_connectionString).Options);
}
