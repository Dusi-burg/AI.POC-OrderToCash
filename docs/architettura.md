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
| 6\. Governance trasversale | Identità, guardrail, controllo umano | Entra ID, ToolApprovalAgent, Teams/web approvals |

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
| Messaggistica | RabbitMQ in container Docker dentro WSL, vhost `o2c`, exchange `deal-closed-won` (M7) | Service Bus | No |
| Osservabilità | Aspire Dashboard (riceve OTLP nativamente) | Application Insights | No |
| Identità agente | API key locali | Entra ID / Entra Agent ID | No per funzionare |
| Approvazione umana | Pagina web `/approvals` | Teams Adaptive Card | No |
| **Modello (LLM)** | Ollama + modello locale (vedi §3.3) | Azure OpenAI / Foundry | **Unico nodo reale** |

### 3.2 Topologia locale con .NET Aspire

L'host locale di riferimento è un progetto **.NET Aspire AppHost** che compone in C# tutti i servizi — `Erp.Api`, `Erp.Mcp`, `Crm.Mcp`, `Orchestrator`, `Approvals.Web` e, opzionalmente, Ollama — gestendo service discovery e propagazione delle variabili d'ambiente fra loro. Database (LocalDB) e broker (RabbitMQ in WSL) entrano come **risorse esterne via connection string**, senza container gestiti da Aspire; l'AppHost tiene aperta una sessione WSL (`wsl -- docker start --attach rabbitmq`) perché il broker resti attivo mentre gira il POC (M2, M7).

Due conseguenze rilevanti per il progetto:

- Il dashboard Aspire riceve le tracce **OpenTelemetry** già cablate: il requisito di tracing di §12 è verificabile in locale, senza Application Insights.
- Lo stesso AppHost si deploya su Azure Container Apps con `azd up`: la Fase 6 di §11 diventa un comando, non una riscrittura. Le Fasi 1–5 si sviluppano quindi interamente in locale.

### 3.3 Modello: switch fra locale e cloud

L'Agent Framework è costruito sull'astrazione `IChatClient` di `Microsoft.Extensions.AI` e dispone di un provider Ollama documentato, oltre al supporto per qualunque endpoint OpenAI-compatibile. Il passaggio fra modello cloud e modello locale è quindi un cambio di configurazione, non di codice.

**Requisito implementativo**: l'orchestratore non deve avere dipendenze dirette da Azure OpenAI — deve dipendere solo da `IChatClient`, con il provider risolto da configurazione (`MODEL_PROVIDER`).

**Caveat tecnico**: il POC si regge su tool calling affidabile e output strutturato, che è esattamente la capacità dove i modelli piccoli (7–8B) diventano fragili — tool sbagliato, handoff mancato, JSON non conforme. Con un modello locale sottodimensionato si finisce a debuggare il modello invece dell'architettura. Servono modelli con tool calling solido (fascia 30B+ quantizzati in su) e hardware adeguato.

**Scelta operativa**: sviluppare con tutti i servizi in locale ma con l'endpoint del modello sul cloud (costo in token trascurabile per un POC), mantenendo il modello locale come variante configurabile da dimostrare — è un argomento commerciale concreto per clienti che non possono far uscire dati dal perimetro.

### 3.4 Cosa non è dimostrabile in locale

Tre elementi richiedono il cloud, tutti irrilevanti per costruire e testare, tutti rilevanti in una demo enterprise: l'**identità dell'agente** come cittadino di prima classe (Entra Agent ID); l'**approvazione via Teams**, i cui callback delle Adaptive Card richiedono un endpoint pubblicamente raggiungibile (in locale servirebbe un dev tunnel, motivo per cui la pagina `/approvals` è la scelta giusta in sviluppo); lo **scale-to-zero e il runtime gestito**. Tutti e tre entrano in Fase 6.

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
| `get_customer` | `vatNumber` oppure `email` (almeno uno) | `{ customerId, name, vatNumber, email, creditLimit, isBlocked }` o `null` |
| `create_customer` | `name`, `vatNumber`, `email`, `address` | `{ customerId }` |
| `check_stock` | `sku`, `quantity` | `{ sku, available: bool, onHand: int, leadTimeDays: int }` |
| `create_order` | `customerId`, `lines[{ sku, quantity, unitPrice }]`, `externalRef`, `idempotencyKey` | `{ orderId, orderNumber, total, status }` |
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

