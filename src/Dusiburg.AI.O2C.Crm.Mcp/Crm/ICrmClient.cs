using Dusiburg.AI.O2C.Shared.Contracts.Crm;

namespace Dusiburg.AI.O2C.Crm.Mcp.Crm;

/// <summary>
/// Accesso al CRM usato dai tool di <c>crm-mcp</c> (§6.2): in locale <see cref="MockCrmClient"/>, in futuro un adapter HubSpot (M6).
/// </summary>
public interface ICrmClient
{
    /// <summary>Deal con righe e revisione; <c>null</c> se non esiste.</summary>
    Task<DealDto?> GetDealAsync(string dealId, CancellationToken cancellationToken = default);

    /// <summary>Azienda; <c>null</c> se non esiste.</summary>
    Task<CompanyDto?> GetCompanyAsync(string companyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scrive lo stato O2C sul deal senza cambiarne la revisione (G1.3) e lo registra nello storico delle note;
    /// <c>null</c> se il deal non esiste. Input non valido: <see cref="ArgumentException"/>.
    /// </summary>
    Task<UpdateDealResponse?> UpdateDealAsync(UpdateDealRequest request, CancellationToken cancellationToken = default);
}
