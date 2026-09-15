# Fase 2 — Server MCP (Erp.Mcp, Crm.Mcp)

> Indice: [plan.md](plan.md) · Precedente: [Fase 1](fase-1-sistemi-base.md) · Successiva: [Fase 3](fase-3-agente-singolo.md)

## Obiettivo

Esporre i due sistemi come tool MCP con i contratti di §6 (più `revision` su `get_deal`, M3, e `{ customer }` su `get_customer`, M20), su trasporto HTTP stateless, con errori strutturati (M22), autenticazione a API key, correlazione e uno span per tool.

**Accettazione di §11**: entrambi i server rispondono a un client MCP di test con l'elenco corretto dei tool; ogni tool restituisce dati coerenti ed errori strutturati.

## Prerequisiti

Fase 1 completata (`Erp.Api` e CRM mock funzionanti).

## Gate di fase

> Chiuso il 2026-09-15, dopo lo spike S2 (decisioni D33–D37 in [plan.md](plan.md)).

| # | Domanda | Decisione |
|---|---------|-----------|
| G2.1 | Come si rappresenta un errore di tool? | **Deciso dall'utente (D33)**: risultato con `isError = true` e l'envelope `{ error: { code, message } }` **solo come testo JSON**, senza `structuredContent` (riservato al caso felice e conforme all'`outputSchema`, M22). Le eccezioni dei tool, compresi gli errori di binding degli argomenti, le traduce il filtro sulle chiamate |
| G2.2 | Nomi dei tool | **Proposta accettata (D37)**: sul server i nomi di §6 senza prefisso (`create_order`, …), costanti `ErpToolNames`/`CrmToolNames` in `Shared`; il nome qualificato `erp.create_order` si usa solo lato orchestratore per policy e telemetria (R5) |
| G2.3 | API key | **Proposta accettata (D37)**: una chiave distinta per server (`ERP_MCP_API_KEY`, `CRM_MCP_API_KEY`) come parametri segreti dell'AppHost; senza chiave il servizio non parte (validazione all'avvio) |
| G2.4 | MCP Inspector per la verifica manuale | **Proposta accettata (D37)**: opzionale (richiede Node); l'accettazione si basa sui test automatici |
| G2.5 | `create_order` marcato "sensibile" | **Proposta accettata (D37)**: annotazione `destructive` + `_meta` `o2c.sensitive = true` (costante `ToolMetadata.Sensitive` in `Shared`); l'enforcement resta nell'orchestratore (Fase 5) |
| G2.6 | Come restituisce `get_customer` il cliente non trovato? *(emersa dallo spike: un tool che restituisce `null` produce `content: []`)* | **Deciso dall'utente (D34)**: sempre un oggetto `{ customer }`, con `customer: null` se il cliente non esiste; non è un errore (M20) |
| G2.7 | Con cosa gira `Erp.Mcp` nei test? | **Deciso dall'utente (D36)**: `Erp.Api` reale in-process su database `O2C_Test_<guid>` per i casi felici e di dominio; `HttpMessageHandler` stub solo per 500, timeout ed eccezioni |
| G2.8 | Dove sta l'infrastruttura MCP comune? | **Deciso dall'utente (D35)**: nuovo progetto `src/Dusiburg.AI.O2C.Mcp.Hosting` referenziato solo dai due server (M21), invece di `ServiceDefaults/Mcp` |

## Spike S2 — verifica API MCP C# SDK v2.x ✅ (2026-09-15)

Eseguito su un progetto usa e getta fuori dal repo, con server Kestrel e client MCP nello stesso processo.

