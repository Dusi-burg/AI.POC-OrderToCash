# Panoramica del codice — Fase 5

> **Stato**: working copy del branch `develop` al 2026-09-16, Fase 5 completata ([fase-5-human-in-the-loop.md](plan/fase-5-human-in-the-loop.md)). Riferimenti: [plan.md](plan/plan.md) · specifica [architettura.md](architettura.md) · documenti precedenti [fase 3](panoramica-codice-fase3.md), [fase 4](panoramica-codice-fase4.md) · script della demo [demo.md](demo.md).
>
> Come per le fasi precedenti, qui si descrive **quello che il codice fa oggi**. Ciò che è solo predisposto per la Fase 7 (deploy; era la Fase 6 prima dell'inserimento delle UI) è indicato esplicitamente.

## 1. In sintesi

La Fase 5 ha reso il workflow **sospendibile**: `erp.create_order` non parte più senza una decisione umana quando la policy la richiede, e l'attesa non tiene niente in memoria.

- `erp.create_order` è dichiarato **`ApprovalRequiredAIFunction`**: il framework non lo invoca ed espone la chiamata su una **porta esterna** del workflow (D48). Il run si ferma in `PendingRequests` **senza aver toccato l'ERP**.
- A quella richiesta risponde **l'host**, non il modello: `ApprovalGate` valuta `ApprovalPolicy` e decide se approvare subito o sospendere (D49).
- La sospensione persiste **richiesta, stato e checkpoint**; il messaggio del broker viene confermato e il processo si libera.
- La decisione arriva da `Approvals.Web` e riprende il workflow **dal checkpoint, anche in un altro processo** (meccanismo A di D16).
- Due strade indipendenti verso la ripresa: il messaggio `approval-decided` come acceleratore e una **sweep di riconciliazione** periodica e all'avvio.

| Area | Fase 4 | Fase 5 |
|------|--------|--------|
| `create_order` | Invocato dall'agente | Tool con approvazione richiesta: lo invoca il framework solo dopo una risposta |
| Regole di §7 | Assenti | `ApprovalPolicy` deterministica, unica fonte; i prompt non contengono soglie |
| Stato | `orch.WorkflowState` | + `ApprovalRequest`, lookup `ApprovalStatus`/`ApprovalReason`, `WorkflowCheckpoint`, fasi `AwaitingApproval` e `Resuming` |
| Esiti | `OrderCreated` / `Discarded` / `Failed` | + `ApprovalPending`, `Rejected`, `Expired` |
| UI | Scaffolding | `/approvals`, `/approvals/{id}` e callback `POST /api/approvals/{id}/decision` |
| Messaggistica | `deal-closed-won` | + `approval-decided` (+ sweep di riconciliazione) |
| Telemetria | `agent.run`, `agent.handoff` | + `approval.requested`, `approval.decided`, `approval.resume`, `approval.expired` |
| Test | 169 | **200** (+25 in `Orchestrator.Tests`, +6 su `Approvals.Web`, +3 in `Erp.Api.Tests`) |

Resta **solo predisposto**: il canale Teams (l'endpoint di callback è già quello definitivo) e tutto ciò che è Azure (Fase 7).

## 2. Mappa della solution

| Progetto | Cosa cambia |
|----------|-------------|
| `Shared` | Nuovi contratti in `Contracts/Approvals/`: `ApprovalReason`, `ApprovalStatus`, `ApprovalPayload`, `ApprovalLine`, `ApprovalDecisionRequest/Response`, `ApprovalSummary`. In `Contracts/Messaging/`: `ApprovalDecided` e `ApprovalEventsTopology`. Nuovi attributi di telemetria `approval.*` |
| `Orchestration.Data` | `ApprovalRequest`, `WorkflowCheckpoint`, lookup dei due enum, fasi `AwaitingApproval` e `Resuming`; `ApprovalRepository` con le regole di transizione e il possesso della ripresa, condiviso con `Approvals.Web` |
| `Orchestrator` | Nuova cartella `Governance/` (`ApprovalPolicy`, `ApprovalGate`, `ApprovalStore`, `ApprovalSweepService`, `ApprovalSweepOptions`); in `Workflow/` `DealWorkflowEngine`, `ApprovalResumeRunner`, `SqlCheckpointStore`; in `Messaging/` `ApprovalDecidedConsumer` |
| `Approvals.Web` | Da scaffolding a servizio: pagine `/approvals`, servizio di decisione, publisher dietro `IApprovalDecisionPublisher`, endpoint di callback |
| `AppHost` | Inoltro delle manopole della demo ai due servizi (D52) |
| `DbInit` | Nessuna modifica: le tabelle nuove arrivano dal modello (D30) |

## 3. Schema `orch` dopo la Fase 5

```
orch.WorkflowState        Id, CorrelationId (univoco), DealId, DealRevision (univoco con DealId),
                          WorkflowPhaseId → WorkflowPhase, StateJson, CreatedAt, UpdatedAt, RowVersion
orch.WorkflowPhase        Id (tinyint), Name          -- 9 fasi: + AwaitingApproval, Resuming
orch.ApprovalRequest      Id, PublicId (GUID univoco), CorrelationId (indicizzato), DealId, DealRevision,
                          PayloadJson, ReasonsJson, Total, ApprovalStatusId → ApprovalStatus,
                          RequestedAt, DecidedAt, DecidedBy, DecisionNote, TraceParent, CheckpointId, RowVersion
                          -- indice (ApprovalStatusId, RequestedAt): elenco della UI e sweep di scadenza
orch.ApprovalStatus       Id (tinyint), Name          -- Pending, Approved, Rejected, Expired
orch.ApprovalReason       Id (tinyint), Name          -- OverThreshold, InsufficientStock, NewCustomer, BlockedCustomer
orch.WorkflowCheckpoint   Id (bigint = ordine di commit), SessionId, CheckpointId, ParentCheckpointId,
                          PayloadJson, CreatedAt      -- univoco (SessionId, CheckpointId)
```

`SessionId` del checkpoint **è** il correlation id del workflow: è il filo che tiene insieme evento, stato, richiesta e checkpoint.

## 4. La policy (`Governance/ApprovalPolicy.cs`)

Unica fonte delle regole di §7, deterministica e senza modello di mezzo:

| Motivo | Condizione |
|--------|-----------|
| `OverThreshold` | totale delle righe proposte **>** `APPROVAL_THRESHOLD_EUR` (confronto stretto: 10.000 € esatti passano) |
| `InsufficientStock` | almeno una riga la cui verifica di giacenza dà `available = false` |
| `NewCustomer` | l'anagrafica ERP è stata creata durante questo run |
| `BlockedCustomer` | il cliente risolto ha `isBlocked = true` |

Due scelte che contano:

- **Il totale lo calcola la policy** dalle righe proposte (`ApprovalContext.Total`), non lo legge da ciò che dice il modello (D17).
- **Le giacenze le verifica l'host**, non l'agente (D54): vedi sotto.

I motivi sono un **elenco**: D-1008 ne produce due (`OverThreshold` + `NewCustomer`).

### Perché la verifica di giacenza la fa l'host

La prima versione si fidava delle chiamate a `check_stock` fatte da `FulfillmentAgent`, con la regola "una riga senza verifica non è una prova di indisponibilità". Su D-1003, dal vivo, qwen ha passato la mano a `OrderAgent` annunciando il proprio compito invece di eseguirlo (`reason: "ready for stock check"`, zero verifiche registrate): la policy non ha trovato prove, l'ordine è passato **senza approvazione** e l'ERP ha riservato 5 pezzi di uno SKU che ne aveva 3.

Era un buco nel guardrail: bastava che il modello non facesse il proprio lavoro per scavalcare l'approvazione. C'era anche un secondo buco della stessa famiglia — `StockCheckDto` non riporta la quantità richiesta, quindi una verifica fatta con la quantità sbagliata sarebbe passata per buona.

Ora `ApprovalGate.EvaluateAsync` chiama `check_stock` da sé, **riga per riga e con le quantità proposte**, subito prima di valutare la policy, e sostituisce nel contesto le verifiche su cui si decide (`DealRunContext.ReplaceStock`). Le chiamate degli agenti restano visibili in `toolCalls` come traccia di ciò che hanno fatto. Una verifica che non riesce conta come riga **non disponibile**: l'esito peggiore è un'approvazione in più, mai un ordine in meno di controlli.

## 5. Intercettazione (`Governance/ApprovalGate.cs`, `DealWorkflowEngine`)

```
OrderAgent chiama create_order
   └─ FunctionInvokingChatClient non lo invoca: è ApprovalRequiredAIFunction
        └─ il workflow espone ExternalRequest su OrderAgent_..._UserInput
             └─ RequestInfoEvent → ApprovalGate.Evaluate(richiesta, contesto)
                  ├─ tool non sensibile  → risposta "approvato" immediata, il run prosegue
                  ├─ policy: non serve   → risposta "approvato" immediata, il run prosegue
                  └─ policy: serve        → nessuna risposta, il run si ferma
```

Il terzo caso è la sospensione: `DealWorkflowRunner.SuspendAsync` scrive `ApprovalRequest` (proposta congelata, motivi, `TraceParent`, `CheckpointId`), chiama `update_deal(ApprovalPending)` con i motivi e porta `WorkflowState` a `AwaitingApproval`.

Il secondo caso è quello che rende la Fase 5 invisibile sul percorso felice: D-1001 passa dalla porta di approvazione come tutti, ma l'host approva da sé e nessuno se ne accorge.

> **Perché anche i tool non sensibili passano da qui**: `FunctionInvokingChatClient` ha un comportamento tutto-o-niente e, nel turno in cui compare un tool con approvazione richiesta, converte in richieste di approvazione **tutte** le chiamate. Il framework offre `ApprovalNotRequiredFunctionBypassingChatClient` per nasconderlo; qui la cosa è gestita direttamente nella guardia, che è comunque il punto in cui la policy deve intervenire.

### Due comportamenti del framework da conoscere

1. **Lo stream degli eventi di un run con richieste pendenti non si chiude**: resta aperto in attesa della risposta. L'host smette di consumarlo al `SuperStepCompletedEvent` che segue la richiesta — il superstep è committato, quindi il checkpoint esiste — e solo allora esegue le proprie scritture. Senza questo, `ProcessAsync` non ritornerebbe mai.
2. **Gli effetti collaterali dell'host stanno fuori dal run**: il run si chiude (`await using`) prima di scrivere su CRM e database. Con il run ancora aperto, l'invocazione di un altro tool può non completarsi.

## 6. Ripresa (`Workflow/ApprovalResumeRunner.cs`)

```
ApprovalDecidedConsumer / ApprovalSweepService
   └─ ResumeAsync(approvalId)
        ├─ richiesta ancora Pending  → StillPending, niente
        ├─ possesso non ottenuto     → AlreadyResumed, niente          ← idempotenza
        ├─ Approved  → ricostruisce contesto e agenti, ResumeStreamingAsync dal checkpoint,
        │              risponde "approvato", create_order con la stessa idempotencyKey,
        │              update_deal(OrderCreated), WorkflowState = Completed
        └─ Rejected / Expired → nessun ordine: update_deal(Rejected|Expired) e WorkflowState = Completed
```

L'**idempotenza** non si appoggia al broker: è il passaggio da `AwaitingApproval` a `Resuming`, fatto con un UPDATE condizionale, a dire chi può proseguire.

```csharp
UPDATE orch.WorkflowState SET WorkflowPhaseId = Resuming
WHERE CorrelationId = @id AND (WorkflowPhaseId = AwaitingApproval OR (WorkflowPhaseId = Resuming AND UpdatedAt <= @stale))
```

Chi non tocca nessuna riga ha perso la corsa e non fa nulla. Una **lettura** della fase seguita da un'azione non bastava, e dal vivo si è visto: al riavvio dell'orchestratore il consumer di `approval-decided` e la sweep di riconciliazione partono insieme, hanno letto entrambi `AwaitingApproval` e hanno ripreso lo stesso workflow in parallelo. L'ordine è rimasto uno solo — l'indice univoco su `erp.Order.IdempotencyKey` ha fatto il suo lavoro, e nei log è comparsa l'eccezione di chiave duplicata che `OrderService` intercetta per restituire l'ordine esistente — ma gli effetti collaterali **non** idempotenti, cioè le note sul deal CRM, si sono duplicati (D56).

Il possesso ha una scadenza (`ResumeClaimTimeout`, almeno 5 minuti): se il processo muore durante la ripresa, la sweep recupera il workflow rimasto in `Resuming`. La soglia è generosa di proposito — attendere troppo costa un ritardo, attendere troppo poco costa un ordine annunciato due volte.

Il contesto del run si ricostruisce da `WorkflowState.StateJson` (`DealRunSnapshot`): il processo che riprende non ha visto le chiamate ai tool, ma la guardia ha bisogno degli stessi fatti — in particolare della revisione del deal, da cui nasce la chiave di idempotenza.

Lo span `approval.resume` ha come parent il `TraceParent` salvato alla sospensione: anche dopo ore, la ripresa sta nella stessa traccia (G5.4).

## 7. Sweep (`Governance/ApprovalSweepService.cs`)

Un solo hosted service, due compiti, all'avvio e ogni `APPROVAL_SWEEP_MINUTES`:

- **Scadenza**: richieste `Pending` più vecchie di `APPROVAL_TIMEOUT_HOURS` → `Expired` sotto `RowVersion`. Se nel frattempo qualcuno ha deciso, la transizione non si applica e la decisione vince.
- **Riconciliazione**: richieste decise con il workflow ancora in `AwaitingApproval` → ripresa. È ciò che rende il messaggio `approval-decided` un acceleratore e non un punto di rottura (G5.3): niente outbox transazionale.

## 8. `Approvals.Web`

| Pezzo | Cosa fa |
|-------|---------|
| `/approvals` | Coda delle pendenti (`?all=true` per tutte): deal, azienda, totale, motivi, stato |
| `/approvals/{id}` | Proposta leggibile (righe, giacenze, totale), contesto (cliente, motivi, correlation id, chiave di idempotenza, link al deal), azioni Approva/Rifiuta con nota |
| `POST /api/approvals/{id}/decision` | Callback unico, usato dalla UI e in futuro da Teams. `200` con l'esito, `404` se la richiesta non esiste, **`409` se era già decisa** |
| `ApprovalDecisionService` | Transizione sotto `RowVersion`, poi pubblicazione di `approval-decided`. Se la pubblicazione fallisce, la decisione resta scritta e la riconciliazione fa il resto |
| `IApprovalDecisionPublisher` | RabbitMQ dietro interfaccia, come `IDealEventSource` nell'orchestratore: in Fase 7 cambia il broker, e i test non ne hanno bisogno |

Razor Pages senza librerie JS (G5.7). L'approvatore viene da `Approvals:ApproverUpn` (D25).

## 9. Telemetria

| Span | Dove | Attributi |
|------|------|-----------|
| `approval.requested` | Orchestrator, alla sospensione | `approval.id`, `approval.reasons`, `deal.id`, `correlation.id` |
| `approval.decided` | Approvals.Web, alla decisione | `approval.id`, `approval.decision`, `approval.decided_by` |
| `approval.resume` | Orchestrator, alla ripresa | come sopra, **con parent dal `TraceParent` salvato** |
| `approval.expired` | Orchestrator, alla scadenza | `approval.id`, `deal.id`, `correlation.id` |

## 10. Test

| Area | Casi |
|------|------|
| `ApprovalPolicyTests` (10) | Ogni regola da sola, il confine esatto della soglia (10.000 € non scatta), soglia da configurazione, combinazioni, totale calcolato dalle righe, riga senza verifica |
| `ApprovalWorkflowTests` (10) | Sospensione senza chiamare l'ERP; approvazione con doppia consegna e sweep concorrente → **un solo ordine**; ripresa su istanza nuova; rifiuto; scadenza; scadenza su richiesta già decisa; motivi `BlockedCustomer` e `InsufficientStock`+`NewCustomer` |
| `ApprovalsWebTests` (6) | Decisione registrata e pubblicata; **seconda decisione → 409**; richiesta inesistente → 404; coda e dettaglio |

I test del workflow usano un modello a copione e tool in memoria: quello che si verifica è il comportamento dell'host, non la bravura del modello. Il doppio dei tool restituisce `JsonElement`, come fanno i tool MCP veri.

## 11. Limiti noti e cosa manca

- **Cliente nuovo e rifiuto**: l'anagrafica creata da `create_customer` prima della sospensione **resta in ERP** anche se l'ordine viene rifiutato (G5.2). Conforme a §13, dove solo `create_order` è sensibile.
- **`Reserved` può superare `OnHand`** (D55): l'ordine in backorder è accettato e lo stock riservato per intero, perché è la domanda impegnata che un ERP completo userebbe per riordinare. Lo stato resta leggibile grazie a `backorderNote`, che dice cosa manca e in che quantità e finisce come nota sul deal CRM; l'invariante `Reserved ≤ OnHand` non vale e non è imposta da alcun constraint.
- **Reset della demo**: `POST /dev/reset` su ERP e CRM non tocca lo schema `orch`. Per ripartire da zero serve `DbInit`.
- **Checkpoint non potati**: si accumula un checkpoint per superstep (~35 righe per otto deal, payload fino a ~56 KB). Nel POC va bene; in produzione servirebbe una politica di ritenzione.
- **Regole "non chiedere più"**: fuori scope, quindi `ToolApprovalAgent` non è usato.
- **Teams**: solo la pagina web; l'endpoint di callback è già quello che userebbe l'Adaptive Card.
