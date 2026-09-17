# Fase 6 — UI web di CRM ed ERP

> Indice: [plan.md](plan.md) · Precedente: [Fase 5](fase-5-human-in-the-loop.md) · Successiva: [Fase 7](fase-7-deploy-osservabilita.md)
>
> Fase aggiunta il 2026-09-17 (D57), fuori dalle sei fasi di §11: il deploy su Azure diventa la Fase 7.

## Obiettivo

Dare ai due sistemi di contorno una propria interfaccia web, come `Approvals.Web` per le approvazioni, così la demo si fa tutta dal browser e senza chiamate manuali agli endpoint `/dev`:

- **`Crm.Web`** — il front-end del CRM: deal (stage, importo, righe, revisione, stato O2C, ordine ERP, storico delle note scritte da agenti e host), aziende con i loro deal, e i comandi **Chiudi vinto** (avvia il flusso O2C) e **Chiudi perso**.
- **`Erp.Web`** — il front-end dell'ERP, **in sola lettura**: clienti (anagrafica, fido, blocco, ordini), magazzino (prodotti, giacenza, riservato, disponibile, lead time) e ordini ricevuti (righe, totale, stato, backorder, deal di origine).

Le due UI non toccano i database: leggono e scrivono **tramite i servizi proprietari degli schemi** (`Crm.Mcp` per `crm`, `Erp.Api` per `erp`), che restano gli unici a conoscerli (D18, D58).

**Accettazione** (fase fuori da §11, criteri propri):
- da `Crm.Web` un clic su **Chiudi vinto** avvia il workflow, e la pagina del deal mostra lo stato O2C e le note man mano che arrivano;
- l'ordine creato è visibile in `Erp.Web` con righe, totale, eventuale nota di backorder e link al deal; il magazzino mostra la riserva;
- **Chiudi perso** porta il deal in `ClosedLost` senza pubblicare eventi e senza avviare workflow;
- `Erp.Web` non offre alcuna azione di scrittura.

## Prerequisiti

Fase 5 completata (flusso con approvazione, `Approvals.Web`, `docs/demo.md`).

## Decisioni già prese (2026-09-17)

| ID | Decisione |
|----|-----------|
| D57 | La fase entra prima del deploy come **Fase 6**; il deploy diventa **Fase 7** (`fase-7-deploy-osservabilita.md`) |
| D58 | **Due applicazioni nuove**, `src/Dusiburg.AI.O2C.Crm.Web` (porta 5105) e `src/Dusiburg.AI.O2C.Erp.Web` (porta 5106), Razor Pages come `Approvals.Web`, che parlano **solo HTTP** con `Crm.Mcp` ed `Erp.Api` |
| D59 | Comando di chiusura con **due esiti**: `Won` (stage `ClosedWon` + evento `deal-closed-won`) e `Lost` (stage `ClosedLost`, nessun evento) |

## Gate di fase

