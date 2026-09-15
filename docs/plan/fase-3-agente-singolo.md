# Fase 3 — Agente singolo

> Indice: [plan.md](plan.md) · Precedente: [Fase 2](fase-2-server-mcp.md) · Successiva: [Fase 4](fase-4-multi-agente.md)

## Obiettivo

Un solo agente che, dato un `dealId`, legge il deal e l'azienda, verifica lo stock, risolve o crea il cliente, crea l'ordine e aggiorna il CRM, usando i tool MCP della Fase 2. Niente handoff, niente approvazione. In questa fase nascono i pezzi che le fasi successive riusano: factory del modello, provider dei tool MCP, guardia sulle chiamate ai tool.

**Accettazione di §11**: percorso felice completo end-to-end da riga di comando; ordine presente in ERP con `ExternalRef` corretto.

## Prerequisiti

Fase 2 completata (server MCP funzionanti e testati).

## Gate di fase

> Chiuso il 2026-09-15 (decisioni D39–D42 in [plan.md](plan.md)), dopo un'analisi su cosa serve al modello e su cosa può girare in locale con 8 GB di VRAM.

| # | Domanda | Decisione |
|---|---------|-----------|
| G3.1 | Modello cloud | **Deciso dall'utente (D39)**: Claude via API Anthropic, **`claude-sonnet-5`**, SDK ufficiale `Anthropic` per C# (implementa `IChatClient`); `MODEL_PROVIDER` = `anthropic` \| `ollama`. Azure OpenAI esce dal POC (rivede D11, M23) |
| G3.2 | Autenticazione locale al modello | **Deciso dall'utente (D40)**: `ANTHROPIC_API_KEY` negli user-secrets (M10) |
| G3.3 | Lingua dei prompt di sistema | **Proposta accettata (D42)**: inglese; documentazione e note CRM in italiano |
| G3.4 | Parser della riga di comando | **Proposta accettata (D42)**: `System.CommandLine`; senza argomenti modalità worker |
| G3.5 | Modello locale | **Deciso dall'utente (D41)**: Ollama nativo Windows (non più Docker in WSL) con `qwen3.5:9b`, misurato su N run del flusso reale di D-1001; nessun criterio vincolante (D12) |
| G3.6 | Scenari diversi da D-1001 | **Proposta accettata (D42)**: non garantiti fino alla Fase 5; si usa solo D-1001 |
| G3.7 | Su quale modello si fa l'accettazione? | **Deciso dall'utente (D41)**: Claude; il locale è misurato e documentato |

### Analisi del modello (2026-09-15)

- **Macchina**: RTX 5060 Laptop 8 GB (circa 7 GB liberi), Ryzen AI 7 350, 31 GB di RAM; nessun runtime locale né credenziale cloud preesistenti.
- **Pacchetti correnti**: `Microsoft.Agents.AI` 1.21.0, `Microsoft.Extensions.AI` 10.10.0, `Anthropic` 12.47.0 (anche `Anthropic.Foundry` 0.7.1), `OllamaSharp` 5.4.30.
- **Locale con 8 GB**: `qwen3.5:9b` (~6,6 GB in Q4_K_M) è il candidato più citato, con bug noti di tool call stampate come testo e interazione col thinking; sotto i 7B i migliori su BFCL sono Qwen3-4B-Instruct-2507, Gemma 4 E4B e Phi-4-mini. Le fonti indicano che i modelli di questa fascia perdono coerenza dopo 2–3 passaggi: il flusso di D-1001 ne ha 8–10, mentre gli agenti della Fase 4 ne avranno 2–4 ciascuno. Accorgimenti: thinking disattivato, temperatura bassa, cache KV almeno Q8, contesto 16–32K.
- **Claude**: prezzi per milione di token (input / output) Opus 5 5 $ / 25 $, Sonnet 5 2 $ / 10 $, Haiku 4.5 1 $ / 5 $; stima di un run di D-1001 con Sonnet 5 circa 0,12–0,15 $. Su Foundry, structured outputs e adaptive thinking sono in beta.

## Spike S3 — verifica API di Agent Framework e Microsoft.Extensions.AI (60 min)

Verificare sulle versioni NuGet correnti (`Microsoft.Agents.AI*`, `Microsoft.Extensions.AI*`, `Azure.AI.OpenAI`, provider Ollama):
- creazione di un `ChatClientAgent` da un `IChatClient` con istruzioni, tool e `ChatOptions` (temperatura);
- i tool del client MCP sono utilizzabili direttamente come `AIFunction`;
- wrapper di funzione (`DelegatingAIFunction` o equivalente) per modificare argomenti e schema esposto al modello;
- output strutturato con JSON schema (`RunAsync<T>` o `ResponseFormat`);
- strumentazione OpenTelemetry di chat client e agente e nomi delle `ActivitySource` da registrare in ServiceDefaults;
- provider Ollama compatibile con `IChatClient`.

