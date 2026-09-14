# Fase 3 — Agente singolo

> Indice: [plan.md](plan.md) · Precedente: [Fase 2](fase-2-server-mcp.md) · Successiva: [Fase 4](fase-4-multi-agente.md)

## Obiettivo

Un solo agente che, dato un `dealId`, legge il deal e l'azienda, verifica lo stock, risolve o crea il cliente, crea l'ordine e aggiorna il CRM, usando i tool MCP della Fase 2. Niente handoff, niente approvazione. In questa fase nascono i pezzi che le fasi successive riusano: factory del modello, provider dei tool MCP, guardia sulle chiamate ai tool.

**Accettazione di §11**: percorso felice completo end-to-end da riga di comando; ordine presente in ERP con `ExternalRef` corretto.

## Prerequisiti

Fase 2 completata (server MCP funzionanti e testati).

## Gate di fase

| # | Domanda | Proposta |
|---|---------|----------|
| G3.1 | Endpoint, deployment e modello Azure OpenAI/Foundry | Da indicare (serve un modello con tool calling e output strutturato solidi) |
| G3.2 | Autenticazione locale al modello | API key in user-secrets (`AZURE_OPENAI_API_KEY`, modifica M10); Entra/managed identity in Fase 6. Alternativa: `DefaultAzureCredential` con credenziale di Visual Studio |
| G3.3 | Lingua dei prompt di sistema | Inglese (maggiore affidabilità del modello); documentazione e note CRM in italiano |
| G3.4 | Parser della riga di comando | `System.CommandLine`; in assenza di argomenti il processo parte in modalità worker |
| G3.5 | Smoke test Ollama | Opzionale: container `ollama/ollama` nel Docker di WSL (con GPU solo se il NVIDIA container toolkit è presente), modello piccolo con tool calling |
| G3.6 | Scenari diversi da D-1001 | Non garantiti fino alla Fase 5 (nessuna policy di approvazione ancora): si documenta e si usa solo D-1001 |

## Spike S3 — verifica API di Agent Framework e Microsoft.Extensions.AI (60 min)

Verificare sulle versioni NuGet correnti (`Microsoft.Agents.AI*`, `Microsoft.Extensions.AI*`, `Azure.AI.OpenAI`, provider Ollama):
- creazione di un `ChatClientAgent` da un `IChatClient` con istruzioni, tool e `ChatOptions` (temperatura);
- i tool del client MCP sono utilizzabili direttamente come `AIFunction`;
- wrapper di funzione (`DelegatingAIFunction` o equivalente) per modificare argomenti e schema esposto al modello;
- output strutturato con JSON schema (`RunAsync<T>` o `ResponseFormat`);
- strumentazione OpenTelemetry di chat client e agente e nomi delle `ActivitySource` da registrare in ServiceDefaults;
- provider Ollama compatibile con `IChatClient`.

## Step operativi

**3.1 — `ModelClientFactory`** (`src/Dusiburg.AI.O2C.Orchestrator/Model/`)
- Legge `MODEL_PROVIDER` (`azure-openai` | `ollama`); valore sconosciuto → errore all'avvio.
- `azure-openai`: `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_DEPLOYMENT`, autenticazione secondo G3.2.
- `ollama`: `OLLAMA_ENDPOINT`, `OLLAMA_MODEL`.
- Pipeline `IChatClient` comune: OpenTelemetry + logging; temperatura 0.1 di default (§5).
- L'orchestratore dipende solo da `IChatClient` (§3.3): nessun tipo Azure OpenAI fuori dalla factory.

**3.2 — `McpToolProvider`** (`src/Dusiburg.AI.O2C.Orchestrator/Tools/`)
- Crea i client MCP verso `ERP_MCP_URL` e `CRM_MCP_URL` (da service discovery sotto AppHost, da configurazione in modalità CLI), con header `X-Api-Key` e un handler che aggiunge `x-correlation-id` dal contesto ambientale del run.
- Espone i tool con nome qualificato interno (`erp.create_order`, `crm.get_deal`, …) e filtra per **allow-list dell'agente**.

