# Fase 2 — Server MCP (Erp.Mcp, Crm.Mcp)

> Indice: [plan.md](plan.md) · Precedente: [Fase 1](fase-1-sistemi-base.md) · Successiva: [Fase 3](fase-3-agente-singolo.md)

## Obiettivo

Esporre i due sistemi come tool MCP, esattamente con i contratti di §6 (più `revision` su `get_deal`, M3), su trasporto HTTP stateless, con errori strutturati, autenticazione a API key, correlazione e uno span per tool.

**Accettazione di §11**: entrambi i server rispondono a un client MCP di test con l'elenco corretto dei tool; ogni tool restituisce dati coerenti ed errori strutturati.

## Prerequisiti

Fase 1 completata (`Erp.Api` e CRM mock funzionanti).

## Gate di fase

| # | Domanda | Proposta |
|---|---------|----------|
| G2.1 | Come si rappresenta un errore di tool? | Risultato con `isError = true` e contenuto JSON `{ error: { code, message } }` (verifica nello spike S2): sia il protocollo sia l'agente vedono l'errore |
| G2.2 | Nomi dei tool | Sul server i nomi di §6 senza prefisso (`create_order`, …): non ci sono collisioni fra i due server e i nomi di funzione OpenAI non ammettono il punto (R5). Il nome qualificato `erp.create_order` si usa solo lato orchestratore per policy e telemetria |
| G2.3 | API key | Una chiave distinta per server (`ERP_MCP_API_KEY`, `CRM_MCP_API_KEY`) come parametri segreti dell'AppHost |
| G2.4 | MCP Inspector per la verifica manuale | Opzionale (richiede Node); l'accettazione si basa sui test automatici |
| G2.5 | `create_order` marcato "sensibile" | Annotazione `destructive` + metadata `o2c.sensitive = true` sul tool; l'enforcement resta nell'orchestratore (Fase 5) |

## Spike S2 — verifica API MCP C# SDK v2.x (30–60 min)

Verificare sulla versione NuGet corrente di `ModelContextProtocol` / `ModelContextProtocol.AspNetCore`:
- registrazione server: `AddMcpServer().WithHttpTransport(o => o.Stateless = true).WithTools<T>()` e `MapMcp("/mcp")`;
- attributi dei tool (`[McpServerToolType]`, `[McpServerTool]`, `[Description]`) e annotazioni (read-only, destructive, idempotent);
- output strutturato e output schema; come restituire `isError` con contenuto JSON (G2.1);
- metadata personalizzati sul tool (G2.5);
- filtri sulle chiamate ai tool (per span, gestione eccezioni, correlazione) e accesso agli header HTTP della richiesta;
- client: creazione di un client HTTP verso `/mcp` con `HttpClient` personalizzato (per i test con `WebApplicationFactory`) e header aggiuntivi.

Esito dello spike → breve nota nella sezione "Esito" e, se servono, aggiornamenti a G2.1/G2.5.

## Step operativi

**2.1 — Infrastruttura MCP comune** (in `ServiceDefaults`, cartella `Mcp/`)
- Middleware di autenticazione su `/mcp`: header `X-Api-Key` confrontato in tempo costante con la chiave da configurazione → 401 se assente o errata.
- Filtro sulle chiamate ai tool: apre lo span `mcp.tool {tool.name}` sull'`ActivitySource` del server con `tool.name`, `correlation.id`, `tool.outcome` (`ok`/`error:<code>`); intercetta eccezioni non gestite → `ToolError(INTERNAL)` (§6: mai eccezioni non gestite); log strutturato di ogni chiamata.
- Correlazione: `x-correlation-id` letto dall'header HTTP (già gestito dal middleware di Fase 0).

