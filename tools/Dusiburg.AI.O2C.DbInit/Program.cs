using Dusiburg.AI.O2C.DbInit;
using Microsoft.Data.SqlClient;

// Uso: dotnet run --project tools/Dusiburg.AI.O2C.DbInit [-- "<connection string>"] [--allow-non-local]
// Senza argomenti usa ConnectionStrings__sql dall'ambiente o, in mancanza, (localdb)\localdev database O2C.
const string AllowNonLocal = "--allow-non-local";

var connectionString = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__sql")
    ?? O2CDatabaseInitializer.LocalConnectionString;

var target = new SqlConnectionStringBuilder(connectionString);

// Il tool cancella il database: fuori da LocalDB serve una conferma esplicita.
if (!target.DataSource.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase) && !args.Contains(AllowNonLocal))
{
    Console.Error.WriteLine($"Il server '{target.DataSource}' non è LocalDB: il database verrebbe cancellato. Aggiungi {AllowNonLocal} per procedere.");
    return 1;
}

Console.WriteLine($"Ricreo il database '{target.InitialCatalog}' su '{target.DataSource}'...");

await O2CDatabaseInitializer.RecreateAsync(connectionString);

Console.WriteLine("Fatto: schemi erp e crm creati, tabelle di lookup popolate dagli enum.");

return 0;