**3.3 — `ToolInvocationGuard`** (wrapper attorno a ogni tool, riusato da tutte le fasi)
- Rifiuta a runtime i tool fuori dall'allow-list dell'agente (difesa in profondità oltre al filtro in 3.2) → `ToolError(UNAUTHORIZED)` restituito al modello.
- Su `create_order`: **nasconde `idempotencyKey` ed `externalRef` dallo schema esposto al modello** e li inietta dal codice (`IdempotencyKey.From(dealId, revision)`, `externalRef = dealId`), sovrascrivendo eventuali valori del modello (D17).
- Registra i risultati reali dei tool in un `DealContext` del run (deal, azienda, esiti stock, cliente): **i fatti vengono dai tool, non dai riassunti del modello**.
- Apre lo span `tool.call` con `agent.name`, `tool.name`, `correlation.id`, `tool.outcome` e durata (§12).

**3.4 — `SingleOrderAgent`**
- Istruzioni di sistema esplicite: sequenza `get_deal` → `get_company` → `check_stock` per riga → `get_customer` (per partita IVA, poi email) → `create_customer` se assente → `create_order` → `update_deal` con `OrderCreated` e numero ordine.
- Output finale strutturato `OrderOutcome { dealId, status: DealStatus, erpOrderNumber?, reasons[], note }` validato contro JSON schema; output non conforme → un nuovo tentativo, poi `Failed`.

**3.5 — Riga di comando**
- `dotnet run --project src/Dusiburg.AI.O2C.Orchestrator -- process --deal D-1001` (G3.4): crea un nuovo `CorrelationId`, apre l'`Activity` radice `o2c.process_deal`, esegue l'agente, stampa l'`OrderOutcome`, exit code ≠ 0 su `Failed`.
- Senza argomenti: modalità worker (per ora solo heartbeat; il consumer arriva in Fase 4).

**3.6 — Configurazione**
- User-secrets dell'Orchestrator/AppHost: chiavi del modello (G3.1/G3.2), API key dei server MCP.

**3.7 — Smoke Ollama (opzionale, G3.5)**
- Stessa CLI con `MODEL_PROVIDER=ollama`: si verifica solo che il cambio sia di sola configurazione; nessun criterio di accettazione.

### Test — `tests/Dusiburg.AI.O2C.Orchestrator.Tests`

**3.8 — Harness**
- `StubChatClient` con copione di risposte (sequenza di chiamate a funzione, poi JSON finale); tool fake come `AIFunction` in memoria. Nessuna chiamata reale né al modello né ai server MCP.

**3.9 — Casi**
- Il guard inietta la `idempotencyKey` corretta e **sovrascrive** quella proposta dal modello.
- Il guard blocca un tool fuori allow-list e restituisce `UNAUTHORIZED` al modello.
- Lo schema di `create_order` esposto al modello non contiene `idempotencyKey`/`externalRef`.
- Span `tool.call` con gli attributi richiesti (listener di `Activity` nel test).
- `ModelClientFactory`: selezione del provider da configurazione; provider sconosciuto → errore.
- `OrderOutcome` non conforme → nuovo tentativo, poi `Failed`.

## Criteri di accettazione

- [ ] Dopo `POST /dev/deals/D-1001/close-won`, la CLI su D-1001 termina con `OrderCreated` e numero d'ordine.
- [ ] In ERP esiste l'ordine con `ExternalRef = D-1001` e la `IdempotencyKey` attesa.
- [ ] Il deal CRM riporta `OrderCreated` e il numero d'ordine ERP.
- [ ] Rieseguire la CLI su D-1001 non crea un secondo ordine.
- [ ] Nel dashboard una traccia unica `o2c.process_deal` contiene gli span dei tool con gli attributi di §12 e le chiamate MCP → `Erp.Api` correlate.
- [ ] Il cambio di provider del modello avviene solo da configurazione.
- [ ] DoD comune soddisfatta.

## Rischi

| Rischio | Mitigazione |
|---------|-------------|
| Il modello salta passaggi o sbaglia tool | Istruzioni esplicite, temperatura bassa, allow-list, output strutturato con nuovo tentativo |
| Costi dei run reali | Test automatici solo con stub; run reali contati e manuali |
| Nome delle funzioni col punto rifiutato dal modello (R5) | Nome esposto senza prefisso, mappa interna verso il nome qualificato |

## Definition of Done

DoD comune di [plan.md](plan.md#definition-of-done-comune-a-ogni-fase).

## Esito

_Da compilare a fine fase (incluso l'esito dello spike S3)._
