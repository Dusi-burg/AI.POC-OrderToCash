# Fase 5 — Human-in-the-loop

> Indice: [plan.md](plan.md) · Precedente: [Fase 4](fase-4-multi-agente.md) · Successiva: [Fase 6](fase-6-deploy-osservabilita.md)

## Obiettivo

Intercettare `erp.create_order` con la policy di approvazione di §7, sospendere il workflow persistendo lo stato (nessuna attesa in memoria), raccogliere la decisione dalla pagina `/approvals` e riprendere il workflow da dove era, gestendo rifiuto e scadenza.

**Accettazione di §11**: un deal sopra soglia genera una richiesta pendente e **nessun** ordine; dopo approvazione l'ordine viene creato una sola volta; dopo rifiuto nessun ordine e il deal CRM riporta lo stato di rifiuto; un riavvio del processo durante l'attesa non perde il workflow.

## Prerequisiti

Fase 4 completata (workflow multi-agente, `WorkflowState`, consumer RabbitMQ).

## Gate di fase

| # | Domanda | Proposta |
|---|---------|----------|
| G5.1 | Meccanismo di ripresa: (A) checkpoint nativo o (B) resume deterministico? | Decisione sull'esito dello spike S5 (D16); (B) è già progettato come ripiego |
| G5.2 | Cliente nuovo + rifiuto: l'anagrafica creata da `create_customer` prima di `create_order` resta in ERP | Accettato (conforme a §13: solo `create_order` è sensibile); la policy riconosce il cliente nuovo dal `DealContext` (`CustomerCreatedInThisRun`); documentato nel README |
| G5.3 | Consegna della decisione all'orchestratore | Messaggio `approval-decided` su RabbitMQ come acceleratore + **sweep di riconciliazione** periodica e all'avvio (richieste decise con workflow ancora in `AwaitingApproval`): niente outbox transazionale |
| G5.4 | Una sola traccia attraverso un'attesa di ore | Colonna `TraceParent` su `ApprovalRequests`; la ripresa usa quel contesto come parent → stesso trace id (modifica M12) |
| G5.5 | Timeout in demo | `APPROVAL_TIMEOUT_HOURS` accetta decimali (es. `0.02` ≈ 1 minuto) |
| G5.6 | "Notifica al richiedente" alla scadenza | `update_deal(Expired)` con nota + log strutturato (D24) |
| G5.7 | Tecnologia della UI | Razor Pages, nessuna libreria JS |

## Spike S5 — approvazione e ripresa (mezza giornata, time-box)

**a) Meccanismo di approvazione**
- Esiste `ToolApprovalAgent` nella versione GA di Agent Framework, con quale semantica? In alternativa: `ApprovalRequiredAIFunction` sul tool e richiesta di approvazione restituita dal run (contenuto di tipo function-approval request / user-input request) a cui si risponde in un run successivo.
- Come si manifesta la richiesta dentro un workflow di handoff (evento di richiesta esterna?).

**b) Persistenza e ripresa**
- Checkpointing del workflow con uno store personalizzato su SQL (`orch.WorkflowCheckpoints`); ripresa da checkpoint in un **nuovo processo** con la risposta all'approvazione.

**Esito** → si sceglie (A) o (B), si registra la decisione in [plan.md](plan.md) e si aggiorna la specifica (M8: nome e semantica del meccanismo in §7/§13).

## Step operativi

**5.1 — `ApprovalPolicy`** (`src/Dusiburg.AI.O2C.Orchestrator/Governance/`)
- Input: righe proposte (il totale lo calcola la policy, non il modello), `FulfillmentAssessment`, dati cliente (`IsBlocked`, `CustomerCreatedInThisRun`), soglia `APPROVAL_THRESHOLD_EUR` (default 10000).
- Output: `ApprovalDecision { Required, Reasons[] }` con `ApprovalReason` = `OverThreshold` (totale **>** soglia) · `InsufficientStock` · `NewCustomer` · `BlockedCustomer`.
- Unica fonte delle regole (§12 "policy esplicita e centralizzata"); i prompt non contengono soglie.

**5.2 — Intercettazione di `create_order`** (nel `ToolInvocationGuard`, con il meccanismo scelto nello spike)
- Se `Required`: **nessuna chiamata all'ERP**; in una transazione su `orch`: `ApprovalRequest` (`Pending`, `PayloadJson` = proposta completa con cliente, righe, totale, motivi, `idempotencyKey`; `TraceParent`) + `WorkflowState.Phase = AwaitingApproval` + checkpoint (A) o proposta (B).
- L'host chiama `update_deal(ApprovalPending)` con i motivi; il run termina; il messaggio viene confermato (ack). Nulla resta in memoria.
- `IsBlocked`: le istruzioni di `OrderAgent` chiedono di proporre comunque l'ordine (l'approvazione è l'unica strada, §7).

**5.3 — Tabella `ApprovalRequests`** (`src/Dusiburg.AI.O2C.Orchestration.Data`)
- Campi di §7 + `RowVersion`, `TraceParent`, `Reasons` (JSON); indice `(Status, RequestedAt)`; migrazione `AddApprovals`.

