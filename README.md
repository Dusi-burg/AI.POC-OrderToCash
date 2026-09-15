# AI.POC-OrderToCash

POC **Order-to-Cash agentico**: quando un deal passa a *Closed Won* nel CRM, tre agenti specializzati (Microsoft Agent Framework) leggono il deal, verificano la disponibilità e creano l'ordine nell'ERP tramite due server **MCP**, con **approvazione umana** sopra determinate soglie di rischio.

- Specifica (fonte di verità): [docs/architettura.md](docs/architettura.md)
- Piano per fasi: [docs/plan/plan.md](docs/plan/plan.md)

> **Stato**: Fase 2 — server MCP. `Erp.Mcp` e `Crm.Mcp` espongono i tool di §6 su `/mcp` (HTTP stateless, API key, errori strutturati, uno span per tool) sopra l'ERP mock (`Erp.Api`) e il CRM mock della Fase 1, con i dati demo di [docs/demo.md](docs/demo.md); nessun agente ancora. Correlazione, telemetria e gestione dei segreti sono cablate dalla Fase 0.

## Struttura

| Percorso | Ruolo |
|----------|-------|
| `src/Dusiburg.AI.O2C.AppHost` | .NET Aspire: composizione locale di tutti i servizi |
| `src/Dusiburg.AI.O2C.ServiceDefaults` | OpenTelemetry, health check, service discovery, resilienza, correlation id |
| `src/Dusiburg.AI.O2C.Erp.Api` | Minimal API: l'ERP mock |
| `src/Dusiburg.AI.O2C.Erp.Data` | Modello EF Core dell'ERP (schema `erp`) |
| `src/Dusiburg.AI.O2C.Erp.Mcp` | Server MCP sopra `Erp.Api` |
| `src/Dusiburg.AI.O2C.Crm.Mcp` | Server MCP con il CRM mock |
| `src/Dusiburg.AI.O2C.Crm.Data` | Modello EF Core del CRM mock (schema `crm`) |
| `src/Dusiburg.AI.O2C.Mcp.Hosting` | Infrastruttura comune dei server MCP: API key, filtro sulle chiamate ai tool, errori strutturati |
| `src/Dusiburg.AI.O2C.Orchestrator` | Worker: agenti, handoff, policy di approvazione |
| `src/Dusiburg.AI.O2C.Approvals.Web` | UI delle approvazioni |
| `src/Dusiburg.AI.O2C.Shared` | Contratti (§6), helper di idempotenza e correlazione, codici errore, nomi di telemetria |
| `tools/Dusiburg.AI.O2C.DbInit` | Crea da zero il database `O2C` dal modello EF (niente migration) |
| `tests/*` | NUnit 4 con `Assert.That` (runner NUnit su Microsoft.Testing.Platform) |

## Prerequisiti

- **.NET SDK 10.0.4xx** (vedi `global.json`).
- **SQL Server LocalDB** con l'istanza `localdev` e il database `O2C` creato dal tool `DbInit`:
  ```powershell
  sqllocaldb create localdev -s
  dotnet run --project tools/Dusiburg.AI.O2C.DbInit   # cancella e ricrea O2C: schemi erp e crm, lookup degli enum
  ```
  Non ci sono migration: a ogni modifica del modello si rilancia il tool. Senza argomenti usa `ConnectionStrings__sql` o `(localdb)\localdev`; su un server che non è LocalDB serve `--allow-non-local`.
- **Docker in WSL** (distro `Ubuntu-26.04`) con un container `rabbitmq` (`rabbitmq:4.3.5-management`, porte 5672/15672) e, sul broker, il vhost `o2c` con l'utente `o2c`. Il container **non va avviato a mano**: lo fa l'AppHost (vedi sotto). Se distro o nome del container sono diversi, impostare `RabbitMq:WslDistro` e `RabbitMq:Container` negli user-secrets dell'AppHost.

  Creazione di vhost e utente (una volta sola; eseguire i comandi nella stessa sessione WSL, a broker avviato):
  ```bash
  docker exec rabbitmq rabbitmqctl add_vhost o2c
  docker exec rabbitmq rabbitmqctl add_user o2c '<password>'
  docker exec rabbitmq rabbitmqctl set_permissions -p o2c o2c '.*' '.*' '.*'
  ```

### Perché RabbitMQ lo avvia l'AppHost

Senza sessioni aperte, WSL spegne la propria VM dopo pochi secondi e con lei Docker e il broker. L'AppHost definisce la risorsa `rabbitmq-wsl`, che esegue `wsl -d Ubuntu-26.04 -- docker start --attach rabbitmq`: la sessione resta aperta finché gira il POC e i log del broker compaiono nel dashboard. Non serve configurare la macchina né lanciare script dopo un riavvio.

## Configurazione locale

Nessun segreto nel repository: i valori stanno negli **user-secrets dell'AppHost**, che li passa ai servizi.

