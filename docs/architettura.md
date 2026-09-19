# POC Order\-to\-Cash Agentico — Specifica di Contesto e Implementazione

Architettura agentica enterprise ERP↔CRM · Documento di contesto completo per l'implementazione · Settembre 2026

> **Copia versionata nel repository**: da Fase 0 questo file è la fonte di verità della specifica. Le modifiche rispetto alla versione iniziale sono elencate nel §17 e, quando applicate, riportate direttamente nelle sezioni indicate.

## Come usare questo documento

Questo documento è il **contesto unico e autosufficiente** del progetto: chi lo legge (persona o agente di coding) deve poter iniziare a implementare senza altre fonti. Contiene il razionale architetturale (§1–§3), la specifica funzionale e tecnica del POC (§4–§10) e il piano di lavoro con criteri di accettazione (§11).

Regole operative per un agente di coding che lavora su questo progetto:

- Implementare **fase per fase** secondo §11, senza saltare avanti: ogni fase ha criteri di accettazione verificabili.
- **Non inventare tool MCP** diversi da quelli specificati in §6: i contratti sono normativi. Se serve un tool nuovo, va prima aggiunto a questo documento.
- Rispettare i **requisiti non funzionali** di §12 (idempotenza, tracing, gestione segreti) fin dalla prima riga di codice: non sono rifiniture finali.
- Le **decisioni architetturali** di §13 sono già prese e motivate: non riaprirle senza una ragione tecnica esplicita.
- Quanto elencato in §14 è **fuori scope**\: non implementarlo.

## 1\. Contesto e obiettivi

Il progetto nasce come proof of concept dimostrativo per un architetto software con background .NET/enterprise che si posiziona sul mercato dell'AI agentic per le aziende. Il POC deve dimostrare, su uno scenario di business realistico, tre capacità che oggi distinguono un'integrazione agentica da una tradizionale:

1. **Orchestrazione multi\-agente** — più agenti specializzati che si passano il controllo, non un singolo agente monolitico.
2. **MCP (Model Context Protocol)** — sistemi aziendali esposti come tool interoperabili, non integrazioni custom punto\-a\-punto.
3. **Human\-in\-the\-loop** — un punto di approvazione umana reale, che blocca azioni ad alto impatto prima dell'esecuzione.

L'integrazione agentica non sostituisce l'integrazione classica: la estende. La differenza è che il sistema non esegue un flusso fisso, ma un modello decide **quale** azione compiere, **in che ordine** e **con quali dati**, a partire da un obiettivo. Questo aggiunge strati che prima non esistevano (orchestrazione, tool layer, memoria, guardrail), mentre hosting, identità, networking e osservabilità restano vicini a un'architettura .NET enterprise tradizionale.

## 2\. Architettura di riferimento a strati

Il POC istanzia questa architettura generale. Ogni strato è mappato ai componenti concreti nelle sezioni successive.

| Strato | Ruolo | Componente nel POC |
| --- | --- | --- |
| 1\. Canale / trigger | Dove nasce l'esigenza di agire | Webhook CRM su deal "Closed Won" → Service Bus |
| 2\. Orchestrazione agenti | Planning, reasoning, stato del task | Microsoft Agent Framework, Handoff orchestration |
| 3\. Tool layer | Funzioni invocabili dall'agente | Due server MCP (ERP, CRM) |
| 4\. Integration / backend | API, messaggistica, normalizzazione | Minimal API ASP.NET Core \+ Azure Service Bus |
| 5\. Dati e memoria | Stato, audit trail | Azure SQL (dati ERP mock \+ stato workflow) |
| 6\. Governance trasversale | Identità, guardrail, controllo umano | Entra ID, tool sensibili con approvazione, Teams/web approvals |

## 3\. Piattaforma: perché Azure

La scelta di Azure per questo POC è motivata da ragioni tecniche puntuali, non solo dalla familiarità con .NET:

- **Microsoft Agent Framework** (GA dal 2 aprile 2026, fusione di Semantic Kernel e AutoGen) e l'**MCP C\# SDK ufficiale** (v2.0, mantenuto in collaborazione con Microsoft) sono entrambi progetti .NET\-first: documentazione, esempi e supporto sono più maturi rispetto agli equivalenti su AWS o GCP quando si sviluppa in C\#.
- **Teams** fornisce il canale di approvazione umana più naturale in un contesto enterprise che usa già Microsoft 365.
- **Azure Container Apps** consente hosting leggero con scale\-to\-zero, senza gestire un cluster Kubernetes per un POC.
- Esiste un **percorso di produzione già tracciato**\: l'orchestrazione può essere spostata su Microsoft Foundry Agent Service (runtime gestito, Entra Agent ID, Foundry IQ) senza riscrivere gli agenti.

**Decisione**\: nel POC l'orchestrazione è **self\-hosted** con Microsoft Agent Framework su Azure Container Apps, non delegata al Foundry Agent Service gestito. Motivo: il valore dimostrativo del POC sta nel rendere ispezionabile il meccanismo (handoff, chiamate MCP, intercettazione dell'approvazione). Il servizio gestito è il passo successivo verso la produzione, non la base del POC.

### 3.1 Dipendenze cloud strettamente necessarie

Escludendo ERP e CRM, **l'unica dipendenza esterna realmente obbligatoria è l'endpoint del modello**. Ogni altro componente ha un equivalente locale in container senza perdita di funzionalità.

| Componente | Equivalente locale | Servizio Azure | Cloud obbligatorio? |
| --- | --- | --- | --- |
| `Erp.Api` + database | Processo .NET + SQL Server LocalDB `(localdb)\localdev`, database `O2C`, schemi `erp`/`crm`/`orch` (M2) | Azure SQL serverless | No |
| `Erp.Mcp`, `Crm.Mcp` | Container .NET su rete locale | Container Apps | No |
| `Orchestrator` | Container .NET | Container Apps | No |
| `Approvals.Web` | Container .NET su localhost | Container Apps | No |
| `Crm.Web`, `Erp.Web` (M26) | Container .NET su localhost, solo HTTP verso `Crm.Mcp` ed `Erp.Api` | Container Apps | No |
| Messaggistica | RabbitMQ in container Docker dentro WSL, vhost `o2c`, exchange `deal-closed-won` (M7) | Service Bus | No |
| Osservabilità | Aspire Dashboard (riceve OTLP nativamente) | Application Insights | No |
| Identità agente | API key locali | Entra ID / Entra Agent ID | No per funzionare |
| Approvazione umana | Pagina web `/approvals` | Teams Adaptive Card | No |
| **Modello (LLM)** | Ollama + modello locale (vedi §3.3) | Claude via API Anthropic (M23); su Azure, Claude in Microsoft Foundry (Fase 7) | **Unico nodo reale** |

### 3.2 Topologia locale con .NET Aspire

