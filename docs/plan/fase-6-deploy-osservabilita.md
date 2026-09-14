# Fase 6 — Deploy e osservabilità su Azure (OUTLINE)

> Indice: [plan.md](plan.md) · Precedente: [Fase 5](fase-5-human-in-the-loop.md)
>
> ⚠️ **Questo file è un outline (D4).** All'avvio della fase si chiude il gate, si dettagliano gli step e il file viene riscritto nel formato delle altre fasi.

## Obiettivo

Portare il flusso completo su Azure: Container Apps, Azure SQL serverless, Service Bus, identità Entra dedicata all'agente, approvazione via Teams, tracing end-to-end in Application Insights.

**Accettazione di §11**: il flusso completo gira in Azure; in Application Insights una singola traccia correlata mostra la catena trigger → agenti → tool MCP → approvazione → creazione ordine.

## Prerequisiti

- Fase 5 completata.
- Sottoscrizione Azure (trial o MSDN) con crediti sufficienti (§12 "Costi").
- Tenant Entra con permessi per app registration e role assignment.
- Tenant Microsoft 365 con Teams in cui installare un'app.
- Installazione di `az` e `azd`.

## Gate di fase (bozza)

| # | Domanda |
|---|---------|
| G6.1 | Sottoscrizione, region e resource group; budget e alert di costo |
| G6.2 | Teams: un incoming webhook consente solo card con link (`Action.OpenUrl` verso `/approvals`); pulsanti Approva/Rifiuta nella card (`Action.Execute`) richiedono un bot/Teams app con Azure Bot Service. Quale dei due? |
| G6.3 | Entra Agent ID: disponibile nel tenant? Altrimenti app registration + managed identity |
| G6.4 | Autenticazione dell'approvatore su `Approvals.Web` (OIDC Entra) e mappatura dell'UPN su `DecidedBy` |
| G6.5 | Selezione del broker: `MESSAGING_PROVIDER=rabbitmq\|servicebus` (M13) o solo Service Bus in cloud |
| G6.6 | Migrazioni in cloud: job dedicato di Container Apps o migrazione all'avvio con lock |
| G6.7 | Esposizione pubblica: solo `Approvals.Web` (e l'endpoint del bot) pubblici; server MCP ed `Erp.Api` interni all'ambiente |

## Macro-step previsti

1. **azd dall'AppHost**: `azd init` sul progetto Aspire; generazione e personalizzazione dei Bicep in `infra/`.
2. **Risorse**: ambiente Container Apps (scale-to-zero), Azure Container Registry, Azure SQL serverless (auto-pause), Service Bus (topic `deal-closed-won`, `approval-decided` e relative subscription), Key Vault, Log Analytics + Application Insights.
3. **Messaggistica**: implementazione Service Bus di `IDealEventSource` e del publisher delle decisioni; selezione da configurazione.
4. **Identità**: managed identity per ciascun servizio; identità dedicata dell'agente con permessi minimi (ruolo di utente OpenAI sul modello, accesso ai soli server MCP), distinta dall'identità dell'approvatore (§12); Entra Agent ID se disponibile.
5. **Segreti**: Key Vault / secret di Container Apps; nessuna API key nel repo; API key dei server MCP sostituite o affiancate da token Entra.
6. **Modello**: deployment Azure OpenAI/Foundry in cloud; autenticazione con managed identity.
7. **Approvazioni**: `Approvals.Web` con login Entra; canale Teams secondo G6.2, che scrive sullo stesso endpoint di callback della Fase 5.
8. **Osservabilità**: exporter Azure Monitor quando è presente `APPLICATIONINSIGHTS_CONNECTION_STRING`; verifica della traccia unica (propagazione `traceparent` su Service Bus e ripresa da `TraceParent`).
9. **Costi**: scale-to-zero, SQL auto-pause, alert di budget; spegnimento/`azd down` a fine demo.

## Criteri di accettazione (da §11)

- [ ] Il flusso completo (percorso felice e percorso con approvazione) gira in Azure.
- [ ] In Application Insights una singola traccia correlata mostra trigger → agenti → tool MCP → approvazione → creazione ordine.
- [ ] Identità dell'agente dedicata e con permessi minimi; nessun segreto nel repo.
- [ ] Costi entro i crediti della sottoscrizione.

## Esito

_Da compilare a fine fase._
