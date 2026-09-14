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
| G1.4 | Migrazioni | ~~Applicate all'avvio solo in `Development`~~ — **Rivista dall'utente (D30)**: niente migration; il database si crea da zero dal modello con il tool `tools/Dusiburg.AI.O2C.DbInit` (anche per i test); in cloud (Fase 6) da decidere |
| G1.8 | Enum persistiti | **Deciso dall'utente (D30)**: FK verso tabelle di lookup con PK tinyint = valore esplicito dell'enum, righe generate dal codice; nuovo enum `DealStage` per lo stage del deal |
| G1.9 | Nomi delle tabelle | **Deciso dall'utente (D31)**: singolare, niente pluralizzazioni (`Order`, `OrderStatus`, `Deal`), anche nei check constraint; con EF si imposta con `ToTable` |
| G1.5 | `Erp.Api` rifiuta ordini per clienti `IsBlocked`? | **No**: il blocco è governato dalla policy dell'orchestratore (Fase 5); l'ERP registra l'ordine approvato |
| G1.6 | Autenticazione su `Erp.Api` | Nessuna in locale (è chiamata solo da `Erp.Mcp` sulla rete interna); API key sui server MCP in Fase 2 |
| G1.7 | Naming e tipo delle chiavi | **Deciso dall'utente (D29)**: PK numerica sempre `Id` (mai `<Entity>Id`), FK qualificate; niente PK GUID (il GUID pubblico è una colonna univoca dedicata); chiavi di business stringa come colonne univoche; contratti §6 invariati, mapping nel codice |

## Step operativi

### ERP mock — `src/Dusiburg.AI.O2C.Erp.Api`

**1.1 — Modello e DbContext** ✅ (2026-09-14)
- `ErpDbContext` con `HasDefaultSchema("erp")`, nella libreria `src/Dusiburg.AI.O2C.Erp.Data` (D30) referenziata da `Erp.Api`.
- Entità (§8, convenzioni D29/D30): `Customer` (`Id` int identity, `Name`, `VatNumber`, `Email`, `Address`, `CreditLimit` decimal(18,2), `IsBlocked`), `Product` (`Id` int identity, `Sku` univoco, `Description`, `ListPrice` decimal(18,2), `Uom`), `StockLevel` (`Id` int identity, `ProductId` FK univoca 1:1, `OnHand`, `Reserved`, `LeadTimeDays`, `RowVersion`), `Order` (`Id` int identity, `PublicId` GUID univoco = `orderId` dei contratti, `OrderNumber` univoco, `CustomerId`, `Total`, `OrderStatusId` tinyint FK, `ExternalRef`, `IdempotencyKey`, `CreatedAt` UTC), `OrderLine` (`Id` int identity, `OrderId`, `ProductId`, `Quantity`, `UnitPrice`), lookup `OrderStatus` (`Id` tinyint = valore di `OrderStatus`, `Name`).
- Indici: **univoco su `Order.IdempotencyKey`** (§8); univoci su `Order.PublicId`, `Order.OrderNumber`, `Product.Sku`, `StockLevel.ProductId`, `OrderStatus.Name`; univoco filtrato su `Customer.VatNumber`; indice su `Customer.Email`; indice su `Order.ExternalRef`. Check constraint su quantità, prezzi e giacenze non negativi.
- Sequence `erp.OrderNumberSeq` per `OrderNumber` (G1.2): il formato `SO-yyyy-000000` lo compone il codice in 1.5.

**1.2 — Creazione del database** ✅ (2026-09-14, D30: niente migration)
- Tool `tools/Dusiburg.AI.O2C.DbInit`: `dotnet run --project tools/Dusiburg.AI.O2C.DbInit` cancella e ricrea il database (default `(localdb)\localdev`, `O2C`; oppure `ConnectionStrings__sql` o connection string come argomento), crea le tabelle di `erp` (`EnsureCreated`) e di `crm` (`CreateTables`) e popola le lookup dagli enum. Rifiuta server non LocalDB senza `--allow-non-local`.
- La logica sta in `O2CDatabaseInitializer.RecreateAsync`, riusabile dalle fixture dei test (1.10).
- Eseguito sul database `O2C` di `localdev`, senza seed, per la revisione dello schema.
- Da fare con gli endpoint: registrazione dei DbContext in DI; niente creazione del database all'avvio dei servizi.

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

**1.6 — File `.http`**: `src/Dusiburg.AI.O2C.Erp.Api/Erp.Api.http` con tutti gli endpoint e un doppio POST con la stessa chiave.

### CRM mock — `src/Dusiburg.AI.O2C.Crm.Mcp`

**1.7 — Persistenza e client** (persistenza ✅ 2026-09-14; client da fare)
- `CrmDbContext` schema `crm`, nella libreria `src/Dusiburg.AI.O2C.Crm.Data` (D30) referenziata da `Crm.Mcp`; convenzioni D29/D30: `Company` (`Id` int identity, `Code` univoco es. `C-01` = `companyId`, `Name`, `VatNumber`, `Email`, `Address`), `Deal` (`Id` int identity, `Code` univoco es. `D-1001` = `dealId`, `Name`, `Amount`, `Currency` char(3), `DealStageId` tinyint FK, `CompanyId` FK, `Revision`, `ErpOrderNumber`, `DealStatusId` tinyint FK nullable = stato O2C, `LastNote`, `UpdatedAt`), `DealLineItem` (`Id` int identity, `DealId` FK, `Sku` senza FK verso l'ERP, `Quantity`, `UnitPrice`), `DealNote` (`Id` int identity, `DealId` FK, `DealStatusId` tinyint FK, `ErpOrderNumber`, `Note`, `CreatedAt`: storico delle scritture di O2C, per audit), lookup `DealStage` (enum `DealStage`: `ContractSent` = 1, `ClosedWon` = 2, `ClosedLost` = 3) e `DealStatus` (enum `DealStatus` di `Shared`).
- `ICrmClient` con `GetDealAsync`, `GetCompanyAsync`, `UpdateDealAsync`; implementazione `MockCrmClient` su EF. `UpdateDealAsync` **non** incrementa `Revision` (G1.3) e aggiunge una `DealNote`. Il contratto `get_deal` espone `stage` per nome (`DealStage.ToString()`).
- Tabelle create da `tools/Dusiburg.AI.O2C.DbInit` insieme a quelle di `erp` (vedi 1.2), nessuna migration.

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
- File `src/Dusiburg.AI.O2C.Crm.Mcp/Crm.Mcp.dev.http`.

### Test

**1.10 — `tests/Dusiburg.AI.O2C.Erp.Api.Tests`**
- `WebApplicationFactory<Program>` con database di test dedicato su `(localdb)\localdev` (`O2C_Test_<guid>`, creato e cancellato dalla fixture).
- Casi: giacenza SKU noto/sconosciuto; `available` con `Reserved` > 0; creazione cliente e duplicato (409); ricerca cliente per partita IVA ed email; ordine felice (totale, stato, riserva); **doppio POST con stessa chiave → stesso `orderId`, una sola riga in `Order`**; **POST paralleli con stessa chiave → un solo ordine**; validazioni (400).

**1.11 — `tests/Dusiburg.AI.O2C.Mcp.Tests` (parte CRM)**
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