L'host locale di riferimento è un progetto **.NET Aspire AppHost** che compone in C# tutti i servizi — `Erp.Api`, `Erp.Mcp`, `Crm.Mcp`, `Orchestrator`, `Approvals.Web`, `Crm.Web`, `Erp.Web` e, opzionalmente, Ollama — gestendo service discovery e propagazione delle variabili d'ambiente fra loro. Database (LocalDB) e broker (RabbitMQ in WSL) entrano come **risorse esterne via connection string**, senza container gestiti da Aspire; l'AppHost tiene aperta una sessione WSL (`wsl -- docker start --attach rabbitmq`) perché il broker resti attivo mentre gira il POC (M2, M7).

Due conseguenze rilevanti per il progetto:

- Il dashboard Aspire riceve le tracce **OpenTelemetry** già cablate: il requisito di tracing di §12 è verificabile in locale, senza Application Insights.
- Lo stesso AppHost si deploya su Azure Container Apps con `azd up`: la Fase 7 di §11 (deploy) diventa un comando, non una riscrittura. Le Fasi 1–6 si sviluppano quindi interamente in locale.

### 3.3 Modello: switch fra locale e cloud

L'Agent Framework è costruito sull'astrazione `IChatClient` di `Microsoft.Extensions.AI` e dispone di un provider Ollama documentato, oltre al supporto per qualunque endpoint OpenAI-compatibile. Il passaggio fra modello cloud e modello locale è quindi un cambio di configurazione, non di codice.

**Requisito implementativo**: l'orchestratore non deve avere dipendenze dirette dall'SDK di un provider — deve dipendere solo da `IChatClient`, con il provider risolto da configurazione (`MODEL_PROVIDER`).

**Provider del POC (M23)**: `MODEL_PROVIDER=anthropic` (default) usa **Claude via API Anthropic**, modello `claude-sonnet-5`, con l'SDK ufficiale `Anthropic` per C# che espone un `IChatClient`; `MODEL_PROVIDER=ollama` usa Ollama nativo su Windows con `qwen3.5:9b` (OllamaSharp, anch'esso `IChatClient`). Azure OpenAI non è più previsto; in Fase 7 la variante su Azure è Claude in Microsoft Foundry, con la stessa famiglia di modelli.

**Caveat tecnico**: il POC si regge su tool calling affidabile e output strutturato, che è esattamente la capacità dove i modelli piccoli (7–8B) diventano fragili — tool sbagliato, handoff mancato, JSON non conforme. Con un modello locale sottodimensionato si finisce a debuggare il modello invece dell'architettura. Servono modelli con tool calling solido (fascia 30B+ quantizzati in su) e hardware adeguato. In Fase 3 il modello locale su una GPU da 8 GB (`qwen3.5:9b`) viene **misurato** sul flusso reale, senza criteri di accettazione vincolanti (D12, D41): l'accettazione si fa sul modello cloud.

**Scelta operativa**: sviluppare con tutti i servizi in locale ma con l'endpoint del modello sul cloud (costo in token trascurabile per un POC), mantenendo il modello locale come variante configurabile da dimostrare — è un argomento commerciale concreto per clienti che non possono far uscire dati dal perimetro.

### 3.4 Cosa non è dimostrabile in locale

Tre elementi richiedono il cloud, tutti irrilevanti per costruire e testare, tutti rilevanti in una demo enterprise: l'**identità dell'agente** come cittadino di prima classe (Entra Agent ID); l'**approvazione via Teams**, i cui callback delle Adaptive Card richiedono un endpoint pubblicamente raggiungibile (in locale servirebbe un dev tunnel, motivo per cui la pagina `/approvals` è la scelta giusta in sviluppo); lo **scale-to-zero e il runtime gestito**. Tutti e tre entrano in Fase 7.

## 4\. Scenario funzionale del POC

**Order\-to\-Cash**\: quando un'opportunità viene chiusa positivamente nel CRM, il sistema crea l'ordine cliente corrispondente nell'ERP, verificando disponibilità e condizioni, con approvazione umana obbligatoria sopra determinate soglie di rischio.

Flusso end\-to\-end:

1. Un deal passa allo stage "Closed Won" nel CRM → webhook → messaggio su Service Bus (topic `deal-closed-won`).
2. **IntakeAgent** recupera i dati completi del deal e dell'azienda dal CRM via MCP, li valida e li normalizza.
3. **FulfillmentAgent** verifica per ogni riga la disponibilità a magazzino nell'ERP via MCP.
4. **OrderAgent** verifica l'anagrafica cliente nell'ERP, valuta le regole di approvazione (§7) e crea l'ordine.
5. Se una regola di approvazione scatta, l'esecuzione si **sospende** e viene generata una richiesta di approvazione umana; il workflow riprende solo dopo la decisione.
6. A ordine creato, lo stato e il numero d'ordine ERP vengono riscritti sul deal nel CRM.
7. Ogni passaggio è tracciato (OpenTelemetry) e persistito per audit.

## 5\. Agenti e orchestrazione

Tre agenti specializzati collegati con **Handoff orchestration**\: si dichiarano gli agenti e gli archi diretti tra loro, e il framework inietta automaticamente i tool di trasferimento del controllo.

| Agente | Responsabilità | Tool MCP usati | Handoff verso |
| --- | --- | --- | --- |
| `IntakeAgent` | Validare e normalizzare il deal; arricchire con dati azienda; scartare deal non processabili | `crm.get_deal`, `crm.get_company` | `FulfillmentAgent` |
| `FulfillmentAgent` | Verificare disponibilità di ogni riga; determinare se l'ordine è evadibile, parziale o bloccato | `erp.check_stock` | `OrderAgent` |
| `OrderAgent` | Risolvere/creare anagrafica cliente; applicare regole di approvazione; creare l'ordine; aggiornare il CRM | `erp.get_customer`, `erp.create_customer`, `erp.create_order`, `crm.update_deal` | — (terminale) |

Vincoli di orchestrazione:

- Ogni agente ha **istruzioni di sistema esplicite** che ne delimitano il perimetro: nessun agente può chiamare tool non elencati nella propria riga.
- Il modello opera a **temperatura bassa** (0–0.2) e con **output strutturato** (JSON schema) per ogni decisione che influenza il flusso.
- Lo stato del workflow è persistito su database: il processo deve poter riprendere dopo un riavvio (§12).
- Gli agenti si scambiano **fatti, non testo** (M29): nella richiesta al modello di un agente non entra il testo scritto dagli altri agenti del workflow, ma restano le loro chiamate ai tool e i risultati. Nel workflow di handoff quel testo arriverebbe come messaggio dell'utente e verrebbe letto come un'istruzione: una frase di `IntakeAgent` sul passaggio di mano bastava a far saltare `check_stock` a `FulfillmentAgent`.
- Le **istruzioni di ogni agente stanno in un file** (M30), `Agents/Specs/<Nome>.agent.md`, incluso come risorsa dell'assembly: si leggono e si discutono senza aprire il codice. Nel codice restano l'allow-list dei tool, il verdetto di arresto e la topologia, che sono guardrail e non testo.

