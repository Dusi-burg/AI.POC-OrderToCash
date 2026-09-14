# Fase 4 — Multi-agente con Handoff + trigger RabbitMQ

> Indice: [plan.md](plan.md) · Precedente: [Fase 3](fase-3-agente-singolo.md) · Successiva: [Fase 5](fase-5-human-in-the-loop.md)

## Obiettivo

Sostituire l'agente singolo con i tre agenti di §5 (`IntakeAgent` → `FulfillmentAgent` → `OrderAgent`) collegati con Handoff orchestration, persistere lo stato del workflow e far partire il flusso dall'evento `deal-closed-won` su RabbitMQ (D6), mantenendo la CLI.

**Accettazione di §11**: i log/trace mostrano i due passaggi di handoff con il contesto trasferito; il risultato funzionale resta identico alla Fase 3.

## Prerequisiti

Fase 3 completata (factory del modello, provider MCP, `ToolInvocationGuard`).

## Gate di fase

| # | Domanda | Proposta |
|---|---------|----------|
| G4.1 | Chi scrive sul CRM gli esiti `Discarded`/`Failed`? `IntakeAgent` e `FulfillmentAgent` non hanno `update_deal` nella loro allow-list (§5) | Il **codice host** dell'orchestratore (deterministico, non un agente) chiama `update_deal` tramite il client MCP del CRM a fine workflow. Nessun agente usa tool fuori dalla propria riga |
| G4.2 | Dove vive il DbContext `orch`, che dalla Fase 5 serve anche ad `Approvals.Web`? | Nuovo progetto `src/Dusiburg.AI.O2C.Orchestration.Data` (DbContext + migrazioni) referenziato da Orchestrator e Approvals.Web (modifica M11) |
| G4.3 | Chi pubblica `deal-closed-won`? | L'endpoint dev `POST /dev/deals/{id}/close-won` del CRM mock pubblica direttamente sul broker (simula webhook CRM → ingestion) |
| G4.4 | Topologia RabbitMQ | Exchange durable `deal-closed-won` (topic), coda `o2c.orchestrator.deal-closed-won`, dead-letter exchange `o2c.dlx` con coda `o2c.orchestrator.deal-closed-won.dlq` |
| G4.5 | Concorrenza del consumer | `prefetch = 1` (elaborazione seriale: sufficiente e leggibile per la demo) |
| G4.6 | Libreria di messaggistica | `RabbitMQ.Client` 7.x diretto dietro `IDealEventSource` (niente MassTransit: licenza e peso non giustificati per il POC) |

## Spike S4 — Handoff orchestration (60 min)

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

- [ ] `POST /dev/deals/D-1001/close-won` → l'orchestratore elabora l'evento senza intervento manuale; risultato identico alla Fase 3 (ordine unico, deal `OrderCreated`).
- [ ] La traccia mostra Intake → Fulfillment → Order con **due span di handoff** e il contesto trasferito visibile (`WorkflowState.StateJson` e log).
- [ ] D-1006 → deal `Discarded` con nota; D-1007 → `Failed` con nota; nessun ordine creato.
- [ ] Un messaggio duplicato non crea un secondo workflow né un secondo ordine.
- [ ] La CLI continua a funzionare.
- [ ] DoD comune soddisfatta.

## Rischi

| Rischio | Mitigazione |
|---------|-------------|
| Handoff mancato o verso l'agente sbagliato | Topologia a soli archi in avanti, istruzioni esplicite, test con stub |
| Contesto perso fra agenti | `DealContext` dai tool, non dal modello |
| RabbitMQ non disponibile (WSL spenta) | Health check + messaggio chiaro in log; la CLI resta utilizzabile |

## Definition of Done

DoD comune di [plan.md](plan.md#definition-of-done-comune-a-ogni-fase).

## Esito

_Da compilare a fine fase (incluso l'esito dello spike S4)._
