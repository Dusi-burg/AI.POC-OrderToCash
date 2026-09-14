using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.O2C.Erp.Api.Data;

internal static class SqlErrors
{
    /// <summary>Violazione di un indice univoco (2601) o di un vincolo unique/PK (2627).</summary>
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