Regole di dominio (M9):

- **Valuta**: si processano solo deal in EUR; un deal in altra valuta è scartato con stato `Discarded` e nota.
- **Riga non disponibile** (ordine "parziale"): richiede approvazione (§7); se approvato, l'ordine è creato completo in stato `Backorder`.
- **SKU inesistente in ERP** (ordine "bloccato"): il workflow si ferma con stato `Failed`, senza richiesta di approvazione.
- **Prezzo**: vale lo `unitPrice` del deal; il `ListPrice` dell'ERP è solo informativo.

## 6\. Contratti dei server MCP

Due server MCP distinti, entrambi in C\# con l'SDK ufficiale `ModelContextProtocol`. Esposti su HTTP (trasporto stateless) per il deployment su Container Apps; in locale è ammesso anche stdio per il debug.

### 6\.1 Server `erp-mcp`

| Tool | Input | Output |
| --- | --- | --- |
| `get_customer` | `vatNumber` oppure `email` (almeno uno) | `{ customer: { customerId, name, vatNumber, email, creditLimit, isBlocked } \| null }` (M20) |
| `create_customer` | `name`, `vatNumber`, `email`, `address` | `{ customerId }` |
| `check_stock` | `sku`, `quantity` | `{ sku, available: bool, onHand: int, leadTimeDays: int }` |
| `create_order` | `customerId`, `lines[{ sku, quantity, unitPrice }]`, `externalRef`, `idempotencyKey` | `{ orderId, orderNumber, total, status, backorderNote }` (M25) |
| `get_order` | `orderId` | ordine completo con righe |

### 6\.2 Server `crm-mcp`

| Tool | Input | Output |
| --- | --- | --- |
| `get_deal` | `dealId` | `{ dealId, revision, name, amount, currency, stage, companyId, lineItems[{ sku, quantity, unitPrice }] }` |
| `get_company` | `companyId` | `{ companyId, name, vatNumber, email, address }` |
| `update_deal` | `dealId`, `erpOrderNumber`, `status`, `note` | `{ ok: bool }` |

- `revision` (M3): intero che cresce solo con le modifiche commerciali del deal; serve a calcolare la `idempotencyKey` (§12).
- `update_deal.status` (M4) è un enum chiuso: `ApprovalPending | OrderCreated | Rejected | Expired | Discarded | Failed`. Un valore diverso è un `VALIDATION_ERROR`.

Regole comuni ai due server:

- `get_customer` (M20): un cliente non trovato è `{ customer: null }`, non un errore; così ogni tool ha un output strutturato a oggetto.
- Ogni tool ritorna **errori strutturati** (`{ error: { code, message } }`), mai eccezioni non gestite: l'agente deve poter ragionare sull'errore. Sul protocollo MCP (M22) l'errore è un risultato con `isError = true` e l'envelope come testo JSON, senza `structuredContent`; i risultati positivi hanno `structuredContent` conforme all'`outputSchema` del tool. Codici: `VALIDATION_ERROR`, `NOT_FOUND`, `CONFLICT`, `UNAUTHORIZED`, `UPSTREAM_UNAVAILABLE`, `INTERNAL`.
- Nessun tool esegue più di un'operazione di scrittura: la granularità è deliberatamente fine per rendere l'approvazione selettiva.
- `create_order` è l'unico tool marcato come **sensibile** e soggetto a intercettazione (§7).

### 6\.3 API utente dei sistemi (M27)

Oltre ai tool, i due sistemi espongono le API usate dalle loro UI (`Crm.Web`, `Erp.Web`, Fase 6). Non sono contratti per gli agenti: stanno sotto `/api/views`, separate dagli endpoint dei tool, e i servizi restano gli unici proprietari dei propri schemi (le UI non leggono il database).

| Servizio | Endpoint | Scopo |
| --- | --- | --- |
| `Crm.Mcp` | `GET /api/views/deals?stage=&o2cStatus=&companyId=`, `GET /api/views/deals/{dealId}` | Deal con stage, revisione, stato O2C, righe e storico delle scritture di O2C |
| `Crm.Mcp` | `GET /api/views/companies`, `GET /api/views/companies/{companyId}` | Aziende e loro deal |
| `Crm.Mcp` | `POST /api/deals/{dealId}/close` `{ outcome: Won \| Lost }` | Chiusura del deal: ammessa solo da `ContractSent` (409 altrimenti), revisione invariata; `Won` pubblica `deal-closed-won` ed è l'ingresso del flusso (503 se il deal è chiuso ma l'evento non è partito), `Lost` non pubblica nulla |
| `Erp.Api` | `GET /api/views/customers`, `/customers/{id}`, `/stock?shortOnly=`, `/orders?status=&customerId=`, `/orders/{orderNumber}` | Clienti, magazzino (disponibile = giacenza − riservato, anche negativo) e ordini ricevuti, in sola lettura |

Le letture restituiscono al massimo 200 righe con ordinamento stabile; errori come ProblemDetails con `code` del catalogo. In locale non c'è autenticazione (come `/approvals`); in Fase 7 le API restano interne all'ambiente e le UI passano da Entra.

## 7\. Human\-in\-the\-loop

L'approvazione umana usa il meccanismo nativo di Agent Framework per i tool sensibili (M8): `erp.create_order` è dichiarato **`ApprovalRequiredAIFunction`**, quindi il framework non lo invoca senza una risposta di approvazione ed espone la chiamata su una **porta esterna** del workflow (richiesta `ToolApprovalRequestContent`, risposta `ToolApprovalResponseContent`). Il middleware `ToolApprovalAgent` esiste ma serve alle regole "non chiedere più" e alla coda di richieste multiple, entrambe fuori dallo scope del POC: non viene usato.

Alla richiesta esposta risponde **l'host, non il modello**: valuta la `ApprovalPolicy` sugli argomenti proposti e sui fatti del run e, se non serve nessuna approvazione, approva subito e il workflow prosegue senza che nessuno se ne accorga. `FunctionInvokingChatClient` chiede conferma per ogni chiamata del turno in cui compare un tool sensibile, anche per i tool che non lo sono: quelle richieste vengono approvate direttamente dall'host.

**Regole che richiedono approvazione** (valutate prima di `erp.create_order`):

> Le regole sono un **elenco dichiarativo** in `ApprovalPolicy.Rules`, ciascuna con nome e spiegazione in italiano; da lì si genera [regole-di-approvazione.md](regole-di-approvazione.md), che un test tiene allineato (M31). Restano compilate: configurabile è la sola soglia.