### Spike S3 e misure del modello locale (2026-09-15)

Strumento usa e getta fuori dal repo: agente `ChatClientAgent` sui server MCP reali (AppHost avviato), guardia prototipo, N run per deal ripartendo ogni volta dai dati demo; successo = deal CRM `OrderCreated` con numero d'ordine.

| Verifica | Esito |
|----------|-------|
| API Anthropic | `AnthropicClientExtensions.AsIChatClient(client, model, maxOutputTokens, AnthropicThinkingMode)` (namespace `Microsoft.Extensions.AI`); Claude Sonnet 5 non accetta `temperature` |
| API Agent Framework 1.21 | `ChatClientAgent(IChatClient, ChatClientAgentOptions)`, `CreateSessionAsync`, `RunAsync` / `RunAsync<T>`; i `McpClientTool` sono `AIFunction` usabili direttamente; `DelegatingAIFunction` permette di cambiare schema e argomenti |
| OllamaSharp 5.4.30 | `OllamaApiClient` è un `IChatClient`; `ChatOptions.AddOllamaOption(OllamaOption.Think / NumCtx, …)` |
| Ollama 0.34.0 su Windows | RTX 5060 Laptop rilevata (CUDA 12.0, 6,9 GB liberi); **contesto di default 4096** con 8 GB → portato a 16384 |
| ⚠️ Output strutturato insieme ai tool | Con `RunAsync<T>` lo schema arriva a Ollama come `format` già nella prima richiesta: **Qwen non chiama nessun tool e inventa l'esito** (`OrderCreated`, `SO-2026-000001`) in 57 s. Rimedio adottato: run di lavoro senza schema, poi richiesta dell'esito strutturato nella stessa sessione |
| ⚠️ Memoria | Il processo di Ollama con `qwen3.5:9b` e contesto 16K arriva a **14,3 GB di memoria privata** (5,5 GB in VRAM, il resto su RAM): con AppHost e Visual Studio aperti il sistema ha interrotto i processi in background per memoria insufficiente |

Misure con `qwen3.5:9b` (Q4_K_M, thinking disattivato, temperatura 0.1), dopo il rimedio sull'output strutturato:

| Deal | Scenario | Run | Esito | Chiamate | Durata media | Note |
|------|----------|-----|-------|----------|--------------|------|
| D-1001 | percorso felice | 10 | **10/10** `OrderCreated`, esito strutturato valido 10/10 | 9 (sempre la stessa sequenza) | 33,8 s | Argomenti di `create_order` verificati: righe, quantità e prezzi uguali al deal, `customerId` da `get_customer`; nessun errore dei tool; ~27k token in input a run |
| D-1004 | cliente nuovo | 5 | **5/5** `OrderCreated` | 9–10 | 37,3 s | Ramo `create_customer` seguito; in un run anche la ricerca per email prevista dalle istruzioni |
| D-1007 | SKU inesistente | 5 | **5/5** comportamento corretto: `check_stock` → `NOT_FOUND`, nessun `create_order`, `update_deal` `Failed` senza numero d'ordine, esito strutturato `Failed` valido 5/5 | 6–7 | 32 s | Note CRM sensate e in italiano (es. "SKU IND-SEN-999 non trovato in ERP - impossibile creare ordine con articolo inesistente"); in un run `get_customer` saltato dopo l'errore. Primo run nella serie interrotta per memoria, altri 4 rieseguiti con Visual Studio chiuso: a fine serie Ollama 9,2 GB di memoria privata, memoria impegnabile libera 5,3 GB con l'AppHost acceso |

Stessi scenari con **Claude Haiku 4.5** (API Anthropic, stesso strumento, stesse istruzioni, nessuna `temperature`, nessun thinking):

| Deal | Run | Haiku 4.5 | Durata media | Qwen 3.5 9B locale | Durata media |
|------|-----|-----------|--------------|--------------------|--------------|
| D-1001 | 5 | **5/5** `OrderCreated`, 9 chiamate, argomenti di `create_order` verificati | 18,0 s (15–16 s dopo il primo) | 10/10 | 33,8 s |
| D-1004 | 3 | **3/3** `OrderCreated`, `create_customer` seguito, argomenti verificati | 17,7 s | 5/5 | 37,3 s |
| D-1007 | 3 | **3/3** `Failed` corretto, nessun `create_order`, 6 chiamate (senza `get_customer`) | 10,5 s | 5/5 | 32 s |

