namespace Dusiburg.AI.O2C.Shared.Contracts.Erp;

/// <summary>Input di <c>get_customer</c>: almeno uno fra partita IVA ed email.</summary>
public sealed record GetCustomerRequest(string? VatNumber, string? Email);

/// <summary>Output di <c>get_customer</c> (M20): <see cref="Customer"/> è <c>null</c> se il cliente non esiste, che non è un errore.</summary>
public sealed record GetCustomerResponse(CustomerDto? Customer);

/// <summary>Cliente ERP restituito da <c>get_customer</c>.</summary>
public sealed record CustomerDto(
    int CustomerId,
    string Name,
    string VatNumber,
    string Email,
    decimal CreditLimit,
    bool IsBlocked);

/// <summary>Input di <c>create_customer</c>.</summary>
public sealed record CreateCustomerRequest(string Name, string VatNumber, string Email, string Address);

/// <summary>Output di <c>create_customer</c>.</summary>
public sealed record CreateCustomerResponse(int CustomerId);
