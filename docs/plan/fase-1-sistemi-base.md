# Fase 1 — Sistemi di base (ERP mock + CRM mock)

> Indice: [plan.md](plan.md) · Precedente: [Fase 0](fase-0-setup-scaffolding.md) · Successiva: [Fase 2](fase-2-server-mcp.md)

## Obiettivo

Avere i due "sistemi aziendali" funzionanti via HTTP, senza agenti: l'ERP mock (`Erp.Api` + EF Core + seed) e il CRM mock (`ICrmClient` persistente dentro `Crm.Mcp`), con i dati di scenario per la demo.

**Accettazione di §11**: si crea un ordine e si legge una giacenza via HTTP; il deal di test è leggibile dal CRM.

## Prerequisiti

Fase 0 completata (solution, LocalDB `localdev`, AppHost avviabile).

## Gate di fase

| # | Domanda | Proposta |
|---|---------|----------|
| G1.1 | Settore merceologico dei dati demo | Forniture industriali (cuscinetti, motori, sensori, quadri): SKU tipo `IND-BRG-001` |
| G1.2 | Formato `OrderNumber` | `SO-2026-000001` da sequence SQL `erp.OrderNumberSeq` |
| G1.3 | Semantica di `Revision` del deal | Si incrementa solo su modifiche commerciali (importo, righe, azienda); **non** sugli aggiornamenti di stato scritti da O2C, così la `idempotencyKey` resta stabile durante il workflow |
| G1.4 | Migrazioni | Applicate all'avvio solo in `Development`; in cloud (Fase 6) job dedicato |
| G1.5 | `Erp.Api` rifiuta ordini per clienti `IsBlocked`? | **No**: il blocco è governato dalla policy dell'orchestratore (Fase 5); l'ERP registra l'ordine approvato |
| G1.6 | Autenticazione su `Erp.Api` | Nessuna in locale (è chiamata solo da `Erp.Mcp` sulla rete interna); API key sui server MCP in Fase 2 |

## Step operativi

### ERP mock — `src/Erp.Api`

**1.1 — Modello e DbContext**
- `ErpDbContext` con `HasDefaultSchema("erp")` e history table delle migrazioni nello schema `erp`.
- Entità (§8): `Customer` (`CustomerId` int identity, `Name`, `VatNumber`, `Email`, `Address`, `CreditLimit` decimal(18,2), `IsBlocked`), `Product` (`Sku` PK, `Description`, `ListPrice` decimal(18,2), `Uom`), `StockLevel` (`Sku` PK/FK, `OnHand`, `Reserved`, `LeadTimeDays`, `RowVersion`), `Order` (`OrderId` GUID, `OrderNumber`, `CustomerId`, `Total`, `Status` = `OrderStatus`, `ExternalRef`, `IdempotencyKey`, `CreatedAt` UTC), `OrderLine` (`OrderLineId`, `OrderId`, `Sku`, `Quantity`, `UnitPrice`).
- Indici: **univoco su `Orders.IdempotencyKey`** (§8); univoco filtrato su `Customers.VatNumber`; indice su `Customers.Email`; indice su `Orders.ExternalRef`.
- Sequence `erp.OrderNumberSeq` per `OrderNumber` (G1.2).

**1.2 — Migrazioni**
- `dotnet ef migrations add InitialErp` (tool locale dalla Fase 0).
- `MigrateAsync()` all'avvio in `Development` (G1.4).

**1.3 — Seed deterministico** (idempotente: inserisce solo se le tabelle sono vuote)
- ≥ 20 prodotti con prezzi realistici.
- 10 clienti: 1 bloccato (`IsBlocked = true`), partite IVA coerenti con le aziende del CRM tranne quella del "cliente nuovo".
- Scorte miste: prodotti abbondanti, prodotti a zero, prodotti con giacenza bassa, lead time 2–30 giorni.

**1.4 — Endpoint minimal API** (gruppo `/api`, errori come ProblemDetails con estensione `code` del catalogo `ToolErrorCodes`)

| Endpoint | Comportamento |
|----------|---------------|
| `GET /api/customers?vatNumber=&email=` | 200 cliente · 404 non trovato · 400 se mancano entrambi |
| `POST /api/customers` | 201 `{ customerId }` · 409 se la partita IVA esiste |
| `GET /api/stock/{sku}?quantity=` | 200 `{ sku, available, onHand, leadTimeDays }` con `available = OnHand − Reserved ≥ quantity` (D21) · 404 SKU sconosciuto |
| `POST /api/orders` | vedi 1.5 |
| `GET /api/orders/{orderId}` | 200 ordine con righe · 404 |

