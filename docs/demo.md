# Demo — scenari e dati

> I dati demo nascono tutti da una tabella unica, `src/Dusiburg.AI.O2C.Shared/Demo/DemoCatalog.cs` (D5, D32). Da lì partono sia il seed dell'ERP (`ErpSeeder`) sia quello del CRM mock (`CrmSeeder`), così i due sistemi restano coerenti. I test `DemoCatalogTests` verificano che ogni deal corrisponda al proprio scenario.

## Preparazione

```powershell
dotnet run --project tools/Dusiburg.AI.O2C.DbInit     # ricrea O2C su (localdb)\localdev con i dati demo
dotnet run --project src/Dusiburg.AI.O2C.AppHost        # avvia i servizi (vedi README)
```

> ⚠️ Dopo ogni modifica al modello dati va rieseguito `DbInit`: il database si crea da zero dal modello, senza migration (D30). Le tabelle della Fase 5 (`orch.ApprovalRequest`, `orch.ApprovalStatus`, `orch.ApprovalReason`, `orch.WorkflowCheckpoint`) arrivano da lì.

Il broker non va avviato a mano: la risorsa `rabbitmq-wsl` dell'AppHost fa `docker start --attach rabbitmq` dentro WSL e lo tiene vivo finché l'AppHost gira (D26).

Per ripetere la demo senza ricreare il database (solo in Development):

| Servizio | Endpoint | Effetto |
|----------|----------|---------|
| Erp.Api | `POST http://localhost:5101/dev/reset` | Cancella gli ordini e ripristina clienti, prodotti e giacenze del seed; la numerazione degli ordini riparte da `SO-yyyy-000001` |
| Crm.Mcp | `POST http://localhost:5103/dev/reset` | Ripristina aziende e deal del seed: stage `ContractSent`, revisione 1, nessuno stato O2C né note (anche i deal chiusi come persi) |

> I due reset **non** toccano lo schema `orch`: `WorkflowState`, `ApprovalRequest` e `WorkflowCheckpoint` restano. Senza pulirli, una nuova chiusura vinta sulla stessa revisione viene riconosciuto come duplicato e non riparte alcun workflow. Per ripartire davvero da zero conviene rieseguire `DbInit`; in alternativa, cancellare le righe delle tre tabelle.

Le richieste pronte sono in `src/Dusiburg.AI.O2C.Erp.Api/Erp.Api.http` e `src/Dusiburg.AI.O2C.Crm.Mcp/Crm.Mcp.dev.http`.

### Manopole della demo

Si impostano **sull'AppHost** (riga di comando, user-secrets o variabili d'ambiente) e vengono inoltrate al servizio che le legge:

| Impostazione | Default | A cosa serve |
|--------------|---------|--------------|
| `MODEL_PROVIDER` | `anthropic` | `anthropic` (Claude via API) oppure `ollama` (modello locale, D41) |
| `ANTHROPIC_MODEL` | `claude-sonnet-5` | Modello cloud; per i run di accettazione è stato usato `claude-haiku-4-5` (D43) |
| `OLLAMA_MODEL` | `qwen3.5:9b` | Modello locale |
| `O2C_AGENT_MODE` | `multi` | `multi` (tre agenti con handoff) oppure `single` (agente della Fase 3, D47) |
| `APPROVAL_THRESHOLD_EUR` | `10000` | Soglia oltre la quale serve l'approvazione (confronto stretto) |
| `APPROVAL_TIMEOUT_HOURS` | `24` | Scadenza di una richiesta pendente; accetta i decimali (`0.02` ≈ 72 secondi, G5.5) |
| `APPROVAL_SWEEP_MINUTES` | `5` | Ogni quanto girano riconciliazione e scadenza (minimo 10 secondi) |
| `Approvals__ApproverUpn` | `approver@dusiburg.local` | UPN registrato come decisore (D25) |

Esempio, per fare la demo col modello locale e una scadenza da un minuto:

