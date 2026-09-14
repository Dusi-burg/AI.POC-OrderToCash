# Fase 0 — Setup e scaffolding

> Indice: [plan.md](plan.md) · Successiva: [Fase 1](fase-1-sistemi-base.md)

## Obiettivo

Macchina pronta, repo versionato e solution completa di tutti i progetti di §10 (vuoti ma avviabili), con i requisiti non funzionali di §12 già cablati: correlazione, idempotenza, telemetria, gestione segreti. Nessuna logica di dominio.

## Prerequisiti

Nessuna fase precedente.

## Gate di fase (da chiudere prima di iniziare)

| # | Domanda | Proposta |
|---|---------|----------|
| G0.1 | Dove vive la specifica aggiornata col registro modifiche M1–M9? | Copiarla nel repo come `docs/architettura.md` (versionata, fonte di verità da qui in poi); il file in `C:\Dev\Architettura` resta come snapshot iniziale |
| G0.2 | RabbitMQ: credenziali `guest` o utente dedicato? | Vhost `o2c` + utente `o2c` dedicato, credenziali in user-secrets |
| G0.3 | git: aggiungere l'istanza di Fork al PATH utente o usare il path assoluto? | Aggiungere `...\Fork\gitInstance\2.50.1\cmd` al PATH utente; verificare `user.name`/`user.email` |
| G0.4 | Naming: root name `Dusiburg.AI.O2C` per progetti, cartelle, assembly e namespace (es. `src/Dusiburg.AI.O2C.Erp.Api/Dusiburg.AI.O2C.Erp.Api.csproj`)? | Sì |
| G0.5 | Warning trattati come errori fin da subito? | Sì |
| G0.6 | Versione Aspire | Ultima stabile compatibile con net10.0, verificata all'avvio della fase |

### Esito del gate (2026-09-14)

| # | Decisione |
|---|-----------|
| — | Repo: si usa il clone esistente `C:\Dev\NetCode\AI.POC-OrderToCash` (remote `github.com/Dusi-burg/AI.POC-OrderToCash`), branch `develop`; commit solo locale, push a cura dell'utente. Il `.gitignore` .NET è già presente. |
| G0.1 | Proposta accettata (`docs/architettura.md`) |
| G0.2 | Proposta accettata: vhost `o2c` + utente `o2c`, password casuale solo in user-secrets (sul broker esistono già `admin` e `app`, non toccati) |
| G0.3 | Già fatto dall'utente: Fork git nel PATH utente, `user.name`/`user.email` impostati |
| G0.4 | Root name `Dusiburg.AI.O2C` (D27): applicato con la rinomina successiva al commit di fase, vedi Esito |
| G0.5 | Proposta accettata |
| G0.6 | Aspire **13.5.3** (ultima stabile al 2026-09-14); SDK .NET installato 10.0.401 |

## Step operativi

### Prerequisiti macchina

**P0.1 — git (istanza di Fork)**
- Aggiungere `C:\Users\dusim\AppData\Local\Fork\gitInstance\2.50.1\cmd` al PATH utente (`[Environment]::SetEnvironmentVariable('Path', ..., 'User')`), riaprire la shell, verificare `git --version` → `2.50.1.windows.1`.
- Verificare/impostare `git config --global user.name` e `user.email`.

**P0.2 — LocalDB istanza `localdev`**
- `sqllocaldb create localdev -s` e `sqllocaldb info localdev`.
- Connection string di riferimento: `Server=(localdb)\localdev;Database=O2C;Trusted_Connection=True;TrustServerCertificate=True`.

**P0.3 — RabbitMQ in WSL**
- Verificare la restart policy del container: `wsl -e docker inspect -f '{{.HostConfig.RestartPolicy.Name}}' rabbitmq` (atteso `unless-stopped` o `always`).
- Da Windows: `Test-NetConnection localhost -Port 5672` e management UI `http://localhost:15672`.
- Creare vhost `o2c` e utente `o2c` con permessi sul vhost (da management UI o `rabbitmqctl` nel container), secondo G0.2.
- Connection string di riferimento: `amqp://o2c:<pwd>@localhost:5672/o2c`.

**P0.4 — .NET e Aspire**
- `dotnet --list-sdks` deve mostrare 10.0.400 (in una shell è capitato che `dotnet` non fosse nel PATH: verificare).
- `dotnet new install Aspire.ProjectTemplates` (versione compatibile net10.0).
- Tool locale EF: nel repo, `dotnet new tool-manifest` + `dotnet tool install dotnet-ef` (serve dalla Fase 1).

### Repo e solution

