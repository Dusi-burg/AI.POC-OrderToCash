namespace Dusiburg.AI.O2C.Shared.Demo;

/// <summary>
/// Tabella unica degli scenari demo (D5): da qui nascono i seed dell'ERP e del CRM mock, così i due sistemi restano coerenti
/// (partite IVA, SKU, giacenze). Descritta in <c>docs/demo.md</c>; gli invarianti degli scenari sono verificati dai test.
/// </summary>
public static class DemoCatalog
{
    /// <summary>Soglia di approvazione di default (§10, <c>APPROVAL_THRESHOLD_EUR</c>) attorno a cui sono costruiti gli scenari.</summary>
    public const decimal ApprovalThresholdEur = 10_000m;

    public static IReadOnlyList<DemoProduct> Products { get; } =
    [
        new("IND-BRG-001", "Cuscinetto radiale a sfere 6205-2RS", 12.50m, "PZ", OnHand: 500, Reserved: 20, LeadTimeDays: 3),
        new("IND-BRG-002", "Cuscinetto a rulli conici 30208", 38.90m, "PZ", OnHand: 180, Reserved: 0, LeadTimeDays: 5),
        new("IND-BRG-003", "Cuscinetto orientabile a rulli 22212", 145.00m, "PZ", OnHand: 40, Reserved: 0, LeadTimeDays: 10),
        new("IND-BRG-004", "Supporto ritto UCP 206", 29.80m, "PZ", OnHand: 120, Reserved: 0, LeadTimeDays: 4),
        new("IND-MOT-001", "Motore asincrono trifase 1,5 kW IE3", 310.00m, "PZ", OnHand: 25, Reserved: 0, LeadTimeDays: 14),
        new("IND-MOT-002", "Motore asincrono trifase 4 kW IE3", 590.00m, "PZ", OnHand: 12, Reserved: 2, LeadTimeDays: 14),
        new("IND-MOT-003", "Motore brushless 750 W con encoder", 845.00m, "PZ", OnHand: 3, Reserved: 0, LeadTimeDays: 21),
        new("IND-MOT-004", "Motoriduttore coassiale 0,75 kW i=20", 720.00m, "PZ", OnHand: 0, Reserved: 0, LeadTimeDays: 30),
        new("IND-SEN-001", "Sensore induttivo M18 PNP", 24.00m, "PZ", OnHand: 300, Reserved: 0, LeadTimeDays: 2),
        new("IND-SEN-002", "Sensore fotoelettrico a riflessione", 58.00m, "PZ", OnHand: 150, Reserved: 10, LeadTimeDays: 3),
        new("IND-SEN-003", "Encoder incrementale 1024 impulsi/giro", 189.00m, "PZ", OnHand: 20, Reserved: 0, LeadTimeDays: 7),
        new("IND-SEN-004", "Trasduttore di pressione 0-10 bar 4-20 mA", 132.00m, "PZ", OnHand: 0, Reserved: 0, LeadTimeDays: 20),
        new("IND-PNL-001", "Quadro elettrico IP55 600x400x200", 265.00m, "PZ", OnHand: 15, Reserved: 0, LeadTimeDays: 10),
        new("IND-PNL-002", "Inverter 2,2 kW 400 V", 410.00m, "PZ", OnHand: 18, Reserved: 0, LeadTimeDays: 7),
        new("IND-PNL-003", "Interruttore magnetotermico 4P 32 A", 64.50m, "PZ", OnHand: 90, Reserved: 0, LeadTimeDays: 3),
        new("IND-PNL-004", "PLC compatto 24 I/O", 980.00m, "PZ", OnHand: 6, Reserved: 0, LeadTimeDays: 15),
        new("IND-GRX-001", "Riduttore a vite senza fine i=30", 275.00m, "PZ", OnHand: 30, Reserved: 0, LeadTimeDays: 10),
        new("IND-GRX-002", "Giunto elastico a denti D38", 46.00m, "PZ", OnHand: 75, Reserved: 0, LeadTimeDays: 5),
        new("IND-BLT-001", "Cinghia trapezoidale SPA 1250", 9.80m, "PZ", OnHand: 400, Reserved: 0, LeadTimeDays: 2),
        new("IND-BLT-002", "Cinghia dentata HTD 8M-1200-30", 42.00m, "PZ", OnHand: 60, Reserved: 0, LeadTimeDays: 6),
        new("IND-LUB-001", "Grasso al litio EP2 cartuccia 400 g", 7.90m, "PZ", OnHand: 800, Reserved: 0, LeadTimeDays: 2),
        new("IND-CAB-001", "Cavo schermato 4x1,5 mm2", 3.20m, "M", OnHand: 2500, Reserved: 0, LeadTimeDays: 4),
    ];