```powershell
dotnet run --project src/Dusiburg.AI.O2C.AppHost -- --MODEL_PROVIDER ollama --APPROVAL_TIMEOUT_HOURS 0.02 --APPROVAL_SWEEP_MINUTES 0.2
```

## Scenari

Tutti i deal partono in stage `ContractSent` con revisione 1. La soglia di approvazione di default è **10.000 €** (`APPROVAL_THRESHOLD_EUR`).

| Deal | Azienda CRM | Cliente ERP | Importo | Cosa lo rende interessante | Esito atteso a regime (Fase 5) |
|------|-------------|-------------|---------|----------------------------|--------------------------------|
| D-1001 | C-01 Officine Meccaniche Brambilla | Esistente | 1.485,00 EUR | Tutto disponibile, sotto soglia | Ordine creato senza approvazione |
| D-1002 | C-02 Cartiera del Brenta | Esistente | 10.832,00 EUR | Sopra soglia, stock ok | Approvazione (soglia) |
| D-1003 | C-03 Automazioni Nord-Est | Esistente | 5.300,00 EUR | `IND-MOT-003`: 5 richiesti, 3 disponibili | Approvazione (backorder) |
| D-1004 | C-04 Nuova Robotica Marche | **Assente** | 2.069,00 EUR | Partita IVA non presente in ERP | Approvazione (cliente nuovo) |
| D-1005 | C-05 Fonderia Emiliana | **Bloccato** | 1.747,00 EUR | `IsBlocked = true` | Approvazione obbligatoria |
| D-1006 | C-06 Tessitura Valle Seriana | Esistente | 3.780,00 **USD** | Valuta diversa da EUR | `Discarded` |
| D-1007 | C-07 Imballaggi Tirreno | Esistente | 1.525,00 EUR | `IND-SEN-999` non esiste in ERP | `Failed` |
| D-1008 | C-08 Siderurgica Lombarda Nuova | **Assente** | 10.640,00 EUR | Sopra soglia **e** cliente nuovo | Approvazione con più motivi |

Gli ordini creabili dalla demo non si rubano stock a vicenda: eseguendo tutti i deal in sequenza, ciascuno mantiene il proprio esito.

## Dati ERP

**Clienti** (10, uno bloccato): Officine Meccaniche Brambilla, Tessitura Valle Seriana, Automazioni Nord-Est, Cartiera del Brenta, Fonderia Emiliana (bloccato), Imballaggi Tirreno, Plastiche Adriatiche, Meccanica di Precisione Torinese, Agroalimentare Salento, Lavorazioni Lamiera Friuli. Le aziende del CRM hanno la stessa partita IVA ed email del cliente ERP corrispondente, tranne C-04 e C-08 che in ERP non esistono.

**Prodotti e giacenze** (22 SKU di forniture industriali). Disponibile = `OnHand − Reserved`.