- Ogni tool ritorna **errori strutturati** (`{ error: { code, message } }`), mai eccezioni non gestite: l'agente deve poter ragionare sull'errore.
- Nessun tool esegue più di un'operazione di scrittura: la granularità è deliberatamente fine per rendere l'approvazione selettiva.
- `create_order` è l'unico tool marcato come **sensibile** e soggetto a intercettazione (§7).

## 7\. Human\-in\-the\-loop

L'approvazione umana è implementata con il middleware **`ToolApprovalAgent`** dell'Agent Framework, che intercetta le chiamate a tool sensibili e supporta regole di tipo "non chiedere più" per casi ripetitivi.

**Regole che richiedono approvazione** (valutate prima di `erp.create_order`):

- Totale ordine superiore a `APPROVAL_THRESHOLD_EUR` (default: 10.000 €).
- Almeno una riga con disponibilità insufficiente (`available = false`).
- Cliente non presente in ERP (creazione anagrafica contestuale).
- Cliente con `isBlocked = true` — in questo caso l'approvazione è l'unica strada possibile.

Semantica dei dati usati dalle regole (M9): una riga è disponibile se `OnHand − Reserved ≥ quantity`; `create_order` riserva lo stock (incrementa `Reserved`) nella stessa transazione che crea l'ordine; `creditLimit` non partecipa alle regole di approvazione.

**Canale**\: Adaptive Card in Teams con azioni Approva/Rifiuta; fallback per il POC, una pagina web `/approvals` che elenca le richieste pendenti. Entrambi scrivono sullo stesso endpoint di callback.

**Persistenza dello stato** — tabella `ApprovalRequests`\:

| Campo | Tipo | Note |
| --- | --- | --- |
| `Id` | GUID | chiave |
| `CorrelationId` | string | traccia l'intero workflow |
| `DealId` | string | riferimento CRM |
| `PayloadJson` | text | proposta di ordine completa, mostrata all'approvatore |
| `Reason` | string | quale regola ha scatenato l'approvazione |
| `Status` | enum | `Pending`, `Approved`, `Rejected`, `Expired` |
| `RequestedAt`, `DecidedAt` | datetime |  |
| `DecidedBy` | string | UPN dell'approvatore |
| `DecisionNote` | string | motivazione facoltativa |

**Comportamento richiesto**\: l'attesa dell'approvazione **non** deve tenere il processo agente in memoria. Alla generazione della richiesta il workflow si sospende e lo stato viene persistito; alla decisione il workflow riprende da dove era. Scadenza dopo `APPROVAL_TIMEOUT_HOURS` (default 24) con transizione a `Expired`, nessun ordine creato e notifica al richiedente.

## 8\. Modello dati del mock ERP

Database relazionale (Azure SQL in cloud, SQL Server LocalDB `(localdb)\localdev` in locale — M2) via EF Core, con seed di dati realistici (almeno 20 prodotti, 10 clienti, scorte miste per poter innescare sia il percorso felice sia quello di approvazione).

| Tabella | Campi principali |
| --- | --- |
| `Customers` | `CustomerId`, `Name`, `VatNumber`, `Email`, `Address`, `CreditLimit`, `IsBlocked` |
| `Products` | `Sku`, `Description`, `ListPrice`, `Uom` |
| `StockLevels` | `Sku`, `OnHand`, `Reserved`, `LeadTimeDays` |
| `Orders` | `OrderId`, `OrderNumber`, `CustomerId`, `Total`, `Status`, `ExternalRef`, `IdempotencyKey`, `CreatedAt` |
| `OrderLines` | `OrderLineId`, `OrderId`, `Sku`, `Quantity`, `UnitPrice` |
| `ApprovalRequests` | vedi §7 |
| `WorkflowState` | `CorrelationId`, `DealId`, `Phase`, `StateJson`, `UpdatedAt` |