**0.1 — Repo**
- Creare `C:\Dev\NetCode\o2c-agentic-poc`, `git init`, `dotnet new gitignore`, `.gitattributes` (`* text=auto`), `dotnet new editorconfig`, `README.md` (scopo, prerequisiti, come avviare).

**0.2 — File di build condivisi**
- `global.json`: SDK `10.0.400`, `rollForward: latestFeature`.
- `Directory.Build.props`: `TargetFramework=net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest` (assembly e namespace coincidono con il nome del progetto `Dusiburg.AI.O2C.<Nome>`).
- `Directory.Packages.props`: `ManagePackageVersionsCentrally=true`; versioni fissate qui.

**0.3 — Solution `Dusiburg.AI.O2C.slnx`** (`dotnet new sln --format slnx`)

| Progetto | Template | Note |
|----------|----------|------|
| `src/Dusiburg.AI.O2C.AppHost` | Aspire AppHost | composizione locale |
| `src/Dusiburg.AI.O2C.ServiceDefaults` | Aspire ServiceDefaults | OTel, health, discovery, resilienza |
| `src/Dusiburg.AI.O2C.Erp.Api` | web (minimal API) | |
| `src/Dusiburg.AI.O2C.Erp.Mcp` | web | |
| `src/Dusiburg.AI.O2C.Crm.Mcp` | web | |
| `src/Dusiburg.AI.O2C.Orchestrator` | worker | |
| `src/Dusiburg.AI.O2C.Approvals.Web` | web (Razor Pages) | |
| `src/Dusiburg.AI.O2C.Shared` | classlib | nessuna dipendenza da ASP.NET/EF |
| `tests/Dusiburg.AI.O2C.Erp.Api.Tests` | xUnit v3 | |
| `tests/Dusiburg.AI.O2C.Mcp.Tests` | xUnit v3 | D14 |
| `tests/Dusiburg.AI.O2C.Orchestrator.Tests` | xUnit v3 | include i test degli helper di `Shared` |

**0.4 — ServiceDefaults**
- `AddServiceDefaults()`: OpenTelemetry (tracce, metriche, log) con exporter OTLP da `OTEL_EXPORTER_OTLP_ENDPOINT`, `AddSource("Dusiburg.AI.O2C.*")`; health check `/health` e `/alive`; service discovery; `AddStandardResilienceHandler` sugli HttpClient.
- Middleware `UseCorrelationId()`: legge o genera `x-correlation-id`, lo mette nel contesto ambientale, nell'`Activity` corrente (tag e baggage `correlation.id`) e in uno scope di log; lo rimanda nella risposta.
- `CorrelationIdDelegatingHandler` per gli HttpClient in uscita.

**0.5 — Shared**
- `Correlation/`: `ICorrelationContext` (basato su `AsyncLocal`), costante `HeaderName = "x-correlation-id"`, `CorrelationId.New()` (GUID v7).
- `Idempotency/IdempotencyKey.From(string dealId, int revision)` → stringa deterministica e leggibile, es. `o2c-D-1001-r3` (validazione input, lunghezza max 100).
- `Errors/`: `ToolError(string Code, string Message)`, envelope `ToolErrorResponse { error }`, costanti `ToolErrorCodes`.
- `Contracts/Erp/` e `Contracts/Crm/`: record che rispecchiano gli input/output di §6 (con `revision` in `DealDto`, M3).
- `DealStatus` enum (D23), `OrderStatus` enum (`Confirmed`, `Backorder`).
- `Telemetry/O2CTelemetry`: nomi delle `ActivitySource` (`Dusiburg.AI.O2C.Orchestrator`, `Dusiburg.AI.O2C.Mcp.Erp`, `Dusiburg.AI.O2C.Mcp.Crm`, `Dusiburg.AI.O2C.Erp.Api`, `Dusiburg.AI.O2C.Approvals`) e chiavi degli attributi (`agent.name`, `tool.name`, `correlation.id`, `tool.outcome`).

