# 📋 Piano POC Order-to-Cash Agentico — Indice

> **Fonte**: `C:\Dev\Architettura\Architettura Integrazione Agentica ERP-CRM.md` (nel seguito "la specifica", sezioni citate come §N).
> **Repo di destinazione**: `C:\Dev\NetCode\AI.POC-OrderToCash` (da creare in Fase 0).
> **Piano approvato il**: 2026-09-11 — gate socratico chiuso (tutte le proposte accettate, con i dettagli su Docker/RabbitMQ, LocalDB `localdev` e git di Fork).

## Come usare questo piano

- Si implementa **una fase alla volta**, nell'ordine, senza saltare avanti (regola della specifica).
- Ogni file di fase si apre con un **Gate di fase**: domande da chiudere con l'utente **prima** di scrivere codice per quella fase. Le proposte indicate sono il default se l'utente non dice altro.
- Le fasi 2–5 iniziano con uno **spike di verifica API** (Agent Framework / MCP SDK): se l'API reale diverge dalla specifica, si usa l'equivalente e si registra la modifica nella specifica (vedi "Registro modifiche alla specifica").
- A fine fase: Definition of Done comune (sotto) + aggiornamento della tabella **Stato delle fasi**.
- Prompt suggerito per partire: `esegui la fase N del piano in C:\WorkspaceAI\PLANS\2026-09-11-o2c-agentic-poc\fase-N-<nome>.md`

## Stato delle fasi

| Fase | File | Obiettivo | Stato |
|------|------|-----------|-------|
| 0 | [fase-0-setup-scaffolding.md](fase-0-setup-scaffolding.md) | Tooling, repo, solution, AppHost, ServiceDefaults, Shared | ✅ Completata (2026-09-14) |
| 1 | [fase-1-sistemi-base.md](fase-1-sistemi-base.md) | Erp.Api + EF Core + seed; CRM mock | ⬜ Da iniziare |
| 2 | [fase-2-server-mcp.md](fase-2-server-mcp.md) | Erp.Mcp e Crm.Mcp con i tool di §6 | ⬜ Da iniziare |
| 3 | [fase-3-agente-singolo.md](fase-3-agente-singolo.md) | Un agente end-to-end da CLI | ⬜ Da iniziare |
| 4 | [fase-4-multi-agente.md](fase-4-multi-agente.md) | Intake/Fulfillment/Order con handoff + trigger RabbitMQ | ⬜ Da iniziare |
| 5 | [fase-5-human-in-the-loop.md](fase-5-human-in-the-loop.md) | Approvazione, sospensione e ripresa | ⬜ Da iniziare |
| 6 | [fase-6-deploy-osservabilita.md](fase-6-deploy-osservabilita.md) | Azure (outline, da dettagliare) | ⬜ Outline |

Legenda: ⬜ da iniziare · 🟨 in corso · ✅ completata · ⛔ bloccata

## Ambiente rilevato (2026-09-11)

| Voce | Stato | Conseguenza |
|------|-------|-------------|
| .NET SDK | 10.0.400 (nessun .NET 11) | Target `net10.0` (D7) |
| Docker | Engine 29.7.2 in WSL `Ubuntu-26.04`; nessuna CLI docker lato Windows | Aspire non gestisce container: risorse esterne via connection string (D8) |
| RabbitMQ | Container `rabbitmq:4.3.5-management` in WSL, porte 5672/15672 | Broker locale per il trigger (D6) |
| SQL Server LocalDB | Presente (`MSSQLLocalDB`); si crea l'istanza dedicata `localdev` | DB locale (D9) |
| git | Istanza di Fork: `C:\Users\dusim\AppData\Local\Fork\gitInstance\2.50.1\cmd\git.exe` (2.50.1) — non nel PATH | Da aggiungere al PATH utente o usare il path assoluto (D15) |
| Aspire CLI/template | Assenti | Installazione in Fase 0 |
| azd / az | Assenti | Servono solo in Fase 6 |
| Ollama | Assente; GPU RTX 5060 Laptop 8 GB VRAM, 31 GB RAM | Modello locale solo smoke test (D12) |

## Architettura locale (Fasi 0–5)

