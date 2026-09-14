using System.Text.Json;
using Dusiburg.AI.O2C.DbInit;
using Dusiburg.AI.O2C.Erp.Data;
using Dusiburg.AI.O2C.Erp.Data.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dusiburg.AI.O2C.Erp.Api.Tests;

/// <summary>
/// Un database dedicato per classe di test su <c>(localdb)\localdev</c> (<c>O2C_Test_&lt;guid&gt;</c>), creato dal modello con i dati demo
/// e cancellato a fine classe; prima di ogni test l'ERP torna allo stato del seed.
/// </summary>
public abstract class ErpApiTestBase
{
    private string _connectionString = null!;

    protected WebApplicationFactory<Program> Factory { get; private set; } = null!;

    protected static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [OneTimeSetUp]
    public async Task CreateDatabaseAndFactory()
    {
        _connectionString = O2CDatabaseInitializer.LocalConnectionStringFor($"O2C_Test_{Guid.NewGuid():N}");

        await O2CDatabaseInitializer.RecreateAsync(_connectionString, seed: true, CancellationToken);

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:sql", _connectionString));
    }

    [SetUp]
    public async Task ResetDemoData()
    {
        await WithDbAsync(db => ErpSeeder.ResetAsync(db, CancellationToken));
    }

    [OneTimeTearDown]
    public async Task DropDatabase()
    {
        await Factory.DisposeAsync();
        await O2CDatabaseInitializer.DropAsync(_connectionString);
    }

    protected async Task WithDbAsync(Func<ErpDbContext, Task> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();

        await action(scope.ServiceProvider.GetRequiredService<ErpDbContext>());
    }

    protected async Task<T> WithDbAsync<T>(Func<ErpDbContext, Task<T>> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();

        return await action(scope.ServiceProvider.GetRequiredService<ErpDbContext>());
    }

    /// <summary>Estensione <c>code</c> di un ProblemDetails.</summary>
    protected static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