> Chiuso il 2026-09-17: **tutte le proposte accettate** (G6.3, G6.4, G6.5 e G6.12 confermate esplicitamente dall'utente, le altre per default).

| # | Domanda | Proposta |
|---|---------|----------|
| G6.1 | Forma delle API di lettura | Gruppo **`/api/views`** su entrambi i servizi, separato dagli endpoint dei tool di §6 (che restano invariati: `GET /api/customers` resta la ricerca di `get_customer`). Mappato **in tutti gli ambienti**, non solo in Development: è la superficie degli utenti del sistema, non un supporto alla demo |
| G6.2 | Dove stanno i DTO delle viste | `src/Dusiburg.AI.O2C.Shared/Contracts/Views/Crm` e `.../Views/Erp`: record condivisi fra servizio e UI, serializzati con le opzioni web (enum come stringa) |
| G6.3 | Semantica del comando di chiusura | `POST /api/deals/{dealId}/close` con `{ outcome: "Won" \| "Lost" }`. Ammesso **solo da `ContractSent`**; da uno stage già chiuso → **409 `CONFLICT`** (niente riapertura: per ripartire c'è il reset). La revisione non cambia (G1.3). `Won` pubblica `deal-closed-won` dopo il salvataggio; se la pubblicazione fallisce lo stage resta `ClosedWon` e la risposta è **503 `UPSTREAM_UNAVAILABLE`** con la possibilità di ripubblicare (vedi G6.4) |
| G6.4 | Endpoint dev esistenti | `POST /dev/deals/{dealId}/close-won` resta per gli script e per **ripubblicare** l'evento di un deal già vinto (comportamento attuale), ma delega allo stesso servizio di chiusura. `GET /dev/deals` e `GET /dev/deals/{dealId}` si **tolgono**: li sostituiscono le viste. `POST /dev/reset` invariato |
| G6.5 | Autenticazione | Nessuna in locale, come `/approvals` (D25); login Entra e protezione delle API in Fase 7 (G7.4) |
| G6.6 | Filtri e paginazione | Nessuna paginazione (il seed ha 8 deal e una manciata di prodotti): limite fisso di 200 righe con ordinamento stabile. Filtri: deal per stage, stato O2C e azienda; ordini per stato e cliente; magazzino "solo sotto scorta" (`OnHand − Reserved ≤ 0`) |
| G6.7 | Aggiornamento della pagina del deal | Senza JavaScript (come G5.7): `<meta http-equiv="refresh" content="5">` finché il deal è `ClosedWon` e lo stato O2C non è terminale — cioè è ancora vuoto oppure `ApprovalPending`, perché la decisione può arrivare in qualunque momento — più un pulsante "Aggiorna". Il refresh si ferma su `OrderCreated`, `Rejected`, `Expired`, `Discarded`, `Failed` |
| G6.8 | Collegamenti fra le UI | Deal → ordine in `Erp.Web` (per numero d'ordine, l'unico che il CRM conosce) e → richieste di approvazione del deal in `Approvals.Web` (nuovo filtro `?dealId=`); ordine ERP → deal in `Crm.Web` (da `ExternalRef`); richiesta di approvazione → deal in `Crm.Web` al posto dell'endpoint dev. Gli URL arrivano dall'AppHost con i riferimenti agli endpoint delle risorse (`Links__CrmWeb`, `Links__ErpWeb`, `Links__ApprovalsWeb`): sostituiscono `Approvals__CrmDevBaseUrl` |
| G6.9 | Layout comune | Nessuna Razor Class Library condivisa: ogni app ha il proprio layout Bootstrap (template come `Approvals.Web`), con una barra che porta alle altre due UI e un colore di testata diverso per sistema, così in demo si capisce subito "dove si è" |
| G6.10 | Stato del workflow nelle UI | **Non** mostrato: il CRM conosce solo ciò che O2C gli scrive (stato e note), l'ERP solo gli ordini. Lo schema `orch` resta di orchestratore e `Approvals.Web` |
| G6.11 | Reset dalla UI | No: il reset resta negli script di `docs/demo.md` (con `DbInit` per lo schema `orch`) |
| G6.12 | Test delle UI | Nuovo progetto `tests/Dusiburg.AI.O2C.Web.Tests` (NUnit, D28) per `Crm.Web` ed `Erp.Web` con `WebApplicationFactory` e client HTTP finti; le API nuove si provano nei progetti di test esistenti, sui servizi reali e su database di test |
| G6.13 | Lingua | Interfaccia in italiano, come `Approvals.Web`; importi in formato `N2 €`, date in ora locale |

## Step operativi

**6.1 — Contratti delle viste** (`src/Dusiburg.AI.O2C.Shared/Contracts/Views/`)
- CRM: `CompanySummaryView`, `CompanyDetailView` (anagrafica + deal), `DealSummaryView` (codice, nome, azienda, importo, valuta, stage, revisione, stato O2C, ordine ERP, ultimo aggiornamento), `DealDetailView` (+ righe, ultima nota, storico note), `CloseDealRequest { Outcome }`, enum `DealCloseOutcome { Won, Lost }`.
- ERP: `CustomerSummaryView`, `CustomerDetailView` (+ ordini), `StockItemView` (SKU, descrizione, UoM, prezzo di listino, `OnHand`, `Reserved`, `Available`, lead time), `OrderSummaryView` (numero, `PublicId`, cliente, totale, stato, deal, data, backorder sì/no), `OrderDetailView` (+ righe con descrizione prodotto, `BackorderNote`, `IdempotencyKey`).
- Test di serializzazione in `ContractSerializationTests` (enum per nome).

**6.2 — API del CRM** (`src/Dusiburg.AI.O2C.Crm.Mcp/Api/`)
- `CrmViewEndpoints`: `GET /api/views/companies`, `/api/views/companies/{companyId}`, `/api/views/deals?stage=&o2cStatus=&companyId=`, `/api/views/deals/{dealId}`; query `AsNoTracking`, errori come ProblemDetails con `code` (D32).
- `DealClosingService` (unico punto di chiusura): transizione solo da `ContractSent` (G6.3), `UpdatedAt` aggiornato, revisione invariata; con `Won` pubblica `DealClosedWon` dopo il salvataggio. Metodo separato `RepublishClosedWonAsync` per l'endpoint dev (G6.4).
- `DealCommandEndpoints`: `POST /api/deals/{dealId}/close` → 200 con `DealDetailView` · 400 esito non valido · 404 · 409 stage già chiuso · 503 evento non pubblicato.
- Publisher dietro interfaccia `IDealEventPublisher` (oggi `DealEventPublisher` è una classe concreta): i test della chiusura non devono aver bisogno di un broker, come `IApprovalDecisionPublisher` in Fase 5.
- `CrmDevEndpoints`: `close-won` delega al servizio; tolte le due GET (G6.4); i record `DevDealSummary`/`DevDealDetail`/`DevDealNote` spariscono a favore delle viste.
- Il middleware dell'API key resta limitato a `/mcp`: verificare che `/api/*` non ne sia coperto e che `/mcp` lo resti.
- Span `crm.deal.close` con `deal.id`, `deal.close.outcome`, `correlation.id` (il comando è l'ingresso del flusso: il `x-correlation-id` generato qui deve arrivare nell'header del messaggio, come oggi da `/dev`).

**6.3 — API dell'ERP** (`src/Dusiburg.AI.O2C.Erp.Api/Views/`)
- `ErpViewEndpoints`: `GET /api/views/customers`, `/api/views/customers/{id:int}`, `/api/views/stock?shortOnly=`, `/api/views/orders?status=&customerId=`, `/api/views/orders/{orderNumber}`.
- `Available = OnHand − Reserved` calcolato nella query; può essere negativo (D55) e la vista lo espone così com'è.
- Solo GET: nessun endpoint di scrittura nuovo.

**6.4 — `src/Dusiburg.AI.O2C.Crm.Web`** (porta 5105)
- Pagine: `/` (cruscotto: deal per stage e per stato O2C), `/deals` (elenco con filtri G6.6), `/deals/{dealId}` (dettaglio, righe con totale riga, storico note, refresh G6.7, link G6.8), `/companies`, `/companies/{companyId}`.
- Azioni sul dettaglio di un deal `ContractSent`: **Chiudi vinto** e **Chiudi perso** (form POST, antiforgery, conferma lato server con messaggio di esito; nessuna azione sui deal già chiusi). Badge dello stato O2C con colori coerenti con `Approvals.Web`.
- `CrmApiClient` tipizzato su `http://crm-mcp` (service discovery di Aspire) con `CorrelationIdDelegatingHandler` di ServiceDefaults; errori ProblemDetails mostrati come messaggio leggibile (404 → pagina "deal non trovato", 409 → "deal già chiuso", 503 → "chiuso ma evento non pubblicato").

**6.5 — `src/Dusiburg.AI.O2C.Erp.Web`** (porta 5106)
- Pagine: `/` (cruscotto: numero clienti, ordini per stato, prodotti sotto scorta), `/customers`, `/customers/{id}` (anagrafica, fido, badge "bloccato", ordini del cliente), `/stock` (con evidenza di `Available ≤ 0` e di `Reserved > OnHand`), `/orders`, `/orders/{orderNumber}` (righe, totale, stato, nota di backorder, chiave di idempotenza, link al deal).
- Nessun form di scrittura, nessun handler POST (verificato dai test).
- `ErpApiClient` tipizzato su `http://erp-api` con lo stesso handler di correlazione.

**6.6 — `Approvals.Web`**
- Filtro `?dealId=` sulla coda `/approvals` (per il link dal deal).
- Link al deal verso `Crm.Web` (`Links:CrmWeb`) al posto di `Approvals:CrmDevBaseUrl`; barra di navigazione verso le altre due UI.

**6.7 — AppHost**
- `crm-web` con `WithReference(crmMcp)`, `erp-web` con `WithReference(erpApi)`, entrambi con `WithHttpHealthCheck("/health")`; nessun riferimento a `sql` o `rabbitmq` (D58).
- Link fra le UI con i riferimenti agli endpoint (`Links__CrmWeb`, `Links__ErpWeb`, `Links__ApprovalsWeb`) su tutte e tre le app (G6.8); rimosso l'inoltro di `Approvals__CrmDevBaseUrl`.
- Porte fisse 5105 e 5106 nei `launchSettings.json`; aggiornata la riga "Porte fisse locali" in [plan.md](plan.md).

**6.8 — Documentazione**
- `docs/demo.md`: gli scenari D-1001…D-1008 si eseguono da `Crm.Web`, con i controlli in `Erp.Web` e `Approvals.Web`; nuovo scenario "Chiudi perso"; gli script PowerShell restano come alternativa (reset e `close-won` dev).
- `docs/architettura.md` (M26, M27): nuova fase in §11 e deploy come Fase 7, progetti e porte in §10, diagramma di §3.1, API di lettura e comando di chiusura.
- Riferimenti "Fase 6" intesi come deploy (commenti nel codice e `docs/panoramica-codice-fase*.md`) → "Fase 7".
- `docs/panoramica-codice-fase6.md`, come per le fasi precedenti.

### Test

**6.9 — `tests/Dusiburg.AI.O2C.Mcp.Tests`** (CRM reale su database di test)
- Viste: elenco deal ordinato, filtri per stage/stato O2C/azienda, dettaglio con righe e note, 404 su deal e azienda inesistenti.
- Chiusura: `Won` da `ContractSent` → stage `ClosedWon`, revisione invariata, **un** evento pubblicato con `dealId` e revisione; `Lost` → `ClosedLost`, **nessun** evento; da stage chiuso → 409 senza evento; esito non valido → 400; errore del publisher → 503 con stage `ClosedWon`; dev `close-won` su deal già vinto → ripubblica.
- `/api/*` raggiungibile senza API key; `/mcp` ancora protetto.

**6.10 — `tests/Dusiburg.AI.O2C.Erp.Api.Tests`**
- Viste clienti, magazzino (con `Available` negativo dopo una riserva in backorder e filtro `shortOnly`), ordini (filtri, dettaglio per numero con righe e `BackorderNote`, 404).

**6.11 — `tests/Dusiburg.AI.O2C.Web.Tests`** (nuovo)
- `Crm.Web`: le pagine si generano con i dati del client finto; i pulsanti di chiusura compaiono solo su `ContractSent`; il POST chiama il comando con l'esito giusto e mostra i messaggi di 409/503; il refresh c'è solo con stato non terminale; link verso ERP e approvazioni.
- `Erp.Web`: pagine generate; nessun endpoint risponde a POST; evidenza delle righe sotto scorta.
- Tipi di ingresso `CrmWebEntryPoint` / `ErpWebEntryPoint` per `WebApplicationFactory` (stesso motivo di `ApprovalsWebEntryPoint`).
- `ApprovalsWebTests` (in `Orchestrator.Tests`): filtro `?dealId=` e link verso `Crm.Web`.

## Criteri di accettazione

- [x] `Crm.Web` elenca deal e aziende con i dati del seed; il dettaglio di un deal mostra righe, revisione, stato O2C e storico delle note.
- [x] **Chiudi vinto** su D-1001 dalla UI → in 48 s la pagina (senza intervento) mostra `OrderCreated` e il numero d'ordine; il link apre l'ordine in `Erp.Web` con righe e totale.
- [x] **Chiudi vinto** su D-1002 → deal `ApprovalPending`, link alla richiesta in `Approvals.Web`; dopo l'approvazione il deal passa a `OrderCreated`.
- [x] **Chiudi perso** su un deal `ContractSent` → `ClosedLost`, nessun messaggio su `deal-closed-won`, nessun `WorkflowState` creato.
- [x] Un secondo comando di chiusura sullo stesso deal → messaggio "deal già chiuso", nessun evento.
- [x] `Erp.Web` mostra clienti (con il cliente di D-1005 bloccato), magazzino con la riserva dell'ordine appena creato (e `Reserved > OnHand` evidenziato dopo D-1003), ordini ricevuti con la nota di backorder; nessuna azione di scrittura (POST → 405).
- [~] I sei scenari rimanenti di `docs/demo.md` si eseguono dal browser con gli esiti della Fase 5. *Cinque su sei*: D-1003, D-1004, D-1005, D-1008 con i motivi attesi, D-1006 `Discarded`. **D-1007** è finito in `ApprovalPending` (`InsufficientStock`) invece che `Failed`: vedi "Scostamenti". Il rifiuto e la scadenza non sono stati ripetuti (nessun codice toccato in quei percorsi).
- [~] Nel dashboard la traccia di un flusso parte dalla richiesta HTTP di `Crm.Web` (span `crm.deal.close`) con lo stesso `correlation.id` del workflow. *Verificato in parte*: lo span e i suoi attributi sono coperti dai test; dal vivo il `x-correlation-id` inviato a `Crm.Web` si ritrova identico in `orch.WorkflowState.CorrelationId` per tutti i deal. Ispezione visiva della traccia nel dashboard non fatta.
- [x] Le due UI non hanno connection string `sql`/`rabbitmq` (AppHost: solo `WithReference(crmMcp)` / `WithReference(erpApi)`; i progetti non referenziano EF né il client RabbitMQ).
- [x] DoD comune soddisfatta.

## Rischi

| Rischio | Mitigazione |
|---------|-------------|
| Comando di chiusura esposto senza autenticazione fuori da Development | Accettato in locale (G6.5); in Fase 7 login Entra e API interne all'ambiente (G7.4, G7.7) |
| Stage salvato ma evento non pubblicato: deal vinto senza workflow | 503 esplicito in UI e ripubblicazione dall'endpoint dev (G6.3, G6.4); outbox fuori scope, come in G5.3 |
| Doppio clic su "Chiudi vinto" | Transizione solo da `ContractSent` (409 al secondo) e, comunque, consumer idempotente su `(DealId, DealRevision)` |
| Le viste diventano un secondo contratto da mantenere | DTO separati dai contratti dei tool (G6.1, G6.2): i tool di §6 non cambiano |
| Refresh automatico che martella il servizio | 5 s solo sulla pagina di un deal con stato non terminale |
| Rinumerazione Fase 6 → 7 lascia riferimenti vecchi | Step 6.8 con grep su `Fase 6` in codice e documenti |

## Definition of Done

DoD comune di [plan.md](plan.md#definition-of-done-comune-a-ogni-fase).

## Esito

**Completata il 2026-09-17** — branch `develop`. Gate F6 chiuso con tutte le proposte accettate; prova dal vivo sul modello locale `qwen3.5:9b` (come D53).

### Verifiche

| Criterio | Evidenza |
|----------|----------|
| Build e test | `dotnet build Dusiburg.AI.O2C.slnx` → 0 avvisi, 0 errori; `dotnet test` → **279/279** (200 in Fase 5): +26 in `Mcp.Tests` (`CrmApiEndpointTests`), +11 in `Erp.Api.Tests`, +2 su `Approvals.Web`, +40 nel nuovo `Web.Tests` |
| Avvio | AppHost con le due risorse nuove, tutte e cinque le UI/servizi web sani; link fra le UI valorizzati dagli endpoint delle risorse (`http://localhost:5105`, `5106`, `5104`) |
| D-1001 | Form **Chiudi vinto** → messaggio di avvio, pagina in refresh; `OrderCreated` e `SO-2026-000001` in 48 s, refresh fermato; ordine `Confirmed` in `Erp.Web` con link al deal; `IND-BRG-001` riservato da 20 a 60 |
| D-1002, D-1003 | `ApprovalPending`, pagina ancora in refresh, link a `/approvals?all=true&dealId=…` con la sola richiesta del deal; approvazione dal form di `Approvals.Web` (il cui link riporta a `Crm.Web`) → `OrderCreated` in 12 s. D-1003 in `Backorder` con nota `IND-MOT-003: 2 PZ da ordinare` in `Erp.Web` e sul deal, `IND-MOT-003` evidenziato come riservato oltre giacenza |
| D-1004, D-1005, D-1008 | `ApprovalPending` con `NewCustomer`, `BlockedCustomer`, `OverThreshold`+`NewCustomer`; i due clienti nuovi compaiono in `Erp.Web` |
| D-1006 | `Discarded` |
| D-1007 | ⚠️ `ApprovalPending` con `InsufficientStock` invece di `Failed` (vedi sotto); nessun ordine |
| Chiudi perso | D-1007 (prima prova) → `ClosedLost`, "nessun ordine verrà generato", nessuna riga in `orch.WorkflowState`, nessuna scrittura di O2C, form di chiusura sparito |
| Doppia chiusura | Form inviato su un deal chiuso → avviso "Il deal D-1007 è già chiuso: nessuna modifica." |
| Sola lettura | `POST http://localhost:5106/stock` → 405 |
| Correlazione | `x-correlation-id: fase6-live-<deal>-Won` inviato a `Crm.Web` ritrovato identico in `orch.WorkflowState.CorrelationId` per tutti i deal |

### Cosa è stato fatto
- `Shared`: viste in `Contracts/Views/{Crm,Erp}`, `CloseDealRequest`, `DealCloseOutcome`; `DealStage` spostato da `Crm.Data`; attributo `deal.close.outcome`.
- `ServiceDefaults`: `Portal/PortalLinks`; `ToolProblems.UpstreamUnavailable` e `ConfigureExceptionHandler`.
- `Crm.Mcp`: `Views/CrmViewQueries`, `Views/CrmApiEndpoints` (letture + comando), `Deals/DealClosingService` (UPDATE condizionale, span `crm.deal.close`), `IDealEventPublisher`; endpoint dev ridotti a `close-won` (chiusura o ripubblicazione) e `reset`; `Crm.Mcp.dev.http` aggiornato.
- `Erp.Api`: `Views/ErpViewQueries`, `Views/ErpViewEndpoints`.
- `Crm.Web`, `Erp.Web`: nuove app Razor Pages (sezioni 6.4 e 6.5).
- `Approvals.Web`, `Orchestration.Data`: filtro per deal, link verso `Crm.Web`, barra di navigazione.
- AppHost: `crm-web`, `erp-web`, `Links__*`; tolto `Approvals__CrmDevBaseUrl`.
- Documenti: `docs/demo.md` (demo dal browser, scenario "Chiudi perso"), `README.md`, `docs/architettura.md` (§3.1, §3.2, §6.3, §10, §11, M26, M27, deploy rinumerato in Fase 7), `docs/panoramica-codice-fase6.md`, riferimenti "Fase 6" → "Fase 7" in codice e panoramiche precedenti.

### Scostamenti e note
- **D-1007 non arriva a `Failed` se il modello non segnala lo SKU inesistente** (difetto della Fase 5, emerso dal vivo). `FulfillmentAgent` non ha chiamato `report_failed`, l'ordine è stato proposto, e `ApprovalGate` ha trattato il `NOT_FOUND` della propria verifica su `IND-SEN-999` come "riga non disponibile" (D54): risultato, un'approvazione `InsufficientStock` invece dello stop senza approvazione di D20. Il guardrail ha tenuto (nessun ordine), ma l'esito è sbagliato; se approvato, `create_order` fallirebbe con `NOT_FOUND`. Correzione proposta, **non applicata** perché fuori dal perimetro della fase: in `ApprovalGate`, un `NOT_FOUND` della verifica dell'host chiude il workflow come `Failed` senza approvazione. In attesa della decisione dell'utente.
- **Retry della chiusura**: le opzioni del resilience handler registrato da `ConfigureHttpClientDefaults` non hanno il nome del client; la configurazione per nome non aveva effetto (il test ha visto 4 POST). `Crm.Web` usa `ConfigureAll` (D60).
- **JSON illeggibile → 500 in Development**: le Minimal API lanciano `BadHttpRequestException`; ora `ConfigureExceptionHandler` risponde 400, anche su `POST /api/orders` di `Erp.Api` (D60).
- **Sola lettura di `Erp.Web`**: una POST verso una Razor Page senza handler viene eseguita come la GET; un middleware rifiuta i metodi diversi da GET/HEAD (405).
- **Lettere accentate**: le due UI nuove usano `UnicodeRanges.All` per l'encoder HTML.
- **Piccole differenze dagli step**: le API del CRM stanno in `Views/CrmApiEndpoints` (non `Api/`), il metodo di ripubblicazione si chiama `CloseWonOrRepublishAsync`, e gli enum come stringa si verificano nei test delle API (un intero è rifiutato con 400) invece che in `ContractSerializationTests`.
- **Non ripetuti dal vivo**: rifiuto, scadenza e riavvio in attesa (percorsi non toccati); ispezione visiva della traccia nel dashboard.