| SKU | Descrizione | Listino | OnHand | Reserved | Lead time (gg) |
|-----|-------------|---------|--------|----------|----------------|
| IND-BRG-001 | Cuscinetto radiale a sfere 6205-2RS | 12,50 | 500 | 20 | 3 |
| IND-BRG-002 | Cuscinetto a rulli conici 30208 | 38,90 | 180 | 0 | 5 |
| IND-BRG-003 | Cuscinetto orientabile a rulli 22212 | 145,00 | 40 | 0 | 10 |
| IND-BRG-004 | Supporto ritto UCP 206 | 29,80 | 120 | 0 | 4 |
| IND-MOT-001 | Motore asincrono trifase 1,5 kW IE3 | 310,00 | 25 | 0 | 14 |
| IND-MOT-002 | Motore asincrono trifase 4 kW IE3 | 590,00 | 12 | 2 | 14 |
| IND-MOT-003 | Motore brushless 750 W con encoder | 845,00 | 3 | 0 | 21 |
| IND-MOT-004 | Motoriduttore coassiale 0,75 kW i=20 | 720,00 | 0 | 0 | 30 |
| IND-SEN-001 | Sensore induttivo M18 PNP | 24,00 | 300 | 0 | 2 |
| IND-SEN-002 | Sensore fotoelettrico a riflessione | 58,00 | 150 | 10 | 3 |
| IND-SEN-003 | Encoder incrementale 1024 impulsi/giro | 189,00 | 20 | 0 | 7 |
| IND-SEN-004 | Trasduttore di pressione 0-10 bar 4-20 mA | 132,00 | 0 | 0 | 20 |
| IND-PNL-001 | Quadro elettrico IP55 600x400x200 | 265,00 | 15 | 0 | 10 |
| IND-PNL-002 | Inverter 2,2 kW 400 V | 410,00 | 18 | 0 | 7 |
| IND-PNL-003 | Interruttore magnetotermico 4P 32 A | 64,50 | 90 | 0 | 3 |
| IND-PNL-004 | PLC compatto 24 I/O | 980,00 | 6 | 0 | 15 |
| IND-GRX-001 | Riduttore a vite senza fine i=30 | 275,00 | 30 | 0 | 10 |
| IND-GRX-002 | Giunto elastico a denti D38 | 46,00 | 75 | 0 | 5 |
| IND-BLT-001 | Cinghia trapezoidale SPA 1250 | 9,80 | 400 | 0 | 2 |
| IND-BLT-002 | Cinghia dentata HTD 8M-1200-30 | 42,00 | 60 | 0 | 6 |
| IND-LUB-001 | Grasso al litio EP2 cartuccia 400 g | 7,90 | 800 | 0 | 2 |
| IND-CAB-001 | Cavo schermato 4x1,5 mm2 (al metro) | 3,20 | 2500 | 0 | 4 |

Sull'ordine vale il prezzo unitario del deal, non il listino (D21).

## Script della demo

La demo si fa dal browser, con tre UI collegate fra loro (Fase 6):

| UI | Indirizzo | Cosa serve |
|----|-----------|------------|
| **Crm.Web** | http://localhost:5105 | Deal e aziende; sul dettaglio di un deal aperto i comandi **Chiudi vinto** (avvia il flusso) e **Chiudi perso** |
| **Erp.Web** | http://localhost:5106 | Clienti, magazzino e ordini ricevuti, in sola lettura |
| **Approvals.Web** | http://localhost:5104/approvals | Coda delle approvazioni pendenti |

**Chiudi vinto** porta il deal a `ClosedWon` (la revisione non cambia) e pubblica `deal-closed-won`; l'orchestratore consuma dalla coda e lavora da solo. La pagina del deal si aggiorna ogni 5 secondi finché il flusso non ha un esito finale (resta in aggiornamento anche con `ApprovalPending`) e mostra lo storico delle scritture di O2C, il link all'ordine in `Erp.Web` e quello alle richieste di approvazione del deal. Un secondo comando di chiusura sullo stesso deal risponde "già chiuso" e non pubblica nulla.

In alternativa alla UI restano gli script (solo in Development):

```powershell
$deal = "D-1001"
Invoke-RestMethod -Method Post "http://localhost:5103/api/deals/$deal/close" -ContentType application/json -Body '{ "outcome": "Won" }'
# oppure, per ripubblicare l'evento di un deal già vinto:
Invoke-RestMethod -Method Post "http://localhost:5103/dev/deals/$deal/close-won"
```

Dopo ogni passo si guardano tre posti: la pagina del deal in `Crm.Web`, l'ordine e il magazzino in `Erp.Web`, la coda in `Approvals.Web`. Nel dashboard, la riga finale del log dell'orchestratore `Deal … elaborato (multi) con …: <esito> <numero ordine>`.

### 1. D-1001 — percorso felice, nessuna approvazione

**Chiudi vinto** su D-1001 → in circa 20 s la pagina del deal (senza intervento) mostra `OrderCreated` e il numero d'ordine; il link apre l'ordine in `Erp.Web` con righe e totale, e il magazzino mostra la riserva (es. `IND-BRG-001` riservato da 20 a 60). In `/approvals` **non** compare nulla.

### 2. D-1002 — approvazione per soglia, poi approva