**1.5 — Creazione ordine idempotente**
- In un'unica transazione: se esiste un ordine con la stessa `IdempotencyKey` → **200 con l'ordine esistente**; altrimenti valida cliente e SKU, calcola il totale dai prezzi delle righe (prezzo del deal, D21), inserisce ordine e righe, incrementa `Reserved` per ogni riga; `Status = Confirmed` se tutte le righe erano disponibili, altrimenti `Backorder` → 201.
- Race condition: se l'insert viola l'indice univoco (`DbUpdateException` con errore SQL 2601/2627) → rileggere l'ordine esistente e restituirlo con 200.
- `ExternalRef` = `dealId`.

**1.6 — File `.http`**: `src/Erp.Api/Erp.Api.http` con tutti gli endpoint e un doppio POST con la stessa chiave.

### CRM mock — `src/Crm.Mcp`

**1.7 — Persistenza e client**
- `CrmDbContext` schema `crm`: `Company` (`CompanyId`, `Name`, `VatNumber`, `Email`, `Address`), `Deal` (`DealId` string es. `D-1001`, `Name`, `Amount`, `Currency`, `Stage`, `CompanyId`, `Revision`, `ErpOrderNumber`, `O2CStatus`, `LastNote`, `UpdatedAt`), `DealLineItem` (`Sku`, `Quantity`, `UnitPrice`), `DealNote` (storico delle note scritte da O2C, per audit).
- `ICrmClient` con `GetDealAsync`, `GetCompanyAsync`, `UpdateDealAsync`; implementazione `MockCrmClient` su EF. `UpdateDealAsync` **non** incrementa `Revision` (G1.3) e aggiunge una `DealNote`.
- Migrazione `InitialCrm` + `MigrateAsync()` in `Development`.

**1.8 — Seed degli scenari demo** (D5; tutti i deal partono in stage `ContractSent`)

| Deal | Scenario | Esito atteso a regime (Fase 5) |
|------|----------|-------------------------------|
| D-1001 | Cliente esistente, tutto disponibile, totale < 10.000 € | Ordine creato senza approvazione |
| D-1002 | Totale > 10.000 €, stock ok | Approvazione (soglia) |
| D-1003 | Una riga con stock insufficiente | Approvazione (backorder) |
| D-1004 | Azienda non presente in ERP | Approvazione (cliente nuovo) |
| D-1005 | Cliente bloccato in ERP | Approvazione obbligatoria |
| D-1006 | Valuta USD | `Discarded` |
| D-1007 | Uno SKU inesistente in ERP | `Failed` |
| D-1008 | Sopra soglia **e** cliente nuovo | Approvazione con più motivi |

**1.9 — Endpoint dev** (solo in `Development`)
- `GET /dev/deals`, `GET /dev/deals/{dealId}` (deal + righe + revision + stato O2C).
- `POST /dev/deals/{dealId}/close-won` → stage `ClosedWon` (la pubblicazione sul broker arriva in Fase 4).
- `POST /dev/reset` → ripristina i dati di scenario (deal, note; utile per ripetere la demo).
- File `src/Crm.Mcp/Crm.Mcp.dev.http`.

### Test

**1.10 — `tests/Erp.Api.Tests`**
- `WebApplicationFactory<Program>` con database di test dedicato su `(localdb)\localdev` (`O2C_Test_<guid>`, creato e cancellato dalla fixture).
- Casi: giacenza SKU noto/sconosciuto; `available` con `Reserved` > 0; creazione cliente e duplicato (409); ricerca cliente per partita IVA ed email; ordine felice (totale, stato, riserva); **doppio POST con stessa chiave → stesso `orderId`, una sola riga in `Orders`**; **POST paralleli con stessa chiave → un solo ordine**; validazioni (400).

**1.11 — `tests/Mcp.Tests` (parte CRM)**
- `MockCrmClient` su DB di test: lettura deal con righe e revision; `UpdateDealAsync` non cambia `Revision` e scrive una nota; reset.

## Criteri di accettazione

- [ ] Da `.http`: creato un ordine su `Erp.Api` con `OrderNumber` valorizzato; giacenza letta correttamente.
- [ ] Doppio POST con la stessa `IdempotencyKey` → stesso ordine, nessun duplicato.
- [ ] `GET /dev/deals/D-1001` restituisce il deal con righe e `revision`.
- [ ] Nel dashboard Aspire la traccia del `POST /api/orders` porta `correlation.id`.
- [ ] Nessun agente coinvolto.
- [ ] DoD comune soddisfatta.

## Rischi

| Rischio | Mitigazione |
|---------|-------------|
| Test lenti o instabili con LocalDB | Un DB per classe di test, creazione con `EnsureCreated`/migrazioni una volta per fixture |
| Dati di seed non allineati tra ERP e CRM | Seed dei due sistemi generato da una tabella di scenari unica, documentata nel README di demo |

## Definition of Done

DoD comune di [plan.md](plan.md#definition-of-done-comune-a-ogni-fase).

## Esito

_Da compilare a fine fase._