Vincolo: indice univoco su `Orders.IdempotencyKey` — è il meccanismo che rende impossibile la creazione doppia di un ordine a fronte di un retry dell'agente.

Schemi (M2): le tabelle ERP stanno nello schema `erp` (DbContext di `Erp.Api`), `ApprovalRequests` e `WorkflowState` nello schema `orch` (Orchestrator), il CRM mock nello schema `crm` (`Crm.Mcp`). Stesso database `O2C`, DbContext separati.

## 9\. Stack tecnico

| Componente | Tecnologia (cloud) | In locale |
| --- | --- | --- |
| Linguaggio / runtime | C\# / .NET 10 (`net10.0`, LTS — M1; upgrade a .NET 11 successivo) | identico |
| Orchestrazione agenti | Microsoft Agent Framework (Handoff orchestration, ToolApprovalAgent) | identico |
| Tool layer | MCP C\# SDK (`ModelContextProtocol`, v2.x) | identico |
| Modello | Azure OpenAI / Foundry Models | Ollama via `IChatClient` (§3.3) |
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
│  ├─ AppHost/            # .NET Aspire: composizione locale di tutti i servizi
│  ├─ ServiceDefaults/    # OpenTelemetry, health check, service discovery, correlation id condivisi
│  ├─ Erp.Api/            # Minimal API + EF Core: il "gestionale" mock
│  ├─ Erp.Mcp/            # Server MCP sopra Erp.Api
│  ├─ Crm.Mcp/            # Server MCP + CRM mock persistente dietro ICrmClient (M6); adapter HubSpot opzionale
│  ├─ Orchestrator/       # Worker: agenti, handoff, ToolApprovalAgent
│  ├─ Approvals.Web/      # UI approvazioni + callback Teams
│  └─ Shared/             # DTO, contratti, helper idempotenza e correlazione
├─ tests/
│  ├─ Erp.Api.Tests/
│  ├─ Mcp.Tests/          # server MCP e CRM mock (M5)
│  └─ Orchestrator.Tests/ # server MCP fake + modello stub, nessuna chiamata reale
├─ infra/                 # Bicep / azd
└─ README.md
```

Chiavi di configurazione (user\-secrets in locale, secret di Container Apps o Key Vault in cloud):

| Chiave | Scopo |
| --- | --- |
| `MODEL_PROVIDER` | `azure-openai` (default) oppure `ollama` — vedi §3.3 |
| `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_DEPLOYMENT` | modello di ragionamento |
| `OLLAMA_ENDPOINT`, `OLLAMA_MODEL` | solo con `MODEL_PROVIDER=ollama` |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | in locale punta al dashboard Aspire |
| `ERP_MCP_URL`, `CRM_MCP_URL` | endpoint dei due server MCP |
| `HUBSPOT_TOKEN` | solo se si usa il CRM reale |
| `APPROVAL_THRESHOLD_EUR` | soglia di approvazione, default `10000` |
| `APPROVAL_TIMEOUT_HOURS` | scadenza richieste, default `24` |
| `TEAMS_WEBHOOK_URL` | canale di approvazione |
| `SERVICEBUS_CONNECTION` | topic `deal-closed-won` |
| `SQL_CONNECTION_STRING` | persistenza |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | tracing |

In locale l'AppHost passa database e broker ai servizi come connection string Aspire `ConnectionStrings:sql` e `ConnectionStrings:rabbitmq` (M2, M7), valorizzate negli user-secrets dell'AppHost.

## 11\. Piano di implementazione per fasi

Ogni fase è completa e verificabile prima di passare alla successiva. Le Fasi 1–5 si sviluppano e si verificano **interamente in locale** con l'AppHost Aspire di §3.2; la Fase 6 introduce il cloud.

**Fase 1 — Sistemi di base.** `Erp.Api` con modello dati, migrazioni e seed; collegamento al CRM (HubSpot o mock) verificato. \
*Accettazione*\: si crea un ordine e si legge una giacenza via HTTP; il deal di test è leggibile dal CRM. Nessun agente coinvolto.

**Fase 2 — Server MCP.** `Erp.Mcp` e `Crm.Mcp` che espongono i tool di §6. \
*Accettazione*\: entrambi i server rispondono a un client MCP di test con l'elenco corretto dei tool, e ogni tool restituisce dati coerenti ed errori strutturati.

**Fase 3 — Agente singolo.** Un solo agente che, dato un `dealId`, legge il deal, verifica lo stock e crea l'ordine — senza handoff né approvazione. \
*Accettazione*\: percorso felice completo end\-to\-end da riga di comando; ordine presente in ERP con `ExternalRef` corretto.

**Fase 4 — Multi\-agente.** Split in `IntakeAgent`, `FulfillmentAgent`, `OrderAgent` con Handoff orchestration. \
*Accettazione*\: i log/trace mostrano i due passaggi di handoff con il contesto trasferito; il risultato funzionale resta identico alla Fase 3.

**Fase 5 — Human\-in\-the\-loop.** `ToolApprovalAgent` su `erp.create_order`, regole di §7, persistenza richieste, canale Teams o web, sospensione e ripresa del workflow. \
*Accettazione*\: un deal sopra soglia genera una richiesta pendente e **nessun** ordine; dopo approvazione l'ordine viene creato una sola volta; dopo rifiuto nessun ordine e il deal CRM riporta lo stato di rifiuto; un riavvio del processo durante l'attesa non perde il workflow.

**Fase 6 — Deploy e osservabilità.** Bicep/azd, Container Apps, identità Entra dedicata, tracing OpenTelemetry end\-to\-end. \
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
| **ToolApprovalAgent** | Middleware dell'Agent Framework che intercetta i tool sensibili per l'approvazione |
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

Modifiche rispetto alla versione iniziale del documento (snapshot in `C:\Dev\Architettura`), decise nel gate socratico del piano (`docs/plan/plan.md`, decisioni D1–D25) e nei gate di fase. Le modifiche con stato "Applicata" sono già riportate nelle sezioni indicate.

| ID | § | Modifica | Stato |
| --- | --- | --- | --- |
| M1 | §9 | Runtime .NET 10 (`net10.0`) invece di .NET 11 | Applicata (Fase 0) |
| M2 | §3.1, §3.2, §8, §9, §10 | Persistenza locale su SQL Server LocalDB `(localdb)\localdev`, database `O2C`, schemi `erp`/`crm`/`orch`, invece di SQLite | Applicata (Fase 0) |
| M3 | §6.2 | `get_deal` restituisce anche `revision` | Applicata (Fase 0) |
| M4 | §6.2 | `update_deal.status` è un enum chiuso | Applicata (Fase 0) |
| M5 | §10 | Progetto di test `tests/Mcp.Tests` | Applicata (Fase 0) |
| M6 | §9, §10 | CRM mock persistente dentro `Crm.Mcp` dietro `ICrmClient` | Applicata (Fase 0) |
| M7 | §3.1, §3.2, §9, §10 | Messaggistica locale su RabbitMQ (container in WSL tenuto attivo dall'AppHost), exchange `deal-closed-won` | Applicata (Fase 0) |
| M8 | §7, §13 | Nome e semantica del meccanismo di approvazione (`ToolApprovalAgent`), da confermare con lo spike | Da decidere (Fase 5) |
| M9 | §5, §7 | Regole di dominio: riga non disponibile e SKU inesistente, solo EUR, prezzo del deal, riserva dello stock | Applicata (Fase 0) |
| M10 | §10 | Eventuale `AZURE_OPENAI_API_KEY` per l'autenticazione locale al modello | Da decidere (Gate Fase 3) |
| M11 | §10 | Eventuale progetto `src/Orchestration.Data` (DbContext `orch` condiviso con `Approvals.Web`) | Da decidere (Gate Fase 4) |
| M12 | §7 | Messaggio interno `approval-decided`, colonna `TraceParent` su `ApprovalRequests` | Da decidere (Gate Fase 5) |
| M13 | §10 | Eventuale `MESSAGING_PROVIDER=rabbitmq\|servicebus` | Da decidere (Gate Fase 6) |
| M14 | §10 | Repository `AI.POC-OrderToCash` (clone GitHub) invece di `o2c-agentic-poc` | Applicata (Fase 0) |
