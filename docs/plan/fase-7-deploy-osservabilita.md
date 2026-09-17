# Fase 7 — Deploy e osservabilità su Azure (OUTLINE)

> Indice: [plan.md](plan.md) · Precedente: [Fase 6](fase-6-ui-crm-erp.md)
>
> ⚠️ **Questo file è un outline (D4).** All'avvio della fase si chiude il gate, si dettagliano gli step e il file viene riscritto nel formato delle altre fasi.
>
> Fino al 2026-09-17 era la Fase 6: è diventata la 7 con l'inserimento della fase delle UI di CRM ed ERP (D57). Corrisponde alla Fase 6 di §11.

## Obiettivo

Portare il flusso completo su Azure: Container Apps, Azure SQL serverless, Service Bus, identità Entra dedicata all'agente, approvazione via Teams, tracing end-to-end in Application Insights.

**Accettazione di §11**: il flusso completo gira in Azure; in Application Insights una singola traccia correlata mostra la catena trigger → agenti → tool MCP → approvazione → creazione ordine.

## Prerequisiti

- Fase 6 completata (UI di CRM ed ERP).
- Sottoscrizione Azure (trial o MSDN) con crediti sufficienti (§12 "Costi").
- Tenant Entra con permessi per app registration e role assignment.
- Tenant Microsoft 365 con Teams in cui installare un'app.
- Installazione di `az` e `azd`.

## Gate di fase (bozza)

| # | Domanda |
|---|---------|
| G7.1 | Sottoscrizione, region e resource group; budget e alert di costo |
| G7.2 | Teams: un incoming webhook consente solo card con link (`Action.OpenUrl` verso `/approvals`); pulsanti Approva/Rifiuta nella card (`Action.Execute`) richiedono un bot/Teams app con Azure Bot Service. Quale dei due? |
| G7.3 | Entra Agent ID: disponibile nel tenant? Altrimenti app registration + managed identity |
| G7.4 | Autenticazione degli utenti (OIDC Entra): approvatore su `Approvals.Web` con mappatura dell'UPN su `DecidedBy`; operatori di `Crm.Web` (che può chiudere i deal) ed `Erp.Web`; protezione delle API di lettura `/api/views` e del comando `POST /api/deals/{dealId}/close` (in locale senza autenticazione, D58) |
| G7.5 | Selezione del broker: `MESSAGING_PROVIDER=rabbitmq\|servicebus` (M13) o solo Service Bus in cloud |
| G7.6 | Migrazioni in cloud: job dedicato di Container Apps o migrazione all'avvio con lock |
| G7.7 | Esposizione pubblica: solo le tre UI (`Approvals.Web`, `Crm.Web`, `Erp.Web`) e l'endpoint del bot pubblici; server MCP, `Erp.Api` e API di `Crm.Mcp` interni all'ambiente |

## Macro-step previsti

1. **azd dall'AppHost**: `azd init` sul progetto Aspire; generazione e personalizzazione dei Bicep in `infra/`.
2. **Risorse**: ambiente Container Apps (scale-to-zero), Azure Container Registry, Azure SQL serverless (auto-pause), Service Bus (topic `deal-closed-won`, `approval-decided` e relative subscription), Key Vault, Log Analytics + Application Insights.
3. **Messaggistica**: implementazione Service Bus di `IDealEventSource`, del publisher dei deal chiusi e del publisher delle decisioni; selezione da configurazione.
4. **Identità**: managed identity per ciascun servizio; identità dedicata dell'agente con permessi minimi (ruolo di utente sul modello, accesso ai soli server MCP), distinta dall'identità dell'approvatore e degli operatori delle UI (§12); Entra Agent ID se disponibile.
5. **Segreti**: Key Vault / secret di Container Apps; nessuna API key nel repo; API key dei server MCP sostituite o affiancate da token Entra.
6. **Modello**: Claude in Microsoft Foundry (M23) con autenticazione a managed identity, oppure API Anthropic con chiave in Key Vault.
7. **Approvazioni e UI**: login Entra su `Approvals.Web`, `Crm.Web` ed `Erp.Web` (G7.4); canale Teams secondo G7.2, che scrive sullo stesso endpoint di callback della Fase 5; link fra le UI dagli endpoint pubblici dell'ambiente.
8. **Osservabilità**: exporter Azure Monitor quando è presente `APPLICATIONINSIGHTS_CONNECTION_STRING`; verifica della traccia unica (propagazione `traceparent` su Service Bus e ripresa da `TraceParent`), a partire dal clic su "Chiudi vinto" in `Crm.Web`.
9. **Costi**: scale-to-zero, SQL auto-pause, alert di budget; spegnimento/`azd down` a fine demo.

## Criteri di accettazione (da §11)

- [ ] Il flusso completo (percorso felice e percorso con approvazione) gira in Azure, avviato da `Crm.Web`.
- [ ] In Application Insights una singola traccia correlata mostra trigger → agenti → tool MCP → approvazione → creazione ordine.
- [ ] Identità dell'agente dedicata e con permessi minimi; nessun segreto nel repo.
- [ ] Costi entro i crediti della sottoscrizione.

## Esito

_Da compilare a fine fase._