- Totale ordine superiore a `APPROVAL_THRESHOLD_EUR` (default: 10.000 €).
- Almeno una riga con disponibilità insufficiente (`available = false`).
- Cliente non presente in ERP (creazione anagrafica contestuale).
- Cliente con `isBlocked = true` — in questo caso l'approvazione è l'unica strada possibile.

Semantica dei dati usati dalle regole (M9): una riga è disponibile se `OnHand − Reserved ≥ quantity`; `create_order` riserva lo stock (incrementa `Reserved`) nella stessa transazione che crea l'ordine; `creditLimit` non partecipa alle regole di approvazione.

**Le giacenze su cui la policy decide le verifica l'orchestratore** (M25), riga per riga e con le quantità effettivamente proposte, subito prima di valutare le regole. Le chiamate a `check_stock` fatte da `FulfillmentAgent` restano nella traccia del run ma non sono la base della decisione: un agente che salta la verifica — o che la esegue con la quantità sbagliata — non deve poter far passare un ordine che andrebbe approvato. Se la verifica risponde `NOT_FOUND` lo SKU non esiste in ERP: il workflow si ferma come `Failed` senza approvazione (regola "bloccato" di §5), la chiamata a `create_order` viene rifiutata e da quel momento gli agenti non possono più scrivere su ERP o CRM, perché l'esito lo scrive l'orchestratore (M28). Ogni altra verifica che non riesce conta come riga non disponibile, così l'esito peggiore è un'approvazione in più.

**Ordine in backorder** (M25): l'ordine viene accettato e lo stock riservato per intero, quindi `Reserved` può superare `OnHand` — è la domanda impegnata, che un ERP completo userebbe per riordinare. Perché quell'informazione non resti implicita, `create_order` restituisce `backorderNote` con cosa manca e in che quantità (es. `IND-MOT-003: 2 PZ da ordinare`), calcolata prima della riserva e conservata sull'ordine; l'orchestratore la riporta come nota sul deal CRM.

La policy è deterministica, scritta in C# e **unica**: i prompt degli agenti non contengono soglie né condizioni, e il totale su cui decide viene calcolato dalle righe proposte, non letto da ciò che dice il modello (§12, D17). A `OrderAgent` viene chiesto di proporre l'ordine anche per un cliente bloccato, perché l'approvazione è l'unica strada.

**Canale**\: Adaptive Card in Teams con azioni Approva/Rifiuta; fallback per il POC, una pagina web `/approvals` che elenca le richieste pendenti. Entrambi scrivono sullo stesso endpoint di callback, `POST /api/approvals/{approvalId}/decision`.

**Persistenza dello stato** — tabella `orch.ApprovalRequest` (M12, convenzioni di chiave di M16)\:

| Campo | Tipo | Note |
| --- | --- | --- |
| `Id` | int | chiave |
| `PublicId` | GUID univoco | `approvalId` esposto da UI, callback e messaggio |
| `CorrelationId` | string | traccia l'intero workflow; è anche la sessione dei checkpoint |
| `DealId`, `DealRevision` | string, int | riferimento CRM e revisione su cui è calcolata la chiave di idempotenza |
| `PayloadJson` | text | proposta di ordine completa, mostrata all'approvatore |
| `ReasonsJson` | text | quali regole hanno scatenato l'approvazione (array, possono essere più di una) |
| `Total` | decimal | totale della proposta, per l'elenco della UI |
| `Status` | enum | `Pending`, `Approved`, `Rejected`, `Expired` (FK verso la lookup `orch.ApprovalStatus`) |
| `RequestedAt`, `DecidedAt` | datetime |  |
| `DecidedBy` | string | UPN dell'approvatore |
| `DecisionNote` | string | motivazione facoltativa |
| `TraceParent` | string | contesto W3C dello span che ha generato la richiesta: la ripresa lo usa come parent |
| `CheckpointId` | string | checkpoint del workflow da cui riprendere |
| `RowVersion` | rowversion | concorrenza ottimistica: le transizioni sono ammesse solo da `Pending` |

**Comportamento richiesto**\: l'attesa dell'approvazione **non** tiene il processo agente in memoria. Alla generazione della richiesta il workflow si sospende, lo stato e il checkpoint vengono persistiti (`orch.WorkflowCheckpoint`, uno per superstep, tramite un `ICheckpointStore<JsonElement>` su SQL) e il messaggio del broker viene confermato; alla decisione il workflow riprende dal checkpoint, **anche in un altro processo**, e `create_order` parte con la stessa chiave di idempotenza della proposta.

La decisione raggiunge l'orchestratore in due modi indipendenti: il messaggio `approval-decided` come acceleratore e una **sweep di riconciliazione** periodica e all'avvio, che cerca le richieste decise con il workflow non ancora ripreso. Un messaggio perso non blocca nulla, e due consegne simultanee non riprendono lo stesso workflow: l'uscita da `AwaitingApproval` è un **UPDATE condizionale** verso la fase `Resuming` e procede solo chi tocca una riga (M12). Il possesso scade dopo `ResumeClaimTimeout`, così un processo morto durante la ripresa non lascia il workflow appeso.

Rifiuto e scadenza non riaprono il workflow: non c'è nessun ordine da creare, quindi l'host scrive direttamente l'esito sul CRM (`Rejected` con la nota del decisore, `Expired` con la nota della scadenza) e chiude il workflow. Scadenza dopo `APPROVAL_TIMEOUT_HOURS` (default 24, decimali ammessi per la demo) con transizione a `Expired` sotto `RowVersion`, così una decisione concorrente e la scadenza non possono sovrapporsi.

## 8\. Modello dati del mock ERP

Database relazionale (Azure SQL in cloud, SQL Server LocalDB `(localdb)\localdev` in locale — M2) via EF Core, con seed di dati realistici (almeno 20 prodotti, 10 clienti, scorte miste per poter innescare sia il percorso felice sia quello di approvazione).