**2.2 — `src/Dusiburg.AI.O2C.Erp.Mcp`**
- `ErpApiClient` (HttpClient tipizzato verso `https+http://erp-api` via service discovery, con `CorrelationIdDelegatingHandler`): mappa ProblemDetails e codici HTTP su `ToolError` (404 → `NOT_FOUND`, 400 → `VALIDATION_ERROR`, 409 → `CONFLICT`, 5xx/timeout → `UPSTREAM_UNAVAILABLE`).
- `ErpTools` con i 5 tool di §6.1, descrizioni pensate per il modello (vincoli espliciti, es. "fornire almeno uno fra vatNumber ed email"):
  - `get_customer` → cliente oppure `null` se non trovato (come da contratto, non errore); `VALIDATION_ERROR` se mancano entrambi i parametri;
  - `create_customer` → `{ customerId }`;
  - `check_stock` → `{ sku, available, onHand, leadTimeDays }`; `NOT_FOUND` per SKU inesistente;
  - `create_order` → `{ orderId, orderNumber, total, status }`; `idempotencyKey` ed `externalRef` obbligatori; annotazioni secondo G2.5;
  - `get_order` → ordine completo con righe.
- AppHost: `Erp.Mcp` referenzia `Erp.Api`; parametro `ERP_MCP_API_KEY`.

**2.3 — `src/Dusiburg.AI.O2C.Crm.Mcp`**
- `CrmTools` con i 3 tool di §6.2 sopra `ICrmClient`:
  - `get_deal` → contratto §6.2 **+ `revision`** (M3);
  - `get_company`;
  - `update_deal` → `{ ok }`; `status` validato contro `DealStatus` (D23) → `VALIDATION_ERROR` se fuori enum.
- Il server MCP e gli endpoint dev della Fase 1 convivono nello stesso host; gli endpoint dev restano solo in `Development`.
- AppHost: parametro `CRM_MCP_API_KEY`.

**2.4 — Configurazione per i client**
- Documentare nel README: URL `http://localhost:5102/mcp` (ERP) e `http://localhost:5103/mcp` (CRM), header `X-Api-Key`, `x-correlation-id`.

### Test — `tests/Dusiburg.AI.O2C.Mcp.Tests`

**2.5 — Harness**
- `WebApplicationFactory` per `Erp.Mcp` (con `Erp.Api` sostituito da un `HttpMessageHandler` stub) e per `Crm.Mcp` (DB di test su `localdev`); client MCP costruito sull'`HttpClient` della factory.

**2.6 — Casi**
- `tools/list`: insieme **esatto** dei nomi (ERP: 5, CRM: 3) e parametri obbligatori negli schemi di input.
- Ogni tool: caso felice con dati coerenti.
- Errori: `VALIDATION_ERROR` (input mancanti/non validi), `NOT_FOUND`, `get_customer` → `null`, upstream 500 → `UPSTREAM_UNAVAILABLE`, eccezione interna → `INTERNAL` (mai eccezione non gestita).
- Sicurezza: senza `X-Api-Key` o con chiave errata → 401.
- Correlazione: la chiamata a `Erp.Api` porta lo stesso `x-correlation-id` ricevuto dal server MCP.

## Criteri di accettazione

- [ ] Un client MCP di test ottiene da ciascun server l'elenco corretto dei tool (test automatico).
- [ ] Ogni tool restituisce dati coerenti con il DB/seed (test automatici).
- [ ] Ogni tool restituisce errori strutturati `{ error: { code, message } }` e nessuna eccezione non gestita.
- [ ] Nel dashboard ogni chiamata a tool genera uno span con `tool.name`, `correlation.id`, esito e durata.
- [ ] DoD comune soddisfatta.

## Rischi

| Rischio | Mitigazione |
|---------|-------------|
| API dell'SDK diverse da quanto atteso | Spike S2 prima del codice |
| Output strutturato non supportato come previsto | Ripiego: contenuto testuale JSON serializzato con gli stessi DTO |

## Definition of Done

DoD comune di [plan.md](plan.md#definition-of-done-comune-a-ogni-fase).

## Esito

_Da compilare a fine fase (incluso l'esito dello spike S2)._