**0.6 — AppHost**
- `builder.AddConnectionString("sql")` e `builder.AddConnectionString("rabbitmq")` (valori in user-secrets dell'AppHost: `ConnectionStrings:sql`, `ConnectionStrings:rabbitmq`).
- `AddProject` per ogni servizio con `WithReference` coerenti (per ora: `sql` a Erp.Api, Crm.Mcp, Orchestrator, Approvals.Web; `rabbitmq` a Crm.Mcp, Orchestrator, Approvals.Web) e porte fisse (Erp.Api 5101, Erp.Mcp 5102, Crm.Mcp 5103, Approvals.Web 5104).
- Parametri segreti predisposti (`AddParameter(..., secret: true)`) per le API key dei server MCP e per il modello (valorizzati nelle fasi successive).

**0.7 — Scheletri avviabili**
- Ogni progetto web: `AddServiceDefaults()` + `MapDefaultEndpoints()` + `UseCorrelationId()`, un endpoint `GET /` informativo.
- Orchestrator: `BackgroundService` di heartbeat che logga ogni 30 s con il proprio nome (sostituito nelle fasi successive).
- Log di avvio che conferma la presenza delle connection string **senza stamparne il valore**.

**0.8 — Test degli helper**
- `IdempotencyKey`: stesso input → stessa chiave; revisione diversa → chiave diversa; input non valido → eccezione.
- `CorrelationIdDelegatingHandler`: propaga l'header dal contesto ambientale.
- Middleware di correlazione: genera l'id se assente, rispetta quello in ingresso.

**0.9 — Specifica e piano nel repo**
- Secondo G0.1: copiare la specifica in `docs/architettura.md` e aggiungere la sezione **§17 Registro delle modifiche** con M1–M7 e M9 applicate (M8, M10–M13 elencate come "da decidere").
- Copiare questa cartella del piano in `docs/plan/`.

**0.10 — Primo commit**
- Controllo segreti: grep su `Password=`, `AccountKey=`, `api-key`, `amqp://` nel repo (atteso: nessun risultato fuori da esempi segnaposto).
- `git add` + commit `fase 0: scaffolding solution O2C`.

## Test

- Unit test degli helper (0.8) in `tests/Dusiburg.AI.O2C.Orchestrator.Tests/Shared/`.

## Criteri di accettazione

- [x] `git --version` funziona da PowerShell senza path assoluto.
- [x] `sqllocaldb info localdev` mostra l'istanza in esecuzione.
- [x] RabbitMQ raggiungibile da Windows sulla 5672 con l'utente `o2c` sul vhost `o2c`.
- [x] `dotnet build Dusiburg.AI.O2C.slnx` senza errori né warning.
- [x] `dotnet test --solution Dusiburg.AI.O2C.slnx` verde.
- [x] `dotnet run --project src/Dusiburg.AI.O2C.AppHost`: il dashboard Aspire mostra Erp.Api, Erp.Mcp, Crm.Mcp, Orchestrator e Approvals.Web in stato Running/Healthy.
- [x] Nel dashboard è visibile una traccia di una richiesta HTTP con attributo `correlation.id`.
- [x] Nessun segreto nel repo; primo commit eseguito.

## Rischi

| Rischio | Mitigazione |
|---------|-------------|
| WSL non avviata → RabbitMQ irraggiungibile | Health check RabbitMQ nei servizi che lo usano (dalla Fase 4); nota nel README |
| Template Aspire legati a una versione di .NET diversa | Fissare la versione dei pacchetti Aspire in `Directory.Packages.props` |
| `dotnet` non nel PATH in alcune shell | Verifica in P0.4 |

## Definition of Done

DoD comune di [plan.md](plan.md#definition-of-done-comune-a-ogni-fase).

## Esito

**Completata il 2026-09-14** — branch `develop` di `C:\Dev\NetCode\AI.POC-OrderToCash`, commit `fase 0: scaffolding solution O2C` (solo locale, push a cura dell'utente).

### Ambiente
- git di Fork nel PATH utente (fatto dall'utente); LocalDB `localdev` avviata.
- RabbitMQ: vhost `o2c`, utente `o2c` con permessi `.* .* .*` sul vhost; password casuale solo negli user-secrets dell'AppHost.
- Template `Aspire.ProjectTemplates` 13.5.3 e `xunit.v3.templates` 4.0.1; tool locale `dotnet-ef` 10.0.12 (`dotnet-tools.json`).
- User-secrets dell'AppHost: `ConnectionStrings:sql`, `ConnectionStrings:rabbitmq`, `Parameters:erp-mcp-api-key`, `Parameters:crm-mcp-api-key`.

### Verifiche
| Criterio | Evidenza |
|----------|----------|
| Build | `dotnet build Dusiburg.AI.O2C.slnx` → 0 avvisi, 0 errori |
| Test | `dotnet test --solution Dusiburg.AI.O2C.slnx` → 41/41 (Orchestrator.Tests 32, Mcp.Tests 6, Erp.Api.Tests 3) |
| AppHost | `/health` 200 su 5101–5104; tutte e 5 le risorse inviano telemetria al dashboard; log `Dusiburg.AI.O2C.Orchestrator heartbeat` presente. Verificato via API di telemetria del dashboard (`/api/telemetry/resources`, `/traces`, `/logs`), non a vista nella UI |
| Correlazione | `GET /` con `x-correlation-id: fase0-verifica-510x` → header restituito; span HTTP con `correlation.id` = valore inviato su tutti e 4 i servizi web |
| Segreti | log di avvio "Connection string sql/rabbitmq is configured" senza valori; nessuna password nei log; grep sul repo: solo segnaposto |
| AMQP | client RabbitMQ.Client da Windows: connesso come `o2c` sul vhost `o2c`, RabbitMQ 4.3.5 |

### Scostamenti e decisioni emerse
- **Repo**: clone esistente `AI.POC-OrderToCash` invece di `o2c-agentic-poc` (D15 rivista, M14).
- **RabbitMQ e WSL (D26)**: senza sessioni aperte WSL spegne la VM in pochi secondi e il broker con lei. L'AppHost ha la risorsa eseguibile `rabbitmq-wsl` (`wsl -d <distro> -- docker start --attach rabbitmq`, distro e container configurabili con `RabbitMq:WslDistro` / `RabbitMq:Container`). In Fase 4 i test che usano il broker dovranno tenerne conto.
- **Test runner**: xUnit v3 su Microsoft.Testing.Platform (`xunit.v3.mtp-v2`, `global.json` → `test.runner`). Il comando è `dotnet test --solution Dusiburg.AI.O2C.slnx` (aggiornata la DoD in `plan.md`).
- **Test di ServiceDefaults**: middleware e handler in `tests/Dusiburg.AI.O2C.Orchestrator.Tests/ServiceDefaults/`, helper di Shared in `tests/Dusiburg.AI.O2C.Orchestrator.Tests/Shared/`. `Erp.Api.Tests` ha uno smoke test con `WebApplicationFactory` (GET `/`, `/health`, correlation id); `Mcp.Tests` testa la serializzazione di contratti ed envelope di errore.
- **Contratti in Shared**: `CustomerId` int, `OrderId` GUID, `OrderLineId` int, `CompanyId`/`DealId` string (coerenti con Fase 1). Gli enum `DealStatus`/`OrderStatus` si serializzano per nome e **rifiutano i valori interi** (`StrictStringEnumConverter`).
- **Parametri segreti**: predisposte e valorizzate solo le API key MCP (`ERP_MCP_API_KEY`, `CRM_MCP_API_KEY` passate a server e Orchestrator); la credenziale del modello resta al Gate di Fase 3 (M10).
- **Porte fisse** tramite `launchSettings.json` (solo profilo `http` nei servizi); `Approvals.Web` senza redirect HTTPS in locale.
- **Certificato HTTPS di sviluppo non trusted** sulla macchina: la verifica è stata fatta col profilo `http` dell'AppHost (`ASPIRE_ALLOW_UNSECURED_TRANSPORT=true`). Per il profilo di default serve una volta `dotnet dev-certs https --trust` (vedi README).
- Versioni fissate in `Directory.Packages.props`: OpenTelemetry 1.18.0, Http.Resilience/ServiceDiscovery 10.10.0, Microsoft.Extensions.Hosting 10.0.12, Mvc.Testing 10.0.12, xunit.v3.mtp-v2 4.0.1; Aspire SDK 13.5.3 nell'AppHost.

### Rinomina con root name `Dusiburg.AI.O2C` (2026-09-14, dopo il commit di fase)
Correzione di G0.4 chiesta dall'utente (D27, M15):
- Cartelle e `.csproj` rinominati con `git mv` in `Dusiburg.AI.O2C.<Nome>` (src e tests); solution `Dusiburg.AI.O2C.slnx`.
- Rimosso da `Directory.Build.props` il prefisso calcolato su `AssemblyName`/`RootNamespace`: il nome del progetto contiene già il root.
- Namespace, `ProjectReference`, `Projects.Dusiburg_AI_O2C_*` nell'AppHost, `aspire.config.json`, sorgenti di telemetria (`Dusiburg.AI.O2C.*`), categoria di log `Dusiburg.AI.O2C.Startup` aggiornati. `UserSecretsId` dell'AppHost invariato (segreti conservati).
- Invariati: chiave di idempotenza `o2c-…`, vhost/utente `o2c`, database `O2C`, nomi delle risorse Aspire, classe `O2CTelemetry`.
- Verifica: build 0/0, test 41/41; AppHost avviato: `/health` 4/4, `GET /` restituisce `Dusiburg.AI.O2C.*`, span con `correlation.id`, heartbeat `Dusiburg.AI.O2C.Orchestrator`.