| Verifica | Esito |
|----------|-------|
| Versione | `ModelContextProtocol.AspNetCore` **2.2.0** (ultima su NuGet); protocollo negoziato `2026-07-28` |
| Registrazione server | `AddMcpServer().WithHttpTransport(o => o.Stateless = true).WithTools<T>()` + `MapMcp("/mcp")` come previsto. Dalla revisione `2026-07-28` lo stateless è il default (`SessionMode`); niente handshake `initialize` |
| Attributi e annotazioni | `[McpServerToolType]`, `[McpServerTool(Name, Title, ReadOnly, Destructive, Idempotent, OpenWorld)]`, `[Description]` → `annotations` in `tools/list` |
| Output strutturato | `UseStructuredContent = true`: `outputSchema` generato dal tipo di ritorno (enum per nome, parametri obbligatori corretti anche per `lines[{ sku, quantity, unitPrice }]`); il risultato ha `structuredContent` e lo stesso JSON come testo |
| Metadata personalizzati | `[McpMeta("o2c.sensitive", true)]` → `_meta` del tool in `tools/list` (non nei risultati) |
| `isError` con contenuto JSON | Un tool che restituisce `CallToolResult { IsError = true, … }` arriva intatto al client |
| Eccezioni | ⚠️ Ogni eccezione del tool diventa `isError` con testo generico `An error occurred invoking '<tool>'.` (con `McpException` si aggiunge il messaggio); vale anche per gli errori di binding (parametro obbligatorio mancante → `ArgumentException`, enum fuori valori → `JsonException`). Tool inesistente → errore JSON-RPC (`McpProtocolException`) |
| `null` come risultato | ⚠️ `content: []` senza valore: da qui G2.6 |
| Filtri | `WithRequestFilters(f => f.AddCallToolFilter(next => …))`: il filtro avvolge il tool, vede nome e argomenti e **intercetta le eccezioni prima della conversione dell'SDK**; `context.Services` dà accesso a `IHttpContextAccessor` (header `x-correlation-id`, `X-Api-Key`) |
| Client | `HttpClientTransport(new HttpClientTransportOptions { Endpoint, AdditionalHeaders }, httpClient, …)` + `McpClient.CreateAsync`: funziona con un `HttpClient` fornito dal chiamante (quindi anche con `WebApplicationFactory`) |
| ⚠️ Opzioni JSON *(emerso dai test, dopo lo spike)* | Le opzioni di default dell'SDK (`McpJsonUtilities.DefaultOptions`) omettono le proprietà `null` (`{ customer: null }` → `{}`) e il loro resolver accetta gli enum anche come interi, scavalcando `StrictStringEnumConverter`. I tool usano opzioni proprie (D38) |

## Step operativi