```mermaid
flowchart LR
    subgraph WSL["WSL Ubuntu-26.04 (Docker)"]
        RMQ[(RabbitMQ<br/>5672 / 15672)]
    end
    subgraph WIN["Windows — .NET Aspire AppHost"]
        CRM["Crm.Mcp<br/>(MCP server + CRM mock + webhook simulato)"]
        ERPMCP["Erp.Mcp<br/>(MCP server)"]
        ERP["Erp.Api<br/>(Minimal API + EF Core)"]
        ORCH["Orchestrator<br/>(agenti + handoff + policy)"]
        APPR["Approvals.Web<br/>(UI /approvals + callback)"]
        DASH["Aspire Dashboard<br/>(OTLP)"]
    end
    DB[("LocalDB (localdb)\\localdev<br/>DB O2C: schemi erp · crm · orch")]
    LLM(["Azure OpenAI<br/>(unica dipendenza cloud)"])

    CRM -- "deal-closed-won" --> RMQ
    RMQ -- consume --> ORCH
    ORCH -- MCP/HTTP --> CRM
    ORCH -- MCP/HTTP --> ERPMCP
    ERPMCP -- HTTP --> ERP
    ORCH -- IChatClient --> LLM
    APPR -- "approval-decided" --> RMQ
    ERP --- DB
    CRM --- DB
    ORCH --- DB
    APPR --- DB
    ORCH -. OTLP .-> DASH
    ERP -. OTLP .-> DASH
    ERPMCP -. OTLP .-> DASH
    CRM -. OTLP .-> DASH
    APPR -. OTLP .-> DASH
```

## Decisioni del gate socratico