1. **Chiudi vinto** su D-1002.
2. Il deal diventa `ApprovalPending` con la nota `Approvazione richiesta: OverThreshold. Totale 10.832,00 EUR.` In `Erp.Web` **non** c'è alcun ordine.
3. Il link "Richieste del deal" apre la richiesta in `/approvals`; aprendola si vedono righe, totale, giacenze, cliente e chiave di idempotenza, e il link riporta al deal in `Crm.Web`.
4. **Approva** con una nota → il workflow riprende dal checkpoint, crea l'ordine e la pagina del deal passa a `OrderCreated`.

### 3. D-1003, D-1004, D-1005, D-1008 — gli altri motivi

Stessa sequenza, con i motivi attesi:

| Deal | Motivi attesi in `/approvals` |
|------|-------------------------------|
| D-1003 | `InsufficientStock` (IND-MOT-003: 5 richiesti, 3 disponibili). Dopo l'approvazione l'ordine nasce in **Backorder**: `Erp.Web` mostra la nota `IND-MOT-003: 2 PZ da ordinare` e, in magazzino, `IND-MOT-003` con riservato oltre la giacenza (evidenziato in rosso); il deal riporta la nota `Da approvvigionare — IND-MOT-003: 2 PZ da ordinare` |
| D-1004 | `NewCustomer` (l'anagrafica ERP viene creata prima della sospensione e compare subito fra i clienti di `Erp.Web`) |
| D-1005 | `BlockedCustomer` (in `Erp.Web` il cliente ha il badge "bloccato") |
| D-1008 | `OverThreshold` **e** `NewCustomer` |

### 4. Rifiuto

Su una richiesta pendente premere **Rifiuta** con una nota: non viene creato alcun ordine e il deal passa a `Rejected` con la nota del decisore. La seconda decisione sulla stessa richiesta riceve `409`.

### 5. Scadenza

Avviare l'AppHost con `--APPROVAL_TIMEOUT_HOURS 0.02 --APPROVAL_SWEEP_MINUTES 0.2`, generare una richiesta e non decidere: entro un paio di minuti la richiesta passa a `Expired`, non nasce alcun ordine e il deal diventa `Expired` con la nota della scadenza.

### 6. Riavvio durante l'attesa

1. **Chiudi vinto** su un deal che richiede approvazione e attendere la riga pendente in `/approvals`.
2. Fermare l'orchestratore dal dashboard (o riavviare l'AppHost).
3. Approvare dalla UI: il messaggio `approval-decided` arriva al nuovo processo — e se anche si perdesse, la sweep di riconciliazione trova la richiesta decisa con il workflow ancora in `AwaitingApproval`. L'ordine viene creato una volta sola, con la stessa chiave di idempotenza.

### 7. D-1006 e D-1007 — gli esiti di arresto

**Chiudi vinto** su D-1006 → `Discarded` (valuta USD, verificata dall'host sui dati del deal). Su D-1007 → `Failed` (`IND-SEN-999` non esiste in ERP). In nessuno dei due casi si passa dall'approvazione.

### 8. Chiudi perso

**Chiudi perso** su un deal aperto → stage `ClosedLost`, nessun messaggio su `deal-closed-won`, nessun workflow e nessuna nota di O2C. Il deal non si può più chiudere come vinto (serve il reset).

### Dove guardare nel dashboard

Nelle tracce, una richiesta approvata produce una catena unica: la richiesta HTTP di `crm-web` → `crm.deal.close` (con `deal.close.outcome`) → `publish deal.closed-won` (CRM) → `o2c.process_deal` → `agent.run` di Intake, Fulfillment e Order con i loro `agent.handoff` e `tool.call` → `approval.requested` (con `approval.reasons`) → `approval.decided` (con `approval.decision` e `approval.decided_by`, da Approvals.Web) → `approval.resume` → `tool.call erp.create_order`. Il `correlation.id` nasce dalla richiesta di `Crm.Web` e arriva invariato al workflow. La ripresa si riattacca al contesto salvato in `ApprovalRequest.TraceParent`, quindi anche dopo ore resta lo stesso trace id (G5.4).