    public static IReadOnlyList<DemoCustomer> Customers { get; } =
    [
        new("Officine Meccaniche Brambilla S.r.l.", "IT01234560157", "acquisti@brambilla-om.it", "Via dell'Industria 12, 20054 Segrate (MI)", 50_000m, IsBlocked: false),
        new("Tessitura Valle Seriana S.p.A.", "IT02345670163", "ordini@tessituravs.it", "Via Provinciale 45, 24021 Albino (BG)", 80_000m, IsBlocked: false),
        new("Automazioni Nord-Est S.r.l.", "IT03456780268", "purchasing@automazioni-ne.it", "Viale Venezia 88, 31100 Treviso (TV)", 60_000m, IsBlocked: false),
        new("Cartiera del Brenta S.p.A.", "IT04567890280", "approvvigionamenti@cartierabrenta.it", "Via Riviera 3, 35010 Vigonza (PD)", 120_000m, IsBlocked: false),
        new("Fonderia Emiliana S.r.l.", "IT05678900375", "acquisti@fonderiaemiliana.it", "Via Emilia Ovest 210, 41123 Modena (MO)", 30_000m, IsBlocked: true),
        new("Imballaggi Tirreno S.r.l.", "IT06789010484", "ordini@imballaggitirreno.it", "Via Aurelia 150, 57121 Livorno (LI)", 40_000m, IsBlocked: false),
        new("Plastiche Adriatiche S.r.l.", "IT07890120418", "acquisti@plasticheadriatiche.it", "Strada Montefeltro 21, 61122 Pesaro (PU)", 35_000m, IsBlocked: false),
        new("Meccanica di Precisione Torinese S.p.A.", "IT08901230019", "supply@mptorinese.it", "Corso Francia 300, 10146 Torino (TO)", 90_000m, IsBlocked: false),
        new("Agroalimentare Salento S.r.l.", "IT09012340751", "ordini@agrosalento.it", "Via Lecce-Surbo 7, 73100 Lecce (LE)", 25_000m, IsBlocked: false),
        new("Lavorazioni Lamiera Friuli S.r.l.", "IT10123450302", "acquisti@lamierafriuli.it", "Via Pradamano 55, 33100 Udine (UD)", 45_000m, IsBlocked: false),
    ];

    public static IReadOnlyList<DemoCompany> Companies { get; } =
    [
        new("C-01", "Officine Meccaniche Brambilla S.r.l.", "IT01234560157", "acquisti@brambilla-om.it", "Via dell'Industria 12, 20054 Segrate (MI)"),
        new("C-02", "Cartiera del Brenta S.p.A.", "IT04567890280", "approvvigionamenti@cartierabrenta.it", "Via Riviera 3, 35010 Vigonza (PD)"),
        new("C-03", "Automazioni Nord-Est S.r.l.", "IT03456780268", "purchasing@automazioni-ne.it", "Viale Venezia 88, 31100 Treviso (TV)"),
        new("C-04", "Nuova Robotica Marche S.r.l.", "IT11234560422", "acquisti@nuovaroboticamarche.it", "Via dell'Artigianato 9, 60027 Osimo (AN)"),
        new("C-05", "Fonderia Emiliana S.r.l.", "IT05678900375", "acquisti@fonderiaemiliana.it", "Via Emilia Ovest 210, 41123 Modena (MO)"),
        new("C-06", "Tessitura Valle Seriana S.p.A.", "IT02345670163", "ordini@tessituravs.it", "Via Provinciale 45, 24021 Albino (BG)"),
        new("C-07", "Imballaggi Tirreno S.r.l.", "IT06789010484", "ordini@imballaggitirreno.it", "Via Aurelia 150, 57121 Livorno (LI)"),
        new("C-08", "Siderurgica Lombarda Nuova S.p.A.", "IT12345670965", "procurement@siderlombarda.it", "Via Brescia 400, 25014 Castenedolo (BS)"),
    ];

