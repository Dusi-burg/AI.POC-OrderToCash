using Dusiburg.AI.O2C.Erp.Api.Data;
using Dusiburg.AI.O2C.Erp.Data;
using Dusiburg.AI.O2C.Erp.Data.Entities;
using Dusiburg.AI.O2C.ServiceDefaults.Problems;
using Dusiburg.AI.O2C.Shared.Contracts.Erp;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.O2C.Erp.Api.Customers;

internal static class CustomerEndpoints
{
    public static RouteGroupBuilder MapCustomerEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/customers", GetCustomerAsync);
        api.MapPost("/customers", CreateCustomerAsync);

        return api;
    }

    /// <summary><c>get_customer</c>: per partita IVA oppure email (almeno uno); con entrambi vale prima la partita IVA.</summary>
    private static async Task<Results<Ok<CustomerDto>, ProblemHttpResult>> GetCustomerAsync(
        string? vatNumber, string? email, ErpDbContext db, CancellationToken cancellationToken)
    {
        var vat = vatNumber?.Trim();
        var mail = email?.Trim();

        if (string.IsNullOrEmpty(vat) && string.IsNullOrEmpty(mail))
        {
            return ToolProblems.Validation("Serve almeno uno fra vatNumber ed email.");
        }

        Customer? customer = null;

        if (!string.IsNullOrEmpty(vat))
        {
            customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.VatNumber == vat, cancellationToken);
        }

        if (customer is null && !string.IsNullOrEmpty(mail))
        {
            customer = await db.Customers.AsNoTracking().OrderBy(c => c.Id).FirstOrDefaultAsync(c => c.Email == mail, cancellationToken);
        }

        return customer is null
            ? ToolProblems.NotFound("Cliente non trovato.")
            : TypedResults.Ok(new CustomerDto(customer.Id, customer.Name, customer.VatNumber ?? string.Empty, customer.Email, customer.CreditLimit, customer.IsBlocked));
    }

    /// <summary><c>create_customer</c>: 409 se la partita IVA esiste già.</summary>
    private static async Task<Results<Created<CreateCustomerResponse>, ProblemHttpResult>> CreateCustomerAsync(
        CreateCustomerRequest request, ErpDbContext db, CancellationToken cancellationToken)
    {
        var error = Validate(request);

        if (error is not null)
        {
            return ToolProblems.Validation(error);
        }

        var vat = request.VatNumber.Trim();

        if (await db.Customers.AnyAsync(c => c.VatNumber == vat, cancellationToken))
        {
            return ToolProblems.Conflict($"Esiste già un cliente con partita IVA {vat}.");
        }

        var customer = new Customer
        {
            Name = request.Name.Trim(),
            VatNumber = vat,
            Email = request.Email.Trim(),
            Address = request.Address.Trim(),
            CreditLimit = 0m,
            IsBlocked = false
        };

        db.Customers.Add(customer);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (SqlErrors.IsUniqueViolation(exception))
        {
            return ToolProblems.Conflict($"Esiste già un cliente con partita IVA {vat}.");
        }

        return TypedResults.Created($"/api/customers?vatNumber={Uri.EscapeDataString(vat)}", new CreateCustomerResponse(customer.Id));
    }

    private static string? Validate(CreateCustomerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
        {
            return "name è obbligatorio (max 200 caratteri).";
        }

        if (string.IsNullOrWhiteSpace(request.VatNumber) || request.VatNumber.Trim().Length > 20)
        {
            return "vatNumber è obbligatorio (max 20 caratteri).";
        }

        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Trim().Length > 254 || !request.Email.Contains('@', StringComparison.Ordinal))
        {
            return "email è obbligatoria e deve essere un indirizzo valido (max 254 caratteri).";
        }

        if (string.IsNullOrWhiteSpace(request.Address) || request.Address.Trim().Length > 400)
        {
            return "address è obbligatorio (max 400 caratteri).";
        }

        return null;
    }
}