**2.1 — Infrastruttura MCP comune** — progetto `src/Dusiburg.AI.O2C.Mcp.Hosting` (D35)
- `AddO2CMcpServer(activitySourceName, apiKeyConfigurationKey)`: server MCP con trasporto HTTP stateless e filtro sulle chiamate; opzioni della API key validate all'avvio.
- `MapO2CMcp()`: middleware di autenticazione solo su `/mcp` (header `X-Api-Key` confrontato in tempo costante con la chiave da configurazione) → 401 ProblemDetails con `code = UNAUTHORIZED` se assente o errata; poi `MapMcp("/mcp")`.
- Filtro sulle chiamate ai tool: span `mcp.tool {tool.name}` sull'`ActivitySource` del server con `tool.name`, `correlation.id`, `tool.outcome` (`ok`/`error:<code>`); log strutturato di ogni chiamata. Traduzione degli errori (§6, D33): `ToolException(code)` → quel codice; `ArgumentException`/`JsonException` (binding degli argomenti, input rifiutato dal codice) → `VALIDATION_ERROR`; ogni altra eccezione → `INTERNAL` con messaggio generico e log di errore. Gli errori di protocollo (tool inesistente) restano errori JSON-RPC.
- Correlazione: `x-correlation-id` letto dall'header HTTP dal middleware di Fase 0, disponibile al filtro tramite `ICorrelationContext`.
- In `Shared`: `ErpToolNames`, `CrmToolNames`, `ToolMetadata.Sensitive`, `GetCustomerResponse` (riusabili dall'orchestratore).

**2.2 — `src/Dusiburg.AI.O2C.Erp.Mcp`**
- `ErpApiClient` (HttpClient tipizzato verso `https+http://erp-api` via service discovery, indirizzo sovrascrivibile con `ErpApi:BaseAddress`; correlation id dal `CorrelationIdDelegatingHandler`): mappa i codici HTTP su `ToolException` (404 → `NOT_FOUND`, 400 → `VALIDATION_ERROR`, 409 → `CONFLICT` con il `detail` del ProblemDetails; 5xx, timeout e connessione fallita → `UPSTREAM_UNAVAILABLE`; risposta illeggibile → `INTERNAL`).
- `ErpTools` con i 5 tool di §6.1, descrizioni pensate per il modello (vincoli espliciti):
  - `get_customer` → `{ customer }`, `customer: null` se non trovato (D34, non errore); `VALIDATION_ERROR` se mancano entrambi i parametri;
  - `create_customer` → `{ customerId }`; `CONFLICT` se la partita IVA esiste;
  - `check_stock` → `{ sku, available, onHand, leadTimeDays }`; `NOT_FOUND` per SKU inesistente;
  - `create_order` → `{ orderId, orderNumber, total, status }`; `idempotencyKey` ed `externalRef` obbligatori; annotazioni secondo G2.5;
  - `get_order` → ordine completo con righe.
- AppHost: `Erp.Mcp` referenzia `Erp.Api`; parametro `ERP_MCP_API_KEY` (già presenti dalla Fase 0).

**2.3 — `src/Dusiburg.AI.O2C.Crm.Mcp`**
- `CrmTools` con i 3 tool di §6.2 sopra `ICrmClient`:
  - `get_deal` → contratto §6.2 **+ `revision`** (M3); `NOT_FOUND` se non esiste;
  - `get_company`; `NOT_FOUND` se non esiste;
  - `update_deal` → `{ ok }`; `status` validato contro `DealStatus` (D23) → `VALIDATION_ERROR` se fuori enum; `erpOrderNumber` e `note` facoltativi.
- Il server MCP e gli endpoint dev della Fase 1 convivono nello stesso host; gli endpoint dev restano solo in `Development` e non richiedono la API key.
- AppHost: parametro `CRM_MCP_API_KEY` (già presente).

**2.4 — Configurazione per i client**
- Documentare nel README: URL `http://localhost:5102/mcp` (ERP) e `http://localhost:5103/mcp` (CRM), header `X-Api-Key`, `x-correlation-id`.

### Test — `tests/Dusiburg.AI.O2C.Mcp.Tests`

**2.5 — Harness** (D36)
- `Erp.Mcp`: `WebApplicationFactory` di `Erp.Api` su database `O2C_Test_<guid>` e `WebApplicationFactory` di `Erp.Mcp` il cui `ErpApiClient` usa come handler primario un `UpstreamHandler`: inoltra a `Erp.Api` in-process oppure restituisce una risposta simulata (500, attesa oltre il timeout, eccezione) e registra il `x-correlation-id` ricevuto. Resilienza con attese brevi nei test.
- `Crm.Mcp`: `WebApplicationFactory` su database `O2C_Test_<guid>`.
- Client MCP costruito sull'`HttpClient` della factory.

**2.6 — Casi**
- `tools/list`: insieme **esatto** dei nomi (ERP: 5, CRM: 3), parametri obbligatori negli schemi di input, annotazioni e `_meta` di `create_order`, enum di `update_deal.status`.
- Ogni tool: caso felice con dati coerenti con il seed.
- Errori: `VALIDATION_ERROR` (input mancanti/non validi, binding), `NOT_FOUND`, `CONFLICT`, `get_customer` → `{ customer: null }`, upstream 500 e timeout → `UPSTREAM_UNAVAILABLE`, eccezione interna → `INTERNAL` senza dettagli (mai eccezione non gestita).
- Sicurezza: senza `X-Api-Key` o con chiave errata → 401.
- Correlazione: la chiamata a `Erp.Api` porta lo stesso `x-correlation-id` ricevuto dal server MCP.
- Telemetria: ogni chiamata produce lo span `mcp.tool {tool.name}` con `tool.name`, `correlation.id` e `tool.outcome`.

## Criteri di accettazione

- [x] Un client MCP di test ottiene da ciascun server l'elenco corretto dei tool (test automatico).
- [x] Ogni tool restituisce dati coerenti con il DB/seed (test automatici).
- [x] Ogni tool restituisce errori strutturati `{ error: { code, message } }` e nessuna eccezione non gestita.
- [x] Nel dashboard ogni chiamata a tool genera uno span con `tool.name`, `correlation.id`, esito e durata.
- [x] DoD comune soddisfatta (commit lasciato all'utente, come in Fase 1).

## Rischi

| Rischio | Mitigazione |
|---------|-------------|
| API dell'SDK diverse da quanto atteso | Spike S2 eseguito: nessuna divergenza bloccante; eccezioni e `null` gestiti con D33/D34 |
| Retry della resilienza su `POST` | `create_order` è idempotente per chiave; per `create_customer` un retry dopo una risposta persa dà `CONFLICT` e l'agente rilegge con `get_customer` |
| Messaggi delle eccezioni di binding poco leggibili per il modello | Accettato per il POC: sono comunque `VALIDATION_ERROR` con il nome del parametro |

## Definition of Done

DoD comune di [plan.md](plan.md#definition-of-done-comune-a-ogni-fase).

## Esito

**Completata il 2026-09-15** — branch `develop` di `C:\Dev\NetCode\AI.POC-OrderToCash`, modifiche non committate (il commit lo fa l'utente).

### Verifiche
| Criterio | Evidenza |
|----------|----------|
| Build | `dotnet build Dusiburg.AI.O2C.slnx` → 0 avvisi, 0 errori |
| Test | `dotnet test --solution Dusiburg.AI.O2C.slnx` → 129/129 (86 in Fase 1): +27 `ErpMcpToolTests`, +15 `CrmMcpToolTests`, +1 `ContractSerializationTests`; database `O2C_Test_<guid>` cancellati a fine classe |
| `tools/list` | Test: insieme esatto dei nomi (5 + 3), parametri obbligatori, annotazioni, `_meta` `o2c.sensitive` solo su `create_order`, enum di `update_deal.status`, `outputSchema` su tutti i tool. Con l'AppHost: client MCP reale su `http://localhost:5102/mcp` e `:5103/mcp` con `X-Api-Key`, protocollo `2026-07-28`, stessi 5 + 3 tool |
| Dati coerenti | Test sui dati demo. Con l'AppHost, flusso di D-1001: `get_deal` (revision 1) → `get_company` → `get_customer` → 4 × `check_stock` → `create_order` (`SO-2026-000001`, 1.485,00, `Confirmed`) → `get_order` → `update_deal` (`OrderCreated`) |
| Errori strutturati | Test: `VALIDATION_ERROR` (binding degli argomenti e 400 di Erp.Api), `NOT_FOUND`, `CONFLICT`, `UPSTREAM_UNAVAILABLE` (500 e timeout), `INTERNAL` senza dettagli. Con l'AppHost: `get_deal D-9999` → `NOT_FOUND`, `update_deal status: 1` → `VALIDATION_ERROR`, cliente inesistente → `{"customer":null}` |
| Span | API di telemetria del dashboard: 13 span `mcp.tool {tool}` (8 `erp-mcp`, 5 `crm-mcp`) con `tool.name`, `correlation.id = fase2-accettazione-001`, `tool.outcome` (`ok`, `error:NOT_FOUND`, `error:VALIDATION_ERROR`) e durata; nella stessa traccia gli span HTTP di `erp-api` con lo stesso `correlation.id`. Test `CallTool_EmitsSpanWithToolNameCorrelationIdAndOutcome` |
| Sicurezza | Test: 401 con `code = UNAUTHORIZED` senza `X-Api-Key` o con chiave errata, su entrambi i server |
| Correlazione | Test `CheckStock_ForwardsCorrelationIdToErpApi` (header ricevuto da `Erp.Api` = quello inviato al server MCP) e span di `erp-api` nel dashboard |
| Segreti | Nei file modificati solo i placeholder del README e le chiavi fittizie dei test |

### Cosa è stato fatto
- `src/Dusiburg.AI.O2C.Mcp.Hosting` (D35): `AddO2CMcpServer` / `MapO2CMcp`, `ApiKeyMiddleware` (hash SHA-256 confrontati in tempo costante), `ToolCallFilter` (span, log, traduzione degli errori), `ToolException`, `ToolResults`, `ToolSerializerOptions` (D38).
- `Erp.Mcp`: `ErpApiClient` (service discovery, mappatura HTTP → codici di §6) e `ErpTools` con i 5 tool.
- `Crm.Mcp`: `CrmTools` con i 3 tool sopra `ICrmClient`, accanto agli endpoint dev (che non richiedono la API key).
- `Shared`: `GetCustomerResponse` (M20), `ErpToolNames`, `CrmToolNames`, `ToolMetadata.Sensitive`; `ToolProblems.Unauthorized` in ServiceDefaults; `ModelContextProtocol.AspNetCore` 2.2.0 nel catalogo centrale dei pacchetti.
- Test: `Erp.Mcp` sopra `Erp.Api` reale con `UpstreamHandler` per i guasti (D36), `Crm.Mcp` su database di test, helper `McpTestClient`.
- Specifica (M20–M22), README (sezione "Server MCP"), piano (D33–D38).

### Scostamenti e note
- **Opzioni JSON dei tool (D38)**, emerse dai primi test e non dallo spike: con le opzioni dell'SDK `{ customer: null }` usciva come `{}` e `update_deal` accettava `status: 1`. I tool usano `ToolSerializerOptions` (`JsonSerializerDefaults.Web`, resolver a reflection, null mantenuti). Come nei contratti HTTP, in lettura i nomi degli enum non distinguono le maiuscole.
- I messaggi degli errori di binding sono quelli di System.Text.Json, in inglese (es. `The JSON value could not be converted to …DealStatus`): restano comunque `VALIDATION_ERROR`.
- Gli span `mcp.tool` coprono le chiamate ai tool; `tools/list` ha solo lo span HTTP `POST /mcp`. `agent.name` non compare sugli span del server, che non conosce l'agente: arriva con gli span dell'orchestratore in Fase 3.
- AppHost invariato: riferimenti e parametri `ERP_MCP_API_KEY` / `CRM_MCP_API_KEY` erano già presenti dalla Fase 0. Con `--launch-profile http` l'Aspire CLI ha comunque aperto il dashboard in HTTPS (porta 17279).
- Retry della resilienza su `POST`: vedi Rischi (`create_order` idempotente; `create_customer` → `CONFLICT` al retry).
- Dopo la verifica: `POST /dev/reset` su `Erp.Api` e `Crm.Mcp`, il database `O2C` di sviluppo contiene di nuovo solo i dati demo.
