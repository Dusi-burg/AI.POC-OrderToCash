using Dusiburg.AI.O2C.Shared.Demo;

namespace Dusiburg.AI.O2C.Mcp.Tests;

/// <summary>
/// Invarianti degli scenari demo (1.8, D5): se il catalogo cambia, gli esiti attesi descritti in docs/demo.md devono restare veri.
/// </summary>
public class DemoCatalogTests
{
    private static readonly Dictionary<string, DemoProduct> Products = DemoCatalog.Products.ToDictionary(p => p.Sku);

    [TestCase("D-1001", false, false, "EUR", false, 0, 0)]
    [TestCase("D-1002", false, false, "EUR", true, 0, 0)]
    [TestCase("D-1003", false, false, "EUR", false, 1, 0)]
    [TestCase("D-1004", true, false, "EUR", false, 0, 0)]
    [TestCase("D-1005", false, true, "EUR", false, 0, 0)]
    [TestCase("D-1006", false, false, "USD", false, 0, 0)]
    [TestCase("D-1007", false, false, "EUR", false, 0, 1)]
    [TestCase("D-1008", true, false, "EUR", true, 0, 0)]
    public void Deal_MatchesScenario(
        string dealId, bool newCustomer, bool blockedCustomer, string currency, bool aboveThreshold, int unavailableLines, int unknownSkus)
    {
        //SUT
        var deal = DemoCatalog.Deals.Single(d => d.DealId == dealId);
        var company = DemoCatalog.Companies.Single(c => c.CompanyId == deal.CompanyId);
        var customer = DemoCatalog.Customers.SingleOrDefault(c => c.VatNumber == company.VatNumber);

        Assert.That(customer is null, Is.EqualTo(newCustomer), "cliente nuovo");
        Assert.That(customer?.IsBlocked ?? false, Is.EqualTo(blockedCustomer), "cliente bloccato");
        Assert.That(deal.Currency, Is.EqualTo(currency), "valuta");
        Assert.That(deal.Amount > DemoCatalog.ApprovalThresholdEur, Is.EqualTo(aboveThreshold), "sopra soglia");
        Assert.That(deal.Lines.Count(l => !Products.ContainsKey(l.Sku)), Is.EqualTo(unknownSkus), "SKU inesistenti");
        Assert.That(deal.Lines.Count(l => Products.TryGetValue(l.Sku, out var p) && p.OnHand - p.Reserved < l.Quantity), Is.EqualTo(unavailableLines), "righe non disponibili");
    }

    [Test]
    public void Deals_RunInSequence_DoNotConsumeEachOtherStock()
    {
        //SUT
        // Gli ordini che la demo può creare (EUR, SKU tutti noti) non devono far cambiare esito ai deal successivi.
        var orderable = DemoCatalog.Deals.Where(d => d.Currency == "EUR" && d.Lines.All(l => Products.ContainsKey(l.Sku)));

        var overbooked = orderable
            .SelectMany(d => d.Lines)
            .Where(l => Products[l.Sku].OnHand - Products[l.Sku].Reserved >= l.Quantity)
            .GroupBy(l => l.Sku)
            .Where(g => g.Sum(l => l.Quantity) > Products[g.Key].OnHand - Products[g.Key].Reserved)
            .Select(g => g.Key);

        Assert.That(overbooked, Is.Empty);
    }

    [Test]
    public void Catalog_HasExpectedShape()
    {
        //SUT
        Assert.That(DemoCatalog.Products, Has.Count.GreaterThanOrEqualTo(20));
        Assert.That(DemoCatalog.Customers, Has.Count.EqualTo(10));
        Assert.That(DemoCatalog.Customers.Count(c => c.IsBlocked), Is.EqualTo(1));
        Assert.That(DemoCatalog.Products.Select(p => p.Sku), Is.Unique);
        Assert.That(DemoCatalog.Customers.Select(c => c.VatNumber), Is.Unique);
        Assert.That(DemoCatalog.Companies.Select(c => c.CompanyId), Is.Unique);
        Assert.That(DemoCatalog.Deals.Select(d => d.DealId), Is.Unique);
        Assert.That(DemoCatalog.Products.Any(p => p.OnHand == 0), Is.True, "prodotti a zero");
        Assert.That(DemoCatalog.Products.Any(p => p.OnHand is > 0 and <= 5), Is.True, "prodotti con giacenza bassa");
        Assert.That(DemoCatalog.Products.Select(p => p.LeadTimeDays), Is.All.InRange(2, 30));
        Assert.That(DemoCatalog.Deals.Select(d => d.CompanyId), Is.SubsetOf(DemoCatalog.Companies.Select(c => c.CompanyId)));
    }
}
