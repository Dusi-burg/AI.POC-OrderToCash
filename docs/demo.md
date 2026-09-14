# Demo — scenari e dati

> I dati demo nascono tutti da una tabella unica, `src/Dusiburg.AI.O2C.Shared/Demo/DemoCatalog.cs` (D5, D32). Da lì partono sia il seed dell'ERP (`ErpSeeder`) sia quello del CRM mock (`CrmSeeder`), così i due sistemi restano coerenti. I test `DemoCatalogTests` verificano che ogni deal corrisponda al proprio scenario.

## Preparazione

```powershell
dotnet run --project tools/Dusiburg.AI.O2C.DbInit     # ricrea O2C su (localdb)\localdev con i dati demo
dotnet run --project src/Dusiburg.AI.O2C.AppHost        # avvia i servizi (vedi README)
```

Per ripetere la demo senza ricreare il database (solo in Development):

| Servizio | Endpoint | Effetto |
|----------|----------|---------|
| Erp.Api | `POST http://localhost:5101/dev/reset` | Cancella gli ordini e ripristina clienti, prodotti e giacenze del seed; la numerazione degli ordini riparte da `SO-yyyy-000001` |
| Crm.Mcp | `POST http://localhost:5103/dev/reset` | Ripristina aziende e deal del seed: stage `ContractSent`, revisione 1, nessuno stato O2C né note |

Le richieste pronte sono in `src/Dusiburg.AI.O2C.Erp.Api/Erp.Api.http` e `src/Dusiburg.AI.O2C.Crm.Mcp/Crm.Mcp.dev.http`.

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