    public static IReadOnlyList<DemoDeal> Deals { get; } =
    [
        new("D-1001", "Cliente esistente, tutto disponibile, sotto soglia: ordine senza approvazione",
            "Ricambi cuscinetti linea 2", "EUR", "C-01",
            [new("IND-BRG-001", 40, 12.00m), new("IND-BRG-004", 10, 28.50m), new("IND-LUB-001", 20, 7.50m), new("IND-SEN-001", 25, 22.80m)]),
        new("D-1002", "Sopra soglia, stock ok: approvazione per soglia",
            "Automazione impianto confezionamento", "EUR", "C-02",
            [new("IND-MOT-002", 8, 575.00m), new("IND-PNL-002", 8, 399.00m), new("IND-PNL-004", 2, 960.00m), new("IND-SEN-002", 20, 56.00m)]),
        new("D-1003", "Una riga con stock insufficiente: approvazione per backorder",
            "Retrofit motorizzazioni nastro trasportatore", "EUR", "C-03",
            [new("IND-MOT-003", 5, 830.00m), new("IND-SEN-003", 5, 185.00m), new("IND-GRX-002", 5, 45.00m)]),
        new("D-1004", "Azienda non presente in ERP: approvazione per cliente nuovo",
            "Prima fornitura cella robotizzata", "EUR", "C-04",
            [new("IND-SEN-001", 30, 23.50m), new("IND-PNL-003", 12, 62.00m), new("IND-CAB-001", 200, 3.10m)]),
        new("D-1005", "Cliente bloccato in ERP: approvazione obbligatoria",
            "Manutenzione straordinaria forni", "EUR", "C-05",
            [new("IND-BRG-003", 6, 142.00m), new("IND-MOT-001", 2, 305.00m), new("IND-BLT-001", 30, 9.50m)]),
        new("D-1006", "Valuta USD: scartato (Discarded)",
            "Fornitura trasmissioni per export", "USD", "C-06",
            [new("IND-GRX-001", 10, 290.00m), new("IND-BLT-002", 20, 44.00m)]),
        new("D-1007", "Uno SKU inesistente in ERP: fallito (Failed)",
            "Sensoristica linea imbottigliamento", "EUR", "C-07",
            [new("IND-SEN-001", 20, 23.00m), new("IND-SEN-999", 10, 75.00m), new("IND-PNL-003", 5, 63.00m)]),
        new("D-1008", "Sopra soglia e cliente nuovo: approvazione con più motivi",
            "Nuovo impianto di laminazione", "EUR", "C-08",
            [new("IND-MOT-001", 10, 300.00m), new("IND-PNL-001", 6, 255.00m), new("IND-GRX-001", 12, 270.00m), new("IND-BRG-002", 60, 37.50m), new("IND-PNL-003", 10, 62.00m)]),
    ];
}

public sealed record DemoProduct(string Sku, string Description, decimal ListPrice, string Uom, int OnHand, int Reserved, int LeadTimeDays);

public sealed record DemoCustomer(string Name, string VatNumber, string Email, string Address, decimal CreditLimit, bool IsBlocked);

/// <summary>Azienda del CRM mock: <see cref="CompanyId"/> è la chiave di business (<c>companyId</c> dei contratti).</summary>
public sealed record DemoCompany(string CompanyId, string Name, string VatNumber, string Email, string Address);

public sealed record DemoDealLine(string Sku, int Quantity, decimal UnitPrice);

/// <summary>Deal del CRM mock: parte sempre in stage <c>ContractSent</c> con revisione 1.</summary>
public sealed record DemoDeal(string DealId, string Scenario, string Name, string Currency, string CompanyId, IReadOnlyList<DemoDealLine> Lines)
{
    public decimal Amount => Lines.Sum(l => l.Quantity * l.UnitPrice);
}