**5.4 — `src/Dusiburg.AI.O2C.Approvals.Web`**
- Pagine: `/approvals` (pendenti, filtro "tutte"), `/approvals/{id}` (payload leggibile: cliente, righe, totale, motivi, link al deal dev).
- Azioni Approva/Rifiuta con nota facoltativa; `DecidedBy` dall'approvatore configurato (`Approvals:ApproverUpn`, D25).
- Endpoint di callback unico `POST /api/approvals/{id}/decision` `{ decision, note }`, usato dalla UI e in futuro da Teams.
- Transizione ammessa solo da `Pending`; concorrenza ottimistica con `RowVersion` → seconda decisione = 409.
- Dopo il salvataggio pubblica `ApprovalDecided { approvalId, correlationId, decision }` su exchange `approval-decided` (coda `o2c.orchestrator.approval-decided`).
- AppHost: `sql` e `rabbitmq` ad Approvals.Web; porta 5104.

**5.5 — Ripresa**
- `ApprovalDecidedConsumer` + `ApprovalReconciliationService` (all'avvio e ogni N minuti, G5.3).
- Se il workflow è già `Completed` → nessuna azione (ripresa idempotente).
- **Approved**: (A) ripristino dal checkpoint con risposta di approvazione, oppure (B) esecuzione della proposta approvata; `create_order` con la stessa `idempotencyKey` → un solo ordine; `update_deal(OrderCreated)`; `WorkflowState = Completed`.
- **Rejected**: nessun ordine; `update_deal(Rejected)` con la nota del decisore; `WorkflowState = Completed`.
- Span `approval.resume` con parent dal `TraceParent` salvato (G5.4).

**5.6 — Scadenza** (`ApprovalExpiryService` nell'Orchestrator)
- Ogni 5 minuti (configurabile): richieste `Pending` oltre `APPROVAL_TIMEOUT_HOURS` → `Expired` (con `RowVersion`, per non collidere con una decisione concorrente); nessun ordine; `update_deal(Expired)` + log (G5.6).

**5.7 — Tracing e audit**
- Span `approval.requested` (motivi) e `approval.decided` (esito, decisore); tutte le righe di log con `correlation.id`.

**5.8 — README di demo** (D5)
- Script passo-passo per D-1001…D-1008 con esiti attesi, reset dei dati, dove guardare nel dashboard.

### Test — `tests/Dusiburg.AI.O2C.Orchestrator.Tests`

**5.9 — Casi**
- `ApprovalPolicy`: ogni regola da sola, combinazioni, confine esatto 10.000 € (non scatta), soglia da configurazione.
- Intercettazione: con `Required` nessuna chiamata a `create_order` verso il server, `ApprovalRequest` e `WorkflowState` persistiti, deal `ApprovalPending`.
- Approve → un solo ordine anche con doppio messaggio `approval-decided` e sweep concorrente.
- Reject → nessun ordine, deal `Rejected`.
- Scadenza → `Expired`, nessun ordine, deal `Expired`.
- Doppia decisione → 409 (test di `Approvals.Web`).
- **Riavvio**: host A crea la richiesta e viene fermato; host B (nuova istanza, stesso DB) riceve l'approvazione e completa.

## Criteri di accettazione

- [ ] D-1002 (sopra soglia) → richiesta `Pending` visibile in `/approvals`, **nessun** ordine in ERP, deal `ApprovalPending`.
- [ ] Approvazione → ordine creato **una sola volta**, deal `OrderCreated`.
- [ ] Rifiuto → nessun ordine, deal `Rejected` con nota.
- [ ] Orchestrator fermato durante l'attesa e riavviato → l'approvazione successiva completa il workflow.
- [ ] Scadenza (timeout di demo) → `Expired`, nessun ordine, deal `Expired`.
- [ ] D-1003, D-1004, D-1005, D-1008 generano approvazioni con i motivi corretti; D-1001 passa senza approvazione.
- [ ] Nel dashboard la catena trigger → agenti → tool → richiesta → decisione → ripresa → `create_order` sta in **un'unica traccia**.
- [ ] DoD comune soddisfatta.

## Rischi

| Rischio | Mitigazione |
|---------|-------------|
| Checkpoint nativo non ripristinabile in un nuovo processo | Fallback (B) già progettato; spike a tempo definito |
| Decisione salvata ma messaggio perso | Sweep di riconciliazione (G5.3) |
| Decisione e scadenza concorrenti | Transizioni solo da `Pending` con `RowVersion` |
| Il modello non propone l'ordine per il cliente bloccato | Istruzioni esplicite + test con stub; in caso di esito diverso, `Failed` con motivo visibile |

## Definition of Done

DoD comune di [plan.md](plan.md#definition-of-done-comune-a-ogni-fase).

## Esito

_Da compilare a fine fase (incluso l'esito dello spike S5 e la scelta A/B)._