Token per run di D-1001: Haiku ~33,6k in input e ~1,3k in output (circa 0,04 $ con 1 $ / 5 $ per milione), Qwen ~27k / ~1k. Note CRM di Haiku sensate, quasi sempre in italiano (una in inglese); per D-1004 totale calcolato correttamente nella nota (2.069,00 €).

**Sintesi sul locale**: con istruzioni esplicite, esito strutturato chiesto dopo il lavoro, thinking disattivato e contesto a 16K, `qwen3.5:9b` su 8 GB di VRAM completa correttamente tutti e tre gli scenari misurati (20 run su 20), in circa 30–40 s a run. Il limite pratico è la memoria di sistema: il modello caricato occupa 9–14 GB oltre alla VRAM, quindi AppHost, modello e altri strumenti pesanti non stanno comodamente insieme su 31 GB.

## Step operativi

**3.1 — `ModelClientFactory`** (`src/Dusiburg.AI.O2C.Orchestrator/Model/`)
- Legge `MODEL_PROVIDER` (`anthropic` | `ollama`, D39); valore sconosciuto o `ANTHROPIC_API_KEY` mancante → errore alla creazione del client (non all'avvio del worker, che in Fase 3 non usa il modello).
- `anthropic`: `ANTHROPIC_API_KEY`, `ANTHROPIC_MODEL` (default `claude-sonnet-5`), `AnthropicClient.AsIChatClient(model, maxOutputTokens)`. **Nessuna `temperature`**: Claude Sonnet 5 rifiuta i parametri di sampling (HTTP 400), quindi la temperatura 0.1 di §5 vale solo per Ollama.
- `ollama`: `OLLAMA_ENDPOINT`, `OLLAMA_MODEL` (default `qwen3.5:9b`), `OLLAMA_NUM_CTX` (default 16384: con 8 GB Ollama sceglierebbe 4096); temperatura 0.1, thinking disattivato (`OllamaOption.Think = false`); `HttpClient` dedicato con timeout di 10 minuti, senza la resilienza di ServiceDefaults.
- Pipeline `IChatClient` comune: logging + OpenTelemetry con sorgente `Dusiburg.AI.O2C.Orchestrator.Model`.
- L'orchestratore dipende solo da `IChatClient` (§3.3): nessun tipo Anthropic od Ollama fuori dalla factory.

**3.2 — `McpToolCatalog`** (`src/Dusiburg.AI.O2C.Orchestrator/Tools/`, dietro `IToolCatalog`)
- Crea i client MCP verso `ERP_MCP_URL` e `CRM_MCP_URL` (default `http://erp-mcp/mcp`, `http://crm-mcp/mcp`: service discovery sotto AppHost; in CLI le porte fisse 5102/5103 da `appsettings.Development.json`), con header `X-Api-Key` e gli `HttpClient` di ServiceDefaults, che aggiungono `x-correlation-id` dal contesto ambientale del run.
- Espone i tool con nome qualificato interno (`erp.create_order`, `crm.get_deal`, …) e `_meta` `o2c.sensitive`; l'agente filtra per **allow-list**.

**3.3 — `ToolInvocationGuard`** (wrapper attorno a ogni tool, riusato da tutte le fasi)
- Rifiuta a runtime i tool fuori dall'allow-list dell'agente (difesa in profondità oltre al filtro in 3.2) → `ToolError(UNAUTHORIZED)` restituito al modello.
- Su `create_order`: **nasconde `idempotencyKey` ed `externalRef` dallo schema esposto al modello** e li inietta dal codice (`IdempotencyKey.From(dealId, revision)`, `externalRef = dealId`), sovrascrivendo eventuali valori del modello (D17).
- Registra i risultati reali dei tool in un `DealContext` del run (deal, azienda, esiti stock, cliente): **i fatti vengono dai tool, non dai riassunti del modello**.
- Apre lo span `tool.call` con `agent.name`, `tool.name`, `correlation.id`, `tool.outcome` e durata (§12).

**3.4 — `SingleOrderAgent`** (`ChatClientAgent` di Agent Framework 1.21)
- Istruzioni di sistema esplicite: sequenza `get_deal` → `get_company` → `check_stock` per riga → `get_customer` (per partita IVA, poi email) → `create_customer` se assente → `create_order` → `update_deal` con `OrderCreated` e numero ordine.
- Output finale strutturato `OrderOutcome { dealId, status: DealStatus, erpOrderNumber?, reasons[], note }` con `RunAsync<OrderOutcome>`; output non conforme → **una** nuova richiesta nella stessa sessione, senza rifare i tool.
- **Esito dai fatti**: stato e numero d'ordine finali vengono dal `DealRunContext` (ordine creato e deal aggiornato a `OrderCreated`), non dall'esito del modello, che è solo confrontato (`ModelOutcomeValid`, motivi in caso di discrepanza). Scostamento dal piano originale ("dopo il secondo tentativo `Failed`"): con i fatti già scritti in ERP e CRM, dichiarare `Failed` sarebbe incoerente con i sistemi.

**3.5 — Riga di comando** (`DealProcessor` + `OrchestratorCli`)
- `dotnet run --project src/Dusiburg.AI.O2C.Orchestrator -- process --deal D-1001` (G3.4, `System.CommandLine` 2.0.12): nuovo `CorrelationId` (contesto ambientale + scope di log), `Activity` radice `o2c.process_deal` con `deal.id` e `o2c.outcome`, esecuzione dell'agente, esito in JSON; exit code 0 `OrderCreated`, 1 non concluso, 2 errore.
- Senza argomenti: modalità worker (per ora solo heartbeat; il consumer arriva in Fase 4).

**3.6 — Configurazione**
- Unica fonte dei segreti: user-secrets dell'AppHost (`Parameters:anthropic-api-key`, `Parameters:erp-mcp-api-key`, `Parameters:crm-mcp-api-key`), passati all'orchestratore come variabili d'ambiente.
- In Development la CLI lanciata fuori dall'AppHost legge gli stessi user-secrets (senza sovrascrivere valori già presenti) e invia la telemetria al dashboard con `AppHost:OtlpApiKey` su `O2C_CLI_OTLP_ENDPOINT`.

**3.7 — Misura del modello locale (G3.5, D41)**
- Stesso flusso con `MODEL_PROVIDER=ollama` e `qwen3.5:9b`, N run su D-1001 riportati ogni volta ai dati demo: completamento, sequenza e numero di chiamate, errori dei tool, esito strutturato valido, durata, token. Nessun criterio di accettazione; confronto con Claude sullo stesso flusso.

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

- [x] Dopo `POST /dev/deals/D-1001/close-won`, la CLI su D-1001 termina con `OrderCreated` e numero d'ordine.
- [x] In ERP esiste l'ordine con `ExternalRef = D-1001` e la `IdempotencyKey` attesa.
- [x] Il deal CRM riporta `OrderCreated` e il numero d'ordine ERP.
- [x] Rieseguire la CLI su D-1001 non crea un secondo ordine.
- [x] Nel dashboard una traccia unica `o2c.process_deal` contiene gli span dei tool con gli attributi di §12 e le chiamate MCP → `Erp.Api` correlate.
- [x] Il cambio di provider del modello avviene solo da configurazione.
- [x] DoD comune soddisfatta (commit lasciato all'utente, come nelle fasi precedenti).

## Rischi

| Rischio | Mitigazione |
|---------|-------------|
| Il modello salta passaggi o sbaglia tool | Istruzioni esplicite, temperatura bassa, allow-list, output strutturato con nuovo tentativo |
| Costi dei run reali | Test automatici solo con stub; run reali contati e manuali |
| Nome delle funzioni col punto rifiutato dal modello (R5) | Nome esposto senza prefisso, mappa interna verso il nome qualificato |

## Definition of Done

DoD comune di [plan.md](plan.md#definition-of-done-comune-a-ogni-fase).

## Esito

**Completata il 2026-09-15** — branch `develop` di `C:\Dev\NetCode\AI.POC-OrderToCash`, modifiche non committate (il commit lo fa l'utente). Spike S3 e misure del modello locale nella sezione dedicata sopra.

### Verifiche
| Criterio | Evidenza |
|----------|----------|
| Build | `dotnet build Dusiburg.AI.O2C.slnx` → 0 avvisi, 0 errori |
| Test | `dotnet test --solution Dusiburg.AI.O2C.slnx` → 151/151 (129 in Fase 2): +22 in `Orchestrator.Tests` (factory del modello, guardia, agente con modello a copione) |
| Run di accettazione | Su indicazione dell'utente con **Claude Haiku 4.5** (`ANTHROPIC_MODEL=claude-haiku-4-5`, D43), AppHost avviato: dati demo ripristinati, `close-won` su D-1001 (revisione 1), poi `dotnet run --project src/Dusiburg.AI.O2C.Orchestrator --no-build -- process --deal D-1001` → exit code 0, `OrderCreated`, `SO-2026-000001`, esito del modello valido, 9 chiamate a tool (`get_deal` → `get_company` → 4 × `check_stock` → `get_customer` → `create_order` → `update_deal`), 31,6 s |
| ERP | Un solo ordine con `ExternalRef = D-1001`: `SO-2026-000001`, `IdempotencyKey = o2c-D-1001-r1`, 4 righe, totale 1.485,00, `Confirmed` |
| CRM | Deal D-1001 `OrderCreated`, `ErpOrderNumber = SO-2026-000001`, nota scritta dal modello |
| Idempotenza | Secondo run senza reset → exit code 0, stesso `SO-2026-000001` (`create_order` in 23 ms: ordine esistente restituito dall'ERP), sempre un solo ordine in `erp.Order`; 2 note sul deal (una per run) |
| Traccia | API di telemetria del dashboard: per ogni run una traccia unica (risorsa `orchestrator-cli`) con `o2c.process_deal` (`agent.name`, `correlation.id`, `o2c.outcome = OrderCreated`) e 9 span `tool.call` con `agent.name = SingleOrderAgent`, `tool.name` qualificato, `correlation.id`, `tool.outcome = ok` e durata; nella stessa traccia gli span `mcp.tool` di `erp-mcp`/`crm-mcp`, le richieste HTTP di `erp-api` (con lo stesso `correlation.id`) e le query SQL |
| Cambio di provider | Solo configurazione: Claude Haiku 4.5 via `ANTHROPIC_MODEL`, Qwen 3.5 9B via `MODEL_PROVIDER=ollama` (misure sopra); stesso codice |
| Segreti | Chiavi solo negli user-secrets dell'AppHost; nel repo nessun valore |

### Cosa è stato fatto
- `Orchestrator`: `ModelClientFactory` (Anthropic con modalità di thinking per modello, Ollama con thinking disattivato e contesto 16K), `McpToolCatalog`, `GuardedToolFunction` + `DealRunContext` + `ToolResultReader`, `SingleOrderAgent` (lavoro senza schema, poi esito strutturato, esito dai fatti), `DealProcessor` (correlation id e span radice), `OrchestratorCli` (`System.CommandLine`), `AppHostSecrets` per la CLI in Development.
- AppHost: parametro segreto `anthropic-api-key` → `ANTHROPIC_API_KEY` dell'orchestratore.
- `Shared`: attributi di telemetria `deal.id` e `o2c.outcome`.
- Pacchetti: `Microsoft.Agents.AI` 1.21.0, `Microsoft.Extensions.AI` 10.10.0, `Anthropic` 12.47.0, `OllamaSharp` 5.4.30, `ModelContextProtocol.Core` 2.2.0, `System.CommandLine` 2.0.12.
- Test: modello a copione (`ScriptedChatClient`), tool in memoria con le firme dei server MCP; marcatori `//SETUP`/`//SUT`.
- Documentazione: specifica (M10, M23), README (sezione "Agente (Fase 3)"), piano (D39–D43).

### Scostamenti e note
- **Esito strutturato dopo il lavoro**: con lo schema già nella prima richiesta Qwen non chiama i tool e inventa l'esito; l'agente fa prima il run di lavoro e poi chiede l'esito nella stessa sessione (vale per tutti i provider).
- **Esito dai fatti**: stato e numero d'ordine vengono dai risultati dei tool; un esito del modello non valido dopo il secondo tentativo non rende il deal `Failed` se ERP e CRM dicono il contrario (il piano diceva `Failed`).
- **Sampling e thinking**: nessuna `temperature` per Claude (Sonnet 5 la rifiuta; per uniformità nemmeno su Haiku); Haiku 4.5 usa `AnthropicThinkingMode.Extended` perché non supporta l'adaptive thinking, senza configurazione di thinking inviata.
- **Memoria con il modello locale**: 9–14 GB oltre alla VRAM; con AppHost e Visual Studio il sistema ha interrotto i processi in background. In pratica: modello locale e ambiente di sviluppo pesante non insieme su 31 GB.
- La CLI in Development invia la telemetria al dashboard HTTPS (`https://localhost:21058`, profilo di default dell'AppHost), che è quello avviato dall'Aspire CLI anche con `--launch-profile http`.
- Dopo le verifiche i dati demo vanno ripristinati con `POST /dev/reset` (lo strumento di misura lo fa a ogni run).
