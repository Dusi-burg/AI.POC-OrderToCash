# Fase 4 — Multi-agente con Handoff + trigger RabbitMQ

> Indice: [plan.md](plan.md) · Precedente: [Fase 3](fase-3-agente-singolo.md) · Successiva: [Fase 5](fase-5-human-in-the-loop.md)

## Obiettivo

Sostituire l'agente singolo con i tre agenti di §5 (`IntakeAgent` → `FulfillmentAgent` → `OrderAgent`) collegati con Handoff orchestration, persistere lo stato del workflow e far partire il flusso dall'evento `deal-closed-won` su RabbitMQ (D6), mantenendo la CLI.

**Accettazione di §11**: i log/trace mostrano i due passaggi di handoff con il contesto trasferito; il risultato funzionale resta identico alla Fase 3.

## Prerequisiti

Fase 3 completata (factory del modello, provider MCP, `ToolInvocationGuard`).

## Gate di fase

> Chiuso il 2026-09-15 (decisioni D44–D47 in [plan.md](plan.md)).

| # | Domanda | Decisione |
|---|---------|-----------|
| G4.0 | Handoff del framework o orchestrazione dal codice? *(emersa dallo spike: l'handoff è pensato per conversazioni, serve la modalità autonoma)* | **Deciso dall'utente (D44)**: handoff di Agent Framework con modalità autonoma e condizione di terminazione; esiti e scritture `Discarded`/`Failed` restano all'host |
| G4.1 | Chi scrive sul CRM gli esiti `Discarded`/`Failed`? | **Proposta accettata (D44)**: il codice host, via client MCP del CRM, a fine workflow |
| G4.2 | Dove vive il DbContext `orch`? | **Deciso dall'utente (D45)**: `src/Dusiburg.AI.O2C.Orchestration.Data`, **senza migrazioni** (schema da `DbInit`, D30), `WorkflowState` con PK `Id`, `CorrelationId` univoco, fase come lookup `WorkflowPhase` (D29–D31) |
| G4.3 | Chi pubblica `deal-closed-won`? | **Proposta accettata (D46)**: l'endpoint dev `close-won` del CRM mock |
| G4.4 | Topologia RabbitMQ | **Proposta accettata**: exchange durable `deal-closed-won` (topic), coda `o2c.orchestrator.deal-closed-won`, DLX `o2c.dlx` con coda `.dlq` |
| G4.5 | Concorrenza del consumer | **Proposta accettata**: `prefetch = 1` |
| G4.6 | Libreria di messaggistica | **Deciso dall'utente (D46)**: `Aspire.RabbitMQ.Client` 13.5.3 (sopra `RabbitMQ.Client` 7.x) invece del client diretto |
| G4.7 | Agente singolo della Fase 3 | **Deciso dall'utente (D47)**: tenuto dietro `O2C_AGENT_MODE=single\|multi` (default `multi`) |

## Spike S4 — Handoff orchestration (2026-09-15)

Documentazione di `Microsoft.Agents.AI.Workflows` 1.21.0 (verifica eseguibile in corso):
- `AgentWorkflowBuilder.CreateHandoffBuilderWith(agenteIniziale).WithHandoff(da, a, motivo).Build()`; esecuzione con `InProcessExecution.RunStreamingAsync(workflow, messaggi)` e `TurnToken`.
- Tool di trasferimento aggiunti dal framework con nome `{FunctionPrefix}<id agente>`: non passano dalla guardia dei tool MCP.
- Contesto trasferito: la conversazione intera; per default (`HandoffToolCallFilteringBehavior.HandoffOnly`) si tolgono solo le chiamate di handoff.
- Un agente che risponde senza handoff restituisce il controllo al chiamante: per un flusso senza utente servono `WithAutonomousMode` (continuazione fino a un limite di turni) e `WithTerminationCondition`.
- **Nessun evento dedicato all'handoff**: i passaggi si ricavano dalle chiamate ai tool di trasferimento negli eventi degli agenti.
- `WithOpenTelemetry` esiste su `WorkflowBuilder`; da verificare con il builder di handoff.

Verifica eseguibile (spike usa e getta, Claude Haiku 4.5, server MCP reali, tre `ChatClientAgent` con `Id` espliciti, `WithAutonomousMode()` e terminazione sul prefisso `DISCARDED:` / `FAILED:` / `ORDER_CREATED:` dell'ultima risposta):

| Verifica | Esito |
|----------|-------|
| D-1001 | ✅ Intake (`get_deal`, `get_company`) → Fulfillment (4 × `check_stock`) → Order (`get_customer`, `create_order`, `update_deal`), `ORDER_CREATED: SO-2026-000001`, CRM `OrderCreated`, **21 s**; 18 messaggi con `AuthorName` dell'agente; `WorkflowOutputEvent` con `List<ChatMessage>` |
| Nome dei tool di trasferimento | ⚠️ **`handoff_to_1`** (indice del target nell'agente), non `handoff_to_<id agente>` come nella documentazione; argomento `reasonForHandoff` scritto dal modello (utile per lo span `agent.handoff`) |
| Executor | Id `IntakeAgent_IntakeAgent`, …; eventi `ExecutorInvokedEvent`/`ExecutorCompletedEvent`, `SuperStepStarted/Completed`, `AgentResponseUpdateEvent` |
| D-1006 (USD) | ❌ **Loop autonomo**: Intake legge il deal, la condizione di terminazione non scatta e la modalità autonoma lo rilancia **104 volte** (≈100 chiamate al modello, 45 s) fino a "Ready."; nessun `Discarded`. Il limite di turni di default è troppo alto: va impostato basso e la terminazione non può dipendere solo dal formato del testo |
| D-1006 con `WithAutonomousMode(3)` | Intake valuta correttamente (USD invece di EUR) ma risponde con un riepilogo markdown invece di `DISCARDED:`; le continuazioni automatiche ("User did not respond. Continue assisting autonomously.") ripetono il riepilogo; il limite ferma il workflow in 11 s. Nessun `Discarded` scritto (atteso: nello spike nessuno lo scrive) |

**Scelte di progetto derivate dallo spike** (da applicare nell'implementazione):
1. **Terminazione dai fatti, non dal testo**: gli agenti che possono fermare il flusso hanno un tool **locale dell'host** (non MCP, fuori dall'allow-list dei server) che registra il verdetto nel `DealRunContext` — `report_discarded(reason)` per Intake, `report_failed(reason)` per Fulfillment; la condizione di terminazione guarda il contesto (verdetto registrato, oppure ordine creato e deal aggiornato), non il prefisso del testo.
2. **Limite di turni autonomi basso** (3) su tutti gli agenti, per contenere costi e loop.
3. **Esiti finali dall'host** come in Fase 3: `Discarded` e `Failed` verificati sui fatti (valuta dal `get_deal`, SKU inesistente dal `check_stock`) e scritti con `update_deal` dal codice (G4.1); `OrderCreated` da ordine creato + deal aggiornato.
4. **Span `agent.handoff`** ricavati dalle chiamate `handoff_to_*` (con `handoff.from`, `handoff.to`, `handoff.reason` da `reasonForHandoff`); target ricavato dall'executor successivo.
5. Agenti con `Id` e `Name` espliciti (`IntakeAgent`, …) per executor e autori dei messaggi leggibili.

**Disegno dell'implementazione** (2026-09-15, blocchi 1–2):
- `src/Dusiburg.AI.O2C.Orchestration.Data`: `OrchestrationDbContext` (schema `orch`), `WorkflowState` (PK `Id`, `CorrelationId` univoco, `(DealId, DealRevision)` univoco, `WorkflowPhaseId` → lookup `WorkflowPhase`, `StateJson`, `RowVersion`); tabelle create da `DbInit` insieme a `erp` e `crm` (verificato).
- `Agents/WorkflowAgents.cs`: definizioni dei tre agenti (istruzioni in costanti invece di file `.md` incorporati), allow-list, tool locali `report_discarded` (Intake) e `report_failed` (Fulfillment) che registrano il verdetto nel `DealRunContext`.
- `Workflow/DealWorkflowRunner.cs` (`IDealAgent`, modalità `multi`): legge il deal per la revisione, `WorkflowStateStore.TryStartAsync` (evento: duplicato ignorato; CLI: rielaborazione sulla stessa riga), handoff builder con `WithAutonomousMode(3)` e terminazione su `DealRunContext.IsTerminal`, span `agent.run` / `agent.handoff` dagli eventi `AgentResponseUpdateEvent`, fase aggiornata a ogni cambio di agente, esito dai fatti (Discarded solo se confermato dalle regole di intake sui dati del deal), `update_deal` di `Discarded`/`Failed` scritto dall'host con la guardia (ambito `Host`).
- `GuardedToolFunction` con `AgentScope` per agente; `DealRunContext` con verdetto, handoff e `ToStateJson()`.
- `DealProcessor` sceglie `O2C_AGENT_MODE` (`multi` default | `single`), accetta correlation id, revisione e contesto di traccia dell'evento.

## Spike S4 — Handoff orchestration (piano originale)

Verificare sulla versione corrente di `Microsoft.Agents.AI.Workflows`:
- costruzione del workflow di handoff con archi diretti (Intake → Fulfillment → Order) e assenza di archi di ritorno;
- tool di trasferimento iniettati dal framework: nomi, come compaiono al modello, come interagiscono con il `ToolInvocationGuard`;
- esecuzione in-process e stream di eventi (aggiornamenti dell'agente, handoff, output finale);
- cosa viene trasferito all'agente successivo (storico della conversazione completo o parziale);
- strumentazione OpenTelemetry del workflow.

## Step operativi

**4.1 — Catalogo agenti** (`src/Dusiburg.AI.O2C.Orchestrator/Agents/`)
- `AgentDefinition { Name, Instructions, AllowedTools, HandoffTo }`; istruzioni in file `Prompts/*.md` incorporati come risorse.

| Agente | Allow-list | Compito | Esito verso l'host |
|--------|-----------|---------|--------------------|
| `IntakeAgent` | `crm.get_deal`, `crm.get_company` | Stage `ClosedWon`, valuta EUR (D22), righe non vuote, importo = somma righe | Handoff a Fulfillment, oppure `Discarded` con motivo |
| `FulfillmentAgent` | `erp.check_stock` | Verifica ogni riga → `Fulfillable` \| `Partial` \| `Blocked` (SKU inesistente, D20) | Handoff a Order, oppure `Failed` se `Blocked` |
| `OrderAgent` | `erp.get_customer`, `erp.create_customer`, `erp.create_order`, `crm.update_deal` | Cliente, ordine, aggiornamento CRM | Terminale: `OrderCreated` |

**4.2 — `DealContext` come contesto trasferito**
- Popolato dal `ToolInvocationGuard` con gli output reali dei tool (Fase 3); a ogni handoff viene serializzato in un messaggio strutturato per l'agente successivo e salvato in `WorkflowState.StateJson`. Il modello riceve i fatti, non li riassume.

**4.3 — Workflow**
- `DealWorkflowFactory` costruisce il workflow di handoff (secondo lo spike S4) con i tre agenti.
- `DealWorkflowRunner`: esegue il workflow per `(dealId, correlationId)`, osserva gli eventi, aggiorna `WorkflowState` a ogni handoff, a fine run esegue gli esiti deterministici (G4.1) e restituisce l'`OrderOutcome`.
- `SingleOrderAgent` della Fase 3 rimosso (o lasciato dietro flag solo per confronto, da decidere in esecuzione).

**4.4 — Stato del workflow** (`src/Dusiburg.AI.O2C.Orchestration.Data`, secondo G4.2)
- `OrchestrationDbContext` schema `orch`, tabella `WorkflowState` (§8): `CorrelationId` PK, `DealId`, `DealRevision`, `Phase` (`Received`, `Intake`, `Fulfillment`, `Order`, `Completed`, `Discarded`, `Failed`; `AwaitingApproval` in Fase 5), `StateJson`, `UpdatedAt`, `RowVersion`.
- Indice univoco `(DealId, DealRevision)`: stesso evento ricevuto due volte → il secondo non avvia un nuovo workflow (consumer idempotente).
- Migrazione `InitialOrch`.

**4.5 — Trigger RabbitMQ**
- Messaggio `DealClosedWon { dealId, revision, occurredAt }`, `message-id = {dealId}:{revision}`, header `x-correlation-id` e `traceparent` (W3C) per collegare la traccia del publisher.
- Publisher in `Crm.Mcp` (G4.3): l'endpoint dev `close-won` aggiorna lo stage e pubblica. Il `CorrelationId` del workflow nasce qui.
- Consumer `DealClosedWonConsumer` (`BackgroundService`, implementazione di `IDealEventSource`) nell'Orchestrator: dichiarazione idempotente della topologia (G4.4), `prefetch = 1` (G4.5), **ack manuale solo dopo che lo stato è persistito**; errore transitorio → nack con riaccodamento fino a 3 tentativi (header contatore), poi dead-letter.
- AppHost: connection string `rabbitmq` a `Crm.Mcp` e `Orchestrator`; health check RabbitMQ (R4).
- La CLI `process --deal` resta disponibile e usa lo stesso `DealWorkflowRunner`.

**4.6 — Tracing**
- Span radice `deal-closed-won process` con link/parent al `traceparent` del messaggio; span per ogni turno di agente (`agent.run` con `agent.name`) e per ogni handoff (`agent.handoff` con `handoff.from`, `handoff.to`); gli span `tool.call` restano quelli della Fase 3.

### Test — `tests/Dusiburg.AI.O2C.Orchestrator.Tests`

**4.7 — Casi** (stub del modello con copione di handoff)
- Topologia: sono possibili solo gli archi Intake → Fulfillment → Order.
- Allow-list: se Intake tenta `create_order` → `UNAUTHORIZED`, nessuna chiamata al server.
- `WorkflowState` aggiornato a ogni transizione; `StateJson` contiene i fatti dei tool.
- Esiti deterministici: D-1006 → `update_deal(Discarded)` eseguito dall'host; D-1007 → `Failed`.
- Consumer: stesso messaggio due volte → un solo workflow; eccezione transitoria → nack e riaccodamento; oltre 3 tentativi → dead-letter.

## Criteri di accettazione

- [x] `POST /dev/deals/D-1001/close-won` → l'orchestratore elabora l'evento senza intervento manuale; risultato identico alla Fase 3 (ordine unico, deal `OrderCreated`).
- [x] La traccia mostra Intake → Fulfillment → Order con **due span di handoff** e il contesto trasferito visibile (`WorkflowState.StateJson` e log).
- [x] D-1006 → deal `Discarded` con nota; D-1007 → `Failed` con nota; nessun ordine creato.
- [x] Un messaggio duplicato non crea un secondo workflow né un secondo ordine.
- [x] La CLI continua a funzionare.
- [x] DoD comune soddisfatta (commit lasciato all'utente).

## Rischi

| Rischio | Mitigazione |
|---------|-------------|
| Handoff mancato o verso l'agente sbagliato | Topologia a soli archi in avanti, istruzioni esplicite, test con stub |
| Contesto perso fra agenti | `DealContext` dai tool, non dal modello |
| RabbitMQ non disponibile (WSL spenta) | Health check + messaggio chiaro in log; la CLI resta utilizzabile |

## Definition of Done

DoD comune di [plan.md](plan.md#definition-of-done-comune-a-ogni-fase).

## Esito

**Completata il 2026-09-15** — branch `develop`, modifiche non committate. Spike S4, scelte derivate e disegno nelle sezioni sopra.

### Verifiche
| Criterio | Evidenza |
|----------|----------|
| Build e test | `dotnet build Dusiburg.AI.O2C.slnx` → 0 avvisi, 0 errori; `dotnet test` → **169/169** (151 in Fase 3): +18 in `Orchestrator.Tests` (workflow con modello a copione per agente, regole di intake, topologia, header dei messaggi, modalità) |
| Database | `DbInit` crea anche lo schema `orch`: `WorkflowState` (indici univoci su `CorrelationId` e `(DealId, DealRevision)`) e lookup `WorkflowPhase` con 7 fasi |
| Evento D-1001 | AppHost avviato (worker con consumer RabbitMQ, Claude Haiku 4.5): `POST /dev/deals/D-1001/close-won` → workflow `Completed` in 22 s senza interventi, un solo ordine `SO-2026-000001`, deal `OrderCreated` con nota |
| Duplicato | Secondo `close-won` su D-1001 → log "Evento D-1001:1 duplicato: confermato senza nuovo workflow", `WorkflowState` invariato (stesso `UpdatedAt`), sempre 1 ordine e 1 nota |
| D-1006 / D-1007 | `Discarded` in 8 s (nota dell'host "IntakeAgent: Currency is USD, required EUR"), `Failed` in 12 s dopo un handoff ("FulfillmentAgent: SKU IND-SEN-999 not found in ERP"); nessun ordine |
| Code | `rabbitmqctl list_queues -p o2c`: `o2c.orchestrator.deal-closed-won` 0 messaggi / 1 consumer, `.dlq` 0 messaggi |
| Traccia | API del dashboard: `o2c.process_deal` figlio dello span `publish deal.closed-won` del CRM; per D-1001 `agent.run` di Intake, Fulfillment e Order, **due `agent.handoff`** (`IntakeAgent → FulfillmentAgent`, `FulfillmentAgent → OrderAgent`) con `correlation.id` e `handoff.reason` scritto dal modello, `tool.call` con l'agente che li ha eseguiti; D-1007 un handoff, D-1006 nessuno, duplicato con `o2c.outcome = duplicate` |
| Contesto trasferito | `WorkflowState.StateJson` con deal, azienda, giacenze, cliente, ordine, verdetto, handoff e chiamate per agente; fase aggiornata a ogni cambio di agente |
| CLI | `process --deal D-1001` dopo gli eventi → exit 0, `OrderCreated`, stesso ordine, 2 handoff; sempre 1 ordine e 1 riga di workflow (la CLI rielabora sulla stessa riga) |
| CLI prima di RabbitMQ | Stessi tre deal da CLI con la build del blocco 2: D-1001 21 s, D-1006 9 s, D-1007 14 s, esiti identici |

### Cosa è stato fatto
- `src/Dusiburg.AI.O2C.Orchestration.Data` (D45) e schema `orch` in `DbInit`.
- Orchestrator: `WorkflowAgents` (tre agenti, allow-list, tool locali `report_discarded` / `report_failed`), `DealWorkflowRunner` (handoff builder, `WithAutonomousMode(3)`, terminazione sui fatti, span `agent.run` / `agent.handoff`, esiti e scritture `Discarded`/`Failed` dall'host), `WorkflowStateStore`, `AgentModes` (`O2C_AGENT_MODE`, D47), `DealProcessor` con correlation id, revisione e traccia dell'evento, `DealClosedWonConsumer` (topologia, `prefetch = 1`, ack dopo l'elaborazione, 3 nuovi tentativi con header `x-retry-count`, poi dead-letter); `GuardedToolFunction` con `AgentScope` per agente.
- Crm.Mcp: `DealEventPublisher` (`message-id = {dealId}:{revision}`, `x-correlation-id`, `traceparent`) chiamato da `close-won`.
- `Shared`: `DealClosedWon`, `DealEventsTopology`, attributi `handoff.from` / `handoff.to` / `handoff.reason`.
- Pacchetti: `Microsoft.Agents.AI.Workflows` 1.21.0, `Aspire.RabbitMQ.Client` 13.5.3.

### Scostamenti e note
- **Terminazione e verdetti** (spike S4): la fine del workflow dipende dai fatti del contesto, non dal testo; `report_discarded` / `report_failed` sono tool locali dell'host. Un `Discarded` dichiarato dal modello vale solo se confermato dalle regole di intake sui dati del deal, altrimenti `Failed`.
- **Nomi dei tool di trasferimento** `handoff_to_1` (non `handoff_to_<id>` come in documentazione); nessun evento di handoff dal framework.
- **Istruzioni in costanti C#** invece di file `.md` incorporati (4.1).
- **Riaccodamento**: invece del nack con riaccodamento si ripubblica una copia con `x-retry-count` e si conferma l'originale (gli header non si modificano con il nack); oltre 3 tentativi `reject` senza riaccodamento → dead-letter. Non provato dal vivo (nessun errore transitorio durante le prove).
- **`IDealEventSource`** è un'interfaccia marcatore implementata dal consumer; il cambio verso Service Bus (Fase 7, deploy — era la Fase 6 prima di D57) resta da progettare.
- **Ripetere la demo**: `POST /dev/reset` su ERP e CRM non pulisce `orch.WorkflowState`; senza pulizia un nuovo `close-won` sulla stessa revisione viene considerato duplicato. Per ripartire: `DbInit` oppure cancellare le righe di `orch.WorkflowState`.
- Il publisher dichiara solo l'exchange: se il consumer non ha ancora dichiarato la coda, un evento pubblicato prima del suo avvio va perso (per le prove si attende il log "In ascolto su …").