```powershell
dotnet user-secrets --project src/Dusiburg.AI.O2C.AppHost set "ConnectionStrings:sql" "Server=(localdb)\localdev;Database=O2C;Trusted_Connection=True;TrustServerCertificate=True"
dotnet user-secrets --project src/Dusiburg.AI.O2C.AppHost set "ConnectionStrings:rabbitmq" "amqp://o2c:<password>@localhost:5672/o2c"
dotnet user-secrets --project src/Dusiburg.AI.O2C.AppHost set "Parameters:erp-mcp-api-key" "<valore casuale>"
dotnet user-secrets --project src/Dusiburg.AI.O2C.AppHost set "Parameters:crm-mcp-api-key" "<valore casuale>"
```

## Avvio

```powershell
dotnet run --project src/Dusiburg.AI.O2C.AppHost
```

L'URL del dashboard Aspire (con token di login) viene stampato in console.

Il profilo di default dell'AppHost usa HTTPS e richiede il certificato di sviluppo trusted (una volta sola, con conferma di Windows):

```powershell
dotnet dev-certs https --trust
```

In alternativa si può avviare in HTTP:

```powershell
$env:ASPIRE_ALLOW_UNSECURED_TRANSPORT = "true"; dotnet run --project src/Dusiburg.AI.O2C.AppHost --launch-profile http
```

| Servizio | URL locale |
|----------|-----------|
| Erp.Api | http://localhost:5101 |
| Erp.Mcp | http://localhost:5102 |
| Crm.Mcp | http://localhost:5103 |
| Approvals.Web | http://localhost:5104 |
| RabbitMQ management | http://localhost:15672 |

Ogni servizio web espone `GET /` (informativo), `/health` e `/alive` (solo in Development).

## Server MCP

| Server | Endpoint | Tool | API key (user-secrets dell'AppHost) |
|--------|----------|------|-------------------------------------|
| `erp-mcp` | http://localhost:5102/mcp | `get_customer`, `create_customer`, `check_stock`, `create_order`, `get_order` | `Parameters:erp-mcp-api-key` |
| `crm-mcp` | http://localhost:5103/mcp | `get_deal`, `get_company`, `update_deal` | `Parameters:crm-mcp-api-key` |

- Trasporto MCP Streamable HTTP **stateless** (SDK `ModelContextProtocol.AspNetCore` 2.2.0, protocollo `2026-07-28`).
- Header obbligatorio `X-Api-Key`: se manca o è errato la risposta è 401 (ProblemDetails con `code = UNAUTHORIZED`). Senza chiave configurata il server non si avvia.
- Header facoltativo `x-correlation-id`: propagato a `Erp.Api`, sui log e sullo span `mcp.tool {tool.name}` di ogni chiamata (attributi `tool.name`, `correlation.id`, `tool.outcome`).
- Risultati positivi in `structuredContent`, con lo schema pubblicato in `tools/list`. Errori come risultato `isError = true` con `{ "error": { "code", "message" } }` nel testo. `get_customer` restituisce `{ "customer": null }` se il cliente non esiste.
- `create_order` è annotato `destructive` e ha `_meta` `o2c.sensitive = true`: la policy di approvazione arriva con la Fase 5.
- Verifica manuale facoltativa con MCP Inspector (richiede Node): `npx @modelcontextprotocol/inspector`, trasporto Streamable HTTP, URL del server e header `X-Api-Key`.

## Demo

Scenari, dati demo e reset sono descritti in [docs/demo.md](docs/demo.md). Richieste pronte:

- `src/Dusiburg.AI.O2C.Erp.Api/Erp.Api.http` — API dell'ERP: clienti, giacenze, ordini (compreso il doppio POST con la stessa `idempotencyKey`).
- `src/Dusiburg.AI.O2C.Crm.Mcp/Crm.Mcp.dev.http` — endpoint dev del CRM mock (solo Development): deal, chiusura `ClosedWon`, reset.

Per ripetere la demo senza ricreare il database: `POST /dev/reset` su Erp.Api e Crm.Mcp; per ripartire da zero si rilancia `DbInit`.

## Build e test

```powershell
dotnet build Dusiburg.AI.O2C.slnx   # warning trattati come errori
dotnet test --solution Dusiburg.AI.O2C.slnx   # NUnit su Microsoft.Testing.Platform
```

Code coverage: in Visual Studio da **Test → Analizza code coverage per tutti i test**; da riga di comando `dotnet test --solution Dusiburg.AI.O2C.slnx --coverage` (file `.coverage` in `TestResults/`, esclusa da git).

## Convenzioni trasversali

- **Correlazione**: header `x-correlation-id` su ogni chiamata HTTP/MCP, letto o generato (GUID v7) dal middleware `UseCorrelationId()`, propagato in uscita da `CorrelationIdDelegatingHandler`, esposto come attributo `correlation.id` su span e scope di log.
- **Idempotenza**: `IdempotencyKey.From(dealId, revision)` → `o2c-D-1001-r3`, calcolata dal codice e mai dal modello.
- **Errori dei tool**: sempre `{ "error": { "code", "message" } }`, codici in `ToolErrorCodes`; sui server MCP come risultato `isError = true` con l'envelope nel testo, prodotto dal filtro comune di `Mcp.Hosting` (nessuna eccezione arriva al client).
- **Telemetria**: sorgenti `Dusiburg.AI.O2C.*`, attributi `agent.name`, `tool.name`, `correlation.id`, `tool.outcome`.
- **Segreti**: solo user-secrets in locale.