| ID | Decisione | Fase |
|----|-----------|------|
| D1 | Aggiunta una **Fase 0 — Setup e scaffolding** prima delle 6 fasi di §11: i NFR di §12 valgono dalla prima riga | 0 |
| D2 | Struttura: `plan.md` indice + un file per fase con Gate di fase, spike, step, test, accettazione, rischi, DoD | — |
| D3 | Fonte di verità del piano: `C:\WorkspaceAI\PLANS\2026-09-11-o2c-agentic-poc\`; copia in `docs/plan/` del repo in Fase 0 | 0 |
| D4 | Fase 6 solo outline; si dettaglia al suo avvio | 6 |
| D5 | Scenari demo nel seed (deal D-1001…D-1008) + README di demo | 1, 5 |
| D6 | Trigger: CLI con `dealId` in Fase 3; in Fase 4 **RabbitMQ** (container esistente in WSL) dietro `IDealEventSource`; Service Bus solo in Fase 6 | 3, 4, 6 |
| D7 | Runtime **`net10.0`** (LTS installato), percorso di upgrade a .NET 11 successivo | 0 |
| D8 | Nessun container gestito da Aspire: RabbitMQ e LocalDB entrano come **risorse esterne via connection string** | 0 |
| D9 | DB locale: **SQL Server LocalDB, istanza dedicata `localdev`**, database `O2C` (stesso provider EF di Azure SQL) | 0, 1 |
| D10 | CRM: **mock persistente** dietro `ICrmClient` dentro `Crm.Mcp` (schema `crm`) + endpoint dev; adapter HubSpot opzionale | 1 |
| D11 | `MODEL_PROVIDER=azure-openai` di default | 3 |
| D12 | Provider Ollama implementato ma solo smoke test su modello piccolo; nessuna accettazione dipende dal modello locale | 3 |
| D13 | Ogni fase che usa Agent Framework / MCP SDK inizia con uno **spike di verifica API**; divergenze → equivalente + aggiornamento specifica | 2–5 |
| D14 | Test con **NUnit 4** (`Assert.That`) su Microsoft.Testing.Platform (in origine xUnit v3, sostituito: D28); progetto aggiuntivo `tests/Dusiburg.AI.O2C.Mcp.Tests` | 0, 2 |
| D15 | git: istanza di **Fork** (2.50.1, nel PATH utente); repo **`C:\Dev\NetCode\AI.POC-OrderToCash`** (clone di `github.com/Dusi-burg/AI.POC-OrderToCash`), branch `develop`, **solo commit locali — il push lo fa l'utente** (rivista al Gate F0, 2026-09-14) | 0 |
| D16 | Ripresa dopo approvazione: spike su **(A) checkpointing nativo** del workflow persistito su SQL; fallback **(B) resume deterministico** dalla proposta persistita | 5 |
| D17 | `ApprovalPolicy` deterministica in C#; **`idempotencyKey` e `CorrelationId` calcolati e iniettati dal codice, mai generati dal modello** | 3, 5 |
| D18 | Schemi e DbContext separati: `erp` (Erp.Api), `crm` (Crm.Mcp), `orch` (Orchestrator) | 1, 4 |
| D19 | `get_deal` espone anche `revision` (serve alla `idempotencyKey` di §12) | 1, 2 |
| D20 | Riga non disponibile → approvazione con ordine completo in **backorder**; SKU inesistente in ERP ("bloccato") → stop con `Failed`, senza approvazione | 4, 5 |
| D21 | Vale lo `unitPrice` del deal (`ListPrice` informativo); `available = OnHand − Reserved ≥ quantity`; `create_order` riserva lo stock nella stessa transazione; `creditLimit` non usato (§7 non lo prevede) | 1 |
| D22 | Solo deal in **EUR**; altre valute → `Discarded` con nota | 4 |
| D23 | `update_deal.status` è l'enum chiuso `ApprovalPending \| OrderCreated \| Rejected \| Expired \| Discarded \| Failed`; si scrive anche `ApprovalPending` alla sospensione | 1, 5 |
| D24 | Scadenza: hosted service nell'Orchestrator → `Expired` + `update_deal` + log (nessuna email) | 5 |
| D25 | Approvatore da configurazione in locale (`DecidedBy`); Entra/Teams in Fase 6; regole "non chiedere più" fuori scope | 5, 6 |
| D26 | WSL spegne la VM (e Docker) pochi secondi dopo l'ultima sessione: l'AppHost tiene aperta una sessione con la risorsa eseguibile `rabbitmq-wsl` (`wsl -d Ubuntu-26.04 -- docker start --attach rabbitmq`). Nessuna configurazione di macchina né script al riavvio; il broker è attivo solo mentre gira l'AppHost (Gate F0, 2026-09-14) | 0, 4 |
| D27 | Root name **`Dusiburg.AI.O2C`**: progetti, cartelle, assembly e namespace si chiamano `Dusiburg.AI.O2C.<Nome>` (es. `src/Dusiburg.AI.O2C.Erp.Api/Dusiburg.AI.O2C.Erp.Api.csproj`), solution `Dusiburg.AI.O2C.slnx`, sorgenti di telemetria `Dusiburg.AI.O2C.*`. Restano invariati i valori di dominio (`o2c-…` della chiave di idempotenza, vhost `o2c`, database `O2C`) e i nomi delle risorse Aspire (`erp-api`, …). Correzione di G0.4 chiesta dall'utente dopo la Fase 0 (2026-09-14) | tutte |
| D28 | Framework di test **NUnit 4** con modello a vincoli `Assert.That` e NUnit.Analyzers, runner NUnit su Microsoft.Testing.Platform (`EnableNUnitRunner`), coverage con `Microsoft.Testing.Extensions.CodeCoverage` (anche da Visual Studio). Sostituisce xUnit v3 dopo un confronto sugli stessi test (stessi risultati); scelto per familiarità dell'utente. Da ricordare: un'istanza per classe di test (stato da reinizializzare in `[SetUp]`) ed esecuzione sequenziale per default (2026-09-14) | tutte |

## Registro modifiche alla specifica

La specifica stessa impone che contratti e decisioni vi siano riportati prima di implementarli. Queste modifiche vanno scritte nel documento (dove, lo decide il Gate di Fase 0).

| ID | § | Modifica | Origine | Stato |
|----|---|----------|---------|-------|
| M1 | §9 | Runtime .NET 10 (`net10.0`) invece di .NET 11 | D7 | ✅ Applicata in F0 (`docs/architettura.md`) |
| M2 | §3.1, §8, §9 | Persistenza locale: LocalDB `(localdb)\localdev` invece di SQLite; schemi `erp`/`crm`/`orch` | D9, D18 | ✅ Applicata in F0 (`docs/architettura.md`) |
| M3 | §6.2 | `get_deal` → output con `revision` | D19 | ✅ Applicata in F0 (`docs/architettura.md`) |
| M4 | §6.2 | `update_deal.status` enum chiuso | D23 | ✅ Applicata in F0 (`docs/architettura.md`) |
| M5 | §10 | Progetto `tests/Dusiburg.AI.O2C.Mcp.Tests` | D14 | ✅ Applicata in F0 (`docs/architettura.md`) |
| M6 | §10 | CRM mock dentro `Crm.Mcp` dietro `ICrmClient` | D10 | ✅ Applicata in F0 (`docs/architettura.md`) |
| M7 | §3.1, §9 | Messaggistica locale: RabbitMQ (exchange `deal-closed-won`) | D6 | ✅ Applicata in F0 (`docs/architettura.md`) |
| M8 | §7, §13 | Nome/semantica del meccanismo di approvazione (`ToolApprovalAgent`) da confermare dopo lo spike | D13 | Da decidere in F5 |
| M9 | §5, §7 | Regole di dominio: parziale/bloccato, solo EUR, prezzo del deal, riserva stock | D20–D22 | ✅ Applicata in F0 (`docs/architettura.md`) |
| M10 | §10 | Eventuale `AZURE_OPENAI_API_KEY` per l'autenticazione locale al modello | Gate F3 | Da decidere in F3 |
| M11 | §10 | Eventuale progetto `src/Dusiburg.AI.O2C.Orchestration.Data` (DbContext `orch` condiviso con Approvals.Web) | Gate F4 | Da decidere in F4 |
| M12 | §7 | Messaggio interno `approval-decided`, colonna `TraceParent` su `ApprovalRequests` | Gate F5 | Da decidere in F5 |
| M13 | §10 | Eventuale `MESSAGING_PROVIDER=rabbitmq\|servicebus` | Gate F6 | Da decidere in F6 |
| M14 | §10 | Repository `AI.POC-OrderToCash` invece di `o2c-agentic-poc` | Gate F0 (D15) | ✅ Applicata in F0 (`docs/architettura.md`) |
| M15 | §10 | Cartelle e progetti con root name `Dusiburg.AI.O2C.<Nome>` | D27 | ✅ Applicata dopo F0 (`docs/architettura.md`) |

## Convenzioni trasversali (valgono da Fase 0)

- **Correlazione**: header `x-correlation-id` su ogni HTTP/MCP; attributo `correlation.id` su span e scope di log; header omonimo sui messaggi del broker. Generato una sola volta all'ingresso del workflow (GUID v7).
- **Idempotenza**: `IdempotencyKey.From(dealId, revision)`, calcolata dal codice e iniettata nella chiamata `create_order`; garanzia finale = indice univoco su `erp.Orders.IdempotencyKey`.
- **Tracing**: ogni tool call produce uno span con `agent.name`, `tool.name`, `correlation.id`, `tool.outcome` + durata; prefisso `ActivitySource` `Dusiburg.AI.O2C.*`.
- **Errori dei tool**: sempre `{ error: { code, message } }`; catalogo codici: `VALIDATION_ERROR`, `NOT_FOUND`, `CONFLICT`, `UNAUTHORIZED`, `UPSTREAM_UNAVAILABLE`, `INTERNAL`.
- **Segreti**: solo user-secrets in locale (AppHost e progetti), mai nel repo; controllo con grep prima di ogni commit.
- **Porte fisse locali** (comode per CLI e `.http`): Erp.Api 5101, Erp.Mcp 5102, Crm.Mcp 5103, Approvals.Web 5104.
- **Build di verifica**: la solution è nuova e non è nella mappa della skill `verify-build`: l'equivalente è `dotnet build Dusiburg.AI.O2C.slnx` sull'intera solution (mai singoli progetti come verifica finale).

## Definition of Done comune a ogni fase

1. `dotnet build Dusiburg.AI.O2C.slnx` — 0 errori, 0 warning (warning trattati come errori).
2. `dotnet test --solution Dusiburg.AI.O2C.slnx` — tutti verdi (Microsoft.Testing.Platform).
3. Criteri di accettazione della fase verificati; esito annotato nel file di fase (sezione "Esito").
4. Nessun segreto nel repo.
5. Commit git con messaggio `fase N: <sintesi>`.
6. Stato aggiornato nella tabella "Stato delle fasi" di questo file (e copia in `docs/plan/`).

## Rischi globali

| ID | Rischio | Mitigazione |
|----|---------|-------------|
| R1 | API di Agent Framework diverse da quanto descritto (es. `ToolApprovalAgent`, handoff, checkpoint) | Spike a inizio fase (D13); registro modifiche |
| R2 | Ripresa del workflow dopo riavvio durante l'attesa | Spike (A) con fallback (B) già progettato (D16) |
| R3 | Affidabilità del tool calling del modello | Temperatura 0.1, output strutturato, fatti presi dai tool e non dai riassunti del modello, policy deterministica |
| R4 | RabbitMQ disponibile solo con WSL avviata | Check di avvio in Fase 0; health check nell'AppHost |
| R5 | I nomi di funzione OpenAI non ammettono il punto (`erp.create_order`) | Nome esposto al modello senza prefisso; nome qualificato `erp.create_order` solo per policy e telemetria (Fase 2/3) |
| R6 | Costi del modello cloud | Deployment piccolo, run di test contati, stub del modello nei test automatici |

## Fuori scope

- Tutto quanto elencato in §14 (fatturazione, multi-tenant, auth utenti finali oltre all'approvatore, ERP reale, RAG, UI conversazionale, agenti aggiuntivi).
- Regole "non chiedere più" dell'approvazione.
- Regole di approvazione su `creditLimit`.
- Multi-valuta.
- Adapter HubSpot reale (opzionale, fuori dal percorso critico).
- Criteri di accettazione basati sul modello locale.
- Container gestiti da Aspire (finché non serve una CLI docker lato Windows).