| Tabella | Campi principali |
| --- | --- |
| `Customer` | `Id`, `Name`, `VatNumber`, `Email`, `Address`, `CreditLimit`, `IsBlocked` |
| `Product` | `Id`, `Sku` (univoco), `Description`, `ListPrice`, `Uom` |
| `StockLevel` | `Id`, `ProductId` (univoco, 1:1), `OnHand`, `Reserved`, `LeadTimeDays`, `RowVersion` |
| `Order` | `Id`, `PublicId` (GUID univoco), `OrderNumber`, `CustomerId`, `Total`, `OrderStatusId`, `ExternalRef`, `IdempotencyKey`, `BackorderNote` (M25), `CreatedAt` |
| `OrderStatus` | `Id` (tinyint = valore di `OrderStatus`), `Name` — lookup generata dall'enum |
| `OrderLine` | `Id`, `OrderId`, `ProductId`, `Quantity`, `UnitPrice` |
| `ApprovalRequest` | vedi §7 |
| `ApprovalStatus`, `ApprovalReason` | `Id` (tinyint = valore dell'enum), `Name` — lookup generate dagli enum (M12) |
| `WorkflowState` | `Id`, `CorrelationId` (univoco), `DealId`, `DealRevision` (univoco con `DealId`), `WorkflowPhaseId`, `StateJson`, `CreatedAt`, `UpdatedAt`, `RowVersion` (M11) |
| `WorkflowPhase` | `Id` (tinyint = valore di `WorkflowPhase`), `Name` — lookup generata dall'enum (M11); dalla Fase 5 comprende `AwaitingApproval` e `Resuming` (M12) |
| `WorkflowCheckpoint` | `Id` (bigint crescente = ordine di commit), `SessionId` (= correlation id), `CheckpointId`, `ParentCheckpointId`, `PayloadJson`, `CreatedAt`; univoco su `(SessionId, CheckpointId)` (M12) |

Vincolo: indice univoco su `Order.IdempotencyKey` — è il meccanismo che rende impossibile la creazione doppia di un ordine a fronte di un retry dell'agente.

Convenzioni di chiave (M16): la PK numerica si chiama sempre `Id` e le FK sono qualificate (`CustomerId`); non si usano PK GUID — il GUID esposto come `orderId` dai contratti di §6 è la colonna univoca `Order.PublicId`; le chiavi di business stringa sono colonne univoche accanto a `Id` (`Product.Sku`; nel CRM `Company.Code` = `companyId` e `Deal.Code` = `dealId`, con `DealLineItem` e `DealNote` figlie di `Deal`). I contratti di §6 non cambiano: la traduzione fra nomi di tabella e nomi di contratto è nel codice.

Schemi (M2): le tabelle ERP stanno nello schema `erp` (`ErpDbContext` in `Erp.Data`, usato da `Erp.Api`), `ApprovalRequest`, `WorkflowState` e `WorkflowCheckpoint` nello schema `orch` (Orchestrator e `Approvals.Web`), il CRM mock nello schema `crm` (`CrmDbContext` in `Crm.Data`, usato da `Crm.Mcp`: `Company`, `Deal`, `DealLineItem`, `DealNote`). Stesso database `O2C`, DbContext separati.

Enum e creazione dello schema (M17): ogni enum persistito è una FK verso una tabella di lookup con PK `tinyint` uguale al valore esplicito del membro nel codice e `Name` univoco, righe generate dall'enum (`erp.OrderStatus`; `crm.DealStage` per lo stage del deal, `crm.DealStatus` per lo stato O2C). Non si usano migration: il database si crea da zero dal modello con `tools/Dusiburg.AI.O2C.DbInit` (drop + create), e ogni modifica del modello si applica ricreandolo.

## 9\. Stack tecnico

| Componente | Tecnologia (cloud) | In locale |
| --- | --- | --- |
| Linguaggio / runtime | C\# / .NET 10 (`net10.0`, LTS — M1; upgrade a .NET 11 successivo) | identico |
| Orchestrazione agenti | Microsoft Agent Framework (Handoff orchestration, approvazione dei tool sensibili) | identico |
| Tool layer | MCP C\# SDK (`ModelContextProtocol`, v2.x) | identico |
| Modello | Claude (`claude-sonnet-5`) via API Anthropic, SDK `Anthropic` per C# con `IChatClient`; su Azure, Claude in Microsoft Foundry (M23) | Ollama nativo Windows (`qwen3.5:9b`) via `IChatClient` (§3.3) |
| Persistenza | Azure SQL serverless, EF Core | SQL Server LocalDB `(localdb)\localdev`, EF Core (M2) |
| Messaggistica | Azure Service Bus, topic `deal-closed-won` | RabbitMQ in container Docker dentro WSL, exchange `deal-closed-won` (M7) |
| CRM | HubSpot free tier (reale) oppure mock equivalente dietro lo stesso contratto MCP | mock persistente dentro `Crm.Mcp` dietro `ICrmClient` (M6) |
| Hosting | Azure Container Apps (orchestratore, due server MCP, web approvals) | .NET Aspire AppHost (§3.2) |
| Identità | Entra ID, app registration dedicata per l'agente, least\-privilege | API key locali |
| Osservabilità | OpenTelemetry → Application Insights | OpenTelemetry → Aspire Dashboard |
| Infrastruttura | Bicep \+ Azure Developer CLI (`azd`) | — |

## 10\. Struttura del repository e configurazione

```text
AI.POC-OrderToCash/
├─ src/
│  ├─ Dusiburg.AI.O2C.AppHost/             # .NET Aspire: composizione locale di tutti i servizi
│  ├─ Dusiburg.AI.O2C.ServiceDefaults/     # OpenTelemetry, health check, service discovery, correlation id condivisi
│  ├─ Dusiburg.AI.O2C.Erp.Api/             # Minimal API: il "gestionale" mock
│  ├─ Dusiburg.AI.O2C.Erp.Data/            # Modello EF Core dell'ERP, schema erp (M17)
│  ├─ Dusiburg.AI.O2C.Erp.Mcp/             # Server MCP sopra Erp.Api
│  ├─ Dusiburg.AI.O2C.Crm.Mcp/             # Server MCP + CRM mock persistente dietro ICrmClient (M6); adapter HubSpot opzionale
│  ├─ Dusiburg.AI.O2C.Crm.Data/            # Modello EF Core del CRM mock, schema crm (M17)
│  ├─ Dusiburg.AI.O2C.Mcp.Hosting/         # Infrastruttura comune dei server MCP: API key, filtro sui tool, errori (M21)
│  ├─ Dusiburg.AI.O2C.Orchestration.Data/  # Stato dell'orchestrazione, schema orch: WorkflowState, ApprovalRequest, WorkflowCheckpoint (M11, M12)
│  ├─ Dusiburg.AI.O2C.Orchestrator/        # Worker: agenti, handoff, policy di approvazione
│  ├─ Dusiburg.AI.O2C.Approvals.Web/       # UI approvazioni + callback Teams
│  ├─ Dusiburg.AI.O2C.Crm.Web/             # UI del CRM mock: deal, aziende, chiusura vinto/perso (M26)
│  ├─ Dusiburg.AI.O2C.Erp.Web/             # UI dell'ERP in sola lettura: clienti, magazzino, ordini (M26)
│  └─ Dusiburg.AI.O2C.Shared/              # DTO, contratti, helper idempotenza e correlazione
├─ tests/
│  ├─ Dusiburg.AI.O2C.Erp.Api.Tests/
│  ├─ Dusiburg.AI.O2C.Mcp.Tests/           # server MCP e CRM mock (M5)
│  ├─ Dusiburg.AI.O2C.Orchestrator.Tests/  # server MCP fake + modello stub, nessuna chiamata reale
│  └─ Dusiburg.AI.O2C.Web.Tests/           # Crm.Web ed Erp.Web con API a valle finte (M26)
├─ infra/                 # Bicep / azd
└─ README.md
```

Chiavi di configurazione (user\-secrets in locale, secret di Container Apps o Key Vault in cloud):

| Chiave | Scopo |
| --- | --- |
| `MODEL_PROVIDER` | `anthropic` (default) oppure `ollama` — vedi §3.3 (M23) |
| `ANTHROPIC_API_KEY`, `ANTHROPIC_MODEL` | Claude via API Anthropic: chiave (solo user-secrets o secret, M10) e modello, default `claude-sonnet-5` |
| `OLLAMA_ENDPOINT`, `OLLAMA_MODEL` | solo con `MODEL_PROVIDER=ollama`; default `http://localhost:11434` e `qwen3.5:9b` |
| `O2C_AGENT_MODE` | `multi` (default: Intake → Fulfillment → Order con handoff) oppure `single` (agente unico, per confronto) (M24) |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | in locale punta al dashboard Aspire |
| `ERP_MCP_URL`, `CRM_MCP_URL` | endpoint dei due server MCP |
| `HUBSPOT_TOKEN` | solo se si usa il CRM reale |
| `APPROVAL_THRESHOLD_EUR` | soglia di approvazione, default `10000` (confronto stretto) |
| `APPROVAL_TIMEOUT_HOURS` | scadenza richieste, default `24`; accetta i decimali per la demo (M12) |
| `APPROVAL_SWEEP_MINUTES` | intervallo di riconciliazione e scadenza, default `5`, minimo 10 secondi; da qui deriva anche la scadenza del possesso di una ripresa, almeno 5 minuti (M12) |
| `Approvals__ApproverUpn` | UPN registrato come decisore in locale, default `approver@dusiburg.local` (M12) |
| `TEAMS_WEBHOOK_URL` | canale di approvazione |
| `SERVICEBUS_CONNECTION` | topic `deal-closed-won` |
| `SQL_CONNECTION_STRING` | persistenza |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | tracing |

In locale l'AppHost passa database e broker ai servizi come connection string Aspire `ConnectionStrings:sql` e `ConnectionStrings:rabbitmq` (M2, M7), valorizzate negli user-secrets dell'AppHost.

## 11\. Piano di implementazione per fasi

Ogni fase è completa e verificabile prima di passare alla successiva. Le Fasi 1–6 si sviluppano e si verificano **interamente in locale** con l'AppHost Aspire di §3.2; la Fase 7 introduce il cloud. La Fase 6 è stata aggiunta al piano originale (M26).

**Fase 1 — Sistemi di base.** `Erp.Api` con modello dati, database creato da zero dal modello (nessuna migration, M17) e seed; collegamento al CRM (HubSpot o mock) verificato. \
*Accettazione*\: si crea un ordine e si legge una giacenza via HTTP; il deal di test è leggibile dal CRM. Nessun agente coinvolto.

**Fase 2 — Server MCP.** `Erp.Mcp` e `Crm.Mcp` che espongono i tool di §6. \
*Accettazione*\: entrambi i server rispondono a un client MCP di test con l'elenco corretto dei tool, e ogni tool restituisce dati coerenti ed errori strutturati.

**Fase 3 — Agente singolo.** Un solo agente che, dato un `dealId`, legge il deal, verifica lo stock e crea l'ordine — senza handoff né approvazione. \
*Accettazione*\: percorso felice completo end\-to\-end da riga di comando; ordine presente in ERP con `ExternalRef` corretto.

**Fase 4 — Multi\-agente.** Split in `IntakeAgent`, `FulfillmentAgent`, `OrderAgent` con Handoff orchestration. \
*Accettazione*\: i log/trace mostrano i due passaggi di handoff con il contesto trasferito; il risultato funzionale resta identico alla Fase 3.

**Fase 5 — Human\-in\-the\-loop.** `erp.create_order` come tool con approvazione richiesta, regole di §7 in una policy deterministica, persistenza di richieste e checkpoint, canale Teams o web, sospensione e ripresa del workflow. \
*Accettazione*\: un deal sopra soglia genera una richiesta pendente e **nessun** ordine; dopo approvazione l'ordine viene creato una sola volta; dopo rifiuto nessun ordine e il deal CRM riporta lo stato di rifiuto; un riavvio del processo durante l'attesa non perde il workflow.

**Fase 6 — UI dei sistemi (M26).** `Crm.Web` (deal, aziende, comandi Chiudi vinto / Chiudi perso) ed `Erp.Web` (clienti, magazzino, ordini ricevuti, sola lettura), Razor Pages sulle API di §6.3. \n*Accettazione*: da `Crm.Web` la chiusura vinta avvia il flusso e la pagina del deal ne mostra l'esito; l'ordine è visibile in `Erp.Web` con righe e backorder; la chiusura persa non avvia nulla; `Erp.Web` non scrive.

**Fase 7 — Deploy e osservabilità.** Bicep/azd, Container Apps, identità Entra dedicata, tracing OpenTelemetry end\-to\-end. \
*Accettazione*\: il flusso completo gira in Azure; in Application Insights una singola traccia correlata mostra la catena trigger → agenti → tool MCP → approvazione → creazione ordine.

## 12\. Requisiti non funzionali

- **Idempotenza**\: `idempotencyKey` derivata da `dealId` \+ revisione del deal. Un retry deve restituire l'ordine esistente, mai crearne un secondo (garantito anche a livello di indice univoco su DB).
- **Correlazione**\: ogni messaggio, chiamata MCP, richiesta di approvazione e riga di log porta lo stesso `CorrelationId`.
- **Tracing**\: ogni chiamata a tool produce uno span con attributi `agent.name`, `tool.name`, `correlation.id`, esito e durata.
- **Resilienza**\: il workflow sopravvive al riavvio del processo; le attese di approvazione non sono mai in memoria.
- **Sicurezza**\: nessun segreto nel repository; identità dedicata dell'agente con permessi minimi, distinta dall'identità dell'utente che ha innescato il flusso; nessuna azione di scrittura senza `CorrelationId` tracciato.
- **Autorizzazione a livello di tool**\: la disponibilità di un tool non implica l'autonomia nell'usarlo — la policy di approvazione è esplicita e centralizzata, non dispersa nei prompt.
- **Costi**\: il POC deve poter girare entro i crediti di una sottoscrizione di prova; scale\-to\-zero dove disponibile.

## 13\. Decisioni architetturali già prese

| Decisione | Motivazione |
| --- | --- |
| .NET / C\# per tutto lo stack | Agent Framework e MCP SDK sono .NET\-first; coerenza con le competenze del team |
| Due server MCP separati (ERP, CRM) | Dimostra l'interoperabilità reale su sistemi eterogenei e mantiene i confini di sicurezza distinti |
| Handoff orchestration invece di un agente unico | Rende visibile e governabile la specializzazione; topologia esplicita e ispezionabile |
| Orchestrazione self\-hosted, non Foundry Agent Service | Il meccanismo deve restare ispezionabile: è il valore dimostrativo del POC |
| ERP mock costruito internamente | Evita licenze e dipendenze esterne; il valore del POC è l'orchestrazione, non l'ERP |
| Solo `create_order` come tool sensibile | Mantiene l'approvazione mirata e credibile invece di bloccare ogni operazione |

## 14\. Fuori scope

Non implementare nel POC: fatturazione e pagamenti; gestione multi\-tenant; autenticazione degli utenti finali oltre all'approvatore; integrazione con un ERP di produzione reale; RAG e knowledge grounding su documentazione aziendale; interfaccia conversazionale per l'utente finale; agenti aggiuntivi oltre ai tre specificati.

## 15\. Glossario

| Termine | Significato |
| --- | --- |
| **MCP** (Model Context Protocol) | Standard aperto per esporre sistemi e dati come tool consumabili da qualunque agente |
| **A2A** (Agent\-to\-Agent) | Protocollo per far scoprire e collaborare agenti di framework o cloud diversi |
| **Handoff orchestration** | Pattern in cui gli agenti si trasferiscono il controllo lungo archi dichiarati |
| **ApprovalRequiredAIFunction** | Tool che il framework non invoca senza una risposta di approvazione: la chiamata viene esposta su una porta esterna del workflow e la risposta arriva in un run successivo |
| **ToolApprovalAgent** | Middleware dell'Agent Framework per le regole "non chiedere più" e la coda di richieste multiple: esiste, ma nel POC è fuori scope |
| **Checkpoint del workflow** | Fotografia dello stato di un run, scritta a ogni superstep su `orch.WorkflowCheckpoint`: è ciò da cui il workflow riprende, anche in un altro processo |
| **Human\-in\-the\-loop** | Punto di conferma umana obbligatoria prima di azioni ad alto impatto |
| **RAG** | Retrieval\-Augmented Generation, grounding del modello su dati aziendali |
| **Microsoft Agent Framework** | Framework .NET/Python, fusione di Semantic Kernel e AutoGen, GA da aprile 2026 |

## 16\. Fonti

- [Microsoft Agent Framework at Build 2026 — Agent Harness, Hosted Agents, CodeAct](https://devblogs.microsoft.com/agent-framework/microsoft-agent-framework-at-build-2026-announce/)
- [Microsoft Agent Framework Version 1.0](https://devblogs.microsoft.com/agent-framework/microsoft-agent-framework-version-1-0/)
- [Announcing v2.0 of the official MCP C\# SDK — .NET Blog](https://devblogs.microsoft.com/dotnet/announcing-v20-of-the-official-mcp-csharp-sdk/)
- [modelcontextprotocol/csharp\-sdk — GitHub](https://github.com/modelcontextprotocol/csharp-sdk)
- [Foundry Agent Service — Microsoft Azure](https://azure.microsoft.com/en-us/products/ai-foundry/agent-service)
- [Build and run agents at scale with Microsoft Foundry — Build 2026](https://devblogs.microsoft.com/foundry/agent-service-build2026/)
- [Exposing Teams to AI Agents (MCP) — Microsoft Learn](https://learn.microsoft.com/en-us/microsoftteams/platform/teams-sdk/in-depth-guides/ai-integrations/mcp-server)
- [MCP Enterprise Adoption: The July 2026 State of Play](https://andrew.ooo/answers/mcp-model-context-protocol-enterprise-adoption-july-2026/)
- [Ollama provider — Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/agents/providers/ollama)
- [OpenAI\-compatible endpoints — Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/integrations/openai-endpoints)
- [Agent based on any IChatClient — Microsoft Learn](https://learn.microsoft.com/en-us/agent-framework/user-guide/agents/agent-types/chat-client-agent)
- [Deploy Aspire apps to Azure Container Apps with azd](https://aspire.dev/deployment/azure/container-apps/)
- [Aspire Dashboard in Azure Container Apps — Microsoft Learn](https://learn.microsoft.com/en-us/azure/container-apps/aspire-dashboard)

## 17\. Registro delle modifiche

Modifiche rispetto alla versione iniziale di questo documento, decise in fase di pianificazione e all'inizio di ogni fase di sviluppo. Le modifiche con stato "Applicata" sono già riportate nelle sezioni indicate.

| ID | § | Modifica | Stato |
| --- | --- | --- | --- |
| M1 | §9 | Runtime .NET 10 (`net10.0`) invece di .NET 11 | Applicata (Fase 0) |
| M2 | §3.1, §3.2, §8, §9, §10 | Persistenza locale su SQL Server LocalDB `(localdb)\localdev`, database `O2C`, schemi `erp`/`crm`/`orch`, invece di SQLite | Applicata (Fase 0) |
| M3 | §6.2 | `get_deal` restituisce anche `revision` | Applicata (Fase 0) |
| M4 | §6.2 | `update_deal.status` è un enum chiuso | Applicata (Fase 0) |
| M5 | §10 | Progetto di test `tests/Dusiburg.AI.O2C.Mcp.Tests` | Applicata (Fase 0) |
| M6 | §9, §10 | CRM mock persistente dentro `Crm.Mcp` dietro `ICrmClient` | Applicata (Fase 0) |
| M7 | §3.1, §3.2, §9, §10 | Messaggistica locale su RabbitMQ (container in WSL tenuto attivo dall'AppHost), exchange `deal-closed-won` | Applicata (Fase 0) |
| M8 | §7, §9, §13, §15 | Meccanismo di approvazione: `erp.create_order` dichiarato `ApprovalRequiredAIFunction`, con la chiamata esposta su una porta esterna del workflow (`ToolApprovalRequestContent` / `ToolApprovalResponseContent`) e la policy applicata dall'host; `ToolApprovalAgent` non usato perché copre le regole "non chiedere più", fuori scope | Applicata (Fase 5) |
| M9 | §5, §7 | Regole di dominio: riga non disponibile e SKU inesistente, solo EUR, prezzo del deal, riserva dello stock | Applicata (Fase 0) |
| M10 | §10 | Autenticazione locale al modello con `ANTHROPIC_API_KEY` negli user-secrets (invece dell'eventuale `AZURE_OPENAI_API_KEY`) | Applicata (Fase 3) |
| M11 | §8, §10 | Progetto `src/Dusiburg.AI.O2C.Orchestration.Data` (DbContext `orch` condiviso con `Approvals.Web`); `WorkflowState` con PK `Id`, `CorrelationId` univoco, `(DealId, DealRevision)` univoco e lookup `WorkflowPhase`; schema creato da `DbInit` | Applicata (Fase 4) |
| M12 | §7, §8, §10 | Sospensione e ripresa: `ApprovalRequest` con PK numerica e `PublicId` GUID, `ReasonsJson` (più motivi), `TraceParent`, `CheckpointId` e `RowVersion`; lookup `ApprovalStatus` e `ApprovalReason`; checkpoint del workflow su `orch.WorkflowCheckpoint`; messaggio `approval-decided` come acceleratore più sweep di riconciliazione; rifiuto e scadenza chiusi dall'host senza riaprire il workflow; `APPROVAL_TIMEOUT_HOURS` con decimali, `APPROVAL_SWEEP_MINUTES`, `Approvals__ApproverUpn` | Applicata (Fase 5) |
| M13 | §10 | Eventuale `MESSAGING_PROVIDER=rabbitmq\|servicebus` | Da decidere (Gate Fase 7) |
| M14 | §10 | Repository `AI.POC-OrderToCash` (clone GitHub) invece di `o2c-agentic-poc` | Applicata (Fase 0) |
| M15 | §10 | Progetti, cartelle e namespace con root name `Dusiburg.AI.O2C` (es. `src/Dusiburg.AI.O2C.Erp.Api`), solution `Dusiburg.AI.O2C.slnx` | Applicata (dopo Fase 0) |
| M16 | §8 | Convenzioni di chiave: PK numerica `Id` e FK qualificate, niente PK GUID (`Order.PublicId` univoco), chiavi di business stringa come colonne univoche (`Product.Sku`, `Company.Code`, `Deal.Code`); contratti di §6 invariati | Applicata (Fase 1) |
| M17 | §8, §10 | Enum persistiti come FK verso tabelle di lookup con PK tinyint = valore esplicito dell'enum (`OrderStatus`, `DealStage`, `DealStatus`); nessuna migration: database creato da zero dal modello con `tools/Dusiburg.AI.O2C.DbInit`; modelli dati nei progetti `src/Dusiburg.AI.O2C.Erp.Data` e `src/Dusiburg.AI.O2C.Crm.Data` | Applicata (Fase 1) |
| M18 | §7, §8 | Nomi di tabella al singolare, senza pluralizzazioni (`Order`, `OrderStatus`, `Deal`, `ApprovalRequest`), anche nei nomi dei check constraint | Applicata (Fase 1) |
| M19 | §8, §10 | Dati demo da una tabella unica (`src/Dusiburg.AI.O2C.Shared/Demo/DemoCatalog.cs`) inseriti da `tools/Dusiburg.AI.O2C.DbInit`; endpoint `POST /dev/reset` (solo Development) su `Crm.Mcp` e anche su `Erp.Api`; errori HTTP come ProblemDetails con estensione `code` del catalogo `ToolErrorCodes` | Applicata (Fase 1) |
| M20 | §6.1 | `get_customer` restituisce `{ customer }`, con `customer: null` se il cliente non esiste (non è un errore) | Applicata (Fase 2) |
| M21 | §10 | Progetto `src/Dusiburg.AI.O2C.Mcp.Hosting` con l'infrastruttura comune dei server MCP (API key, filtro sulle chiamate ai tool, errori strutturati), referenziato solo da `Erp.Mcp` e `Crm.Mcp` | Applicata (Fase 2) |
| M22 | §6 | Errore di tool come risultato MCP `isError = true` con l'envelope `{ error: { code, message } }` come testo JSON; `structuredContent` solo per i risultati positivi | Applicata (Fase 2) |
| M23 | §3.1, §3.3, §9, §10 | Modello cloud Claude via API Anthropic (`claude-sonnet-5`, SDK `Anthropic` con `IChatClient`) invece di Azure OpenAI; `MODEL_PROVIDER=anthropic\|ollama`, chiavi `ANTHROPIC_API_KEY`/`ANTHROPIC_MODEL`, Ollama nativo Windows con `qwen3.5:9b` misurato senza criteri vincolanti; Claude in Microsoft Foundry come variante Azure per la Fase 7 (deploy, già Fase 6 prima di M26) | Applicata (Fase 3) |
| M24 | §5, §10 | Workflow a tre agenti con l'handoff di Agent Framework: modalità autonoma con limite di turni, tool locali di verdetto (`report_discarded`, `report_failed`) e terminazione sui fatti del run; esiti `Discarded`/`Failed` verificati e scritti sul CRM dall'orchestratore; `O2C_AGENT_MODE=multi\|single`; trigger RabbitMQ con consumer idempotente su deal e revisione | Applicata (Fase 4) |
| M25 | §6.1, §7, §8 | Le giacenze su cui decide la policy le verifica l'orchestratore sulle righe proposte, non l'agente; `create_order` restituisce `backorderNote` (cosa manca e in che quantità), conservata in `erp.Order.BackorderNote` e riportata come nota sul deal CRM. Emerso da un run dal vivo in cui `FulfillmentAgent` ha saltato `check_stock` e l'ordine è passato senza approvazione | Applicata (Fase 5) |
| M26 | §3.1, §3.2, §10, §11 | Nuova Fase 6 "UI dei sistemi" (il deploy diventa Fase 7): progetti `Crm.Web` (porta 5105) ed `Erp.Web` (5106), solo HTTP verso i servizi proprietari dei dati, e `tests/Dusiburg.AI.O2C.Web.Tests` | Applicata (Fase 6) |
| M27 | §3.1, §6.3 | API utente `/api/views` su `Crm.Mcp` ed `Erp.Api` e comando `POST /api/deals/{dealId}/close` (`Won` \| `Lost`) come ingresso del flusso; letture dev dei deal rimosse, `/dev/deals/{dealId}/close-won` resta per ripubblicare l'evento | Applicata (Fase 6) |
| M28 | §7 | Uno SKU inesistente scoperto dalla verifica dell'orchestratore ferma il workflow come `Failed` senza approvazione, anche se l'agente non l'ha segnalato; dopo un verdetto di arresto gli agenti non possono più chiamare tool di scrittura. Emerso da un run dal vivo su D-1007 finito in approvazione `InsufficientStock` | Applicata (Fase 6) |
| M29 | §5 | Gli agenti si scambiano fatti e non testo: il testo di un agente non entra nella richiesta al modello del successivo, mentre le sue chiamate ai tool e i risultati restano. Emerso dai run in cui una frase di `IntakeAgent` sul passaggio di mano faceva saltare `check_stock` a `FulfillmentAgent` | Applicata (Fase 6) |
| M30 | §5, §10 | Le istruzioni di ogni agente stanno in `Agents/Specs/<Nome>.agent.md`, incluse come risorse dell'assembly e non lette dal disco; allow-list dei tool, verdetti di arresto e topologia restano nel codice | Applicata (2026-09-18) |
| M31 | §7 | Regole di approvazione come elenco dichiarativo con nome e spiegazione, da cui si genera `docs/regole-di-approvazione.md`; un test lo tiene allineato. Le regole restano compilate, configurabile la sola soglia | Applicata (2026-09-18) |
