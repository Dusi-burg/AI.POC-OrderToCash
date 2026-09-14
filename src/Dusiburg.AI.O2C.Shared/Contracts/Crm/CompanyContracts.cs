namespace Dusiburg.AI.O2C.Shared.Contracts.Crm;

/// <summary>Input di <c>get_company</c>.</summary>
public sealed record GetCompanyRequest(string CompanyId);

/// <summary>Output di <c>get_company</c>.</summary>
public sealed record CompanyDto(string CompanyId, string Name, string VatNumber, string Email, string Address);
