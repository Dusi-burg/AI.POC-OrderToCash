# Regole di approvazione

> **Documento generato**: non va modificato a mano. Nasce dalle regole che il sistema applica davvero
> (`ApprovalPolicy.Rules`), e un test lo rigenera se qualcuno le cambia senza aggiornarlo.

Preparato l'ordine, prima di crearlo il sistema controlla se la situazione richiede il permesso di una
persona. **Basta che scatti una di queste regole**; se ne scattano più d'una, compaiono tutte come motivo
della richiesta.

| Regola | Quando scatta |
|--------|---------------|
| **Importo sopra la soglia** | il totale dell'ordine supera la soglia, di base 10.000 €. Un ordine esattamente pari alla soglia passa senza approvazione |
| **Merce insufficiente** | per almeno una riga la merce disponibile non basta. L'ordine non viene ridotto: si crea per intero e va in arretrato |
| **Cliente nuovo** | il cliente non era presente nel gestionale ed è stato creato durante questa lavorazione |
| **Cliente bloccato** | il gestionale segna il cliente come bloccato. Qui l'approvazione è l'unica strada possibile |

## Cosa succede quando una regola scatta

Il lavoro **si ferma prima di creare l'ordine**: nel gestionale non compare nulla. La proposta viene
congelata così com'è e compare fra le richieste da approvare, con righe, totale, giacenze e cliente sotto
gli occhi di chi deve decidere.

Se la richiesta viene **approvata**, il lavoro riprende dal punto esatto in cui si era fermato e crea
l'ordine com'era stato proposto. Se viene **rifiutata**, o se scade, non viene creato nessun ordine e la
trattativa resta segnata di conseguenza. Fra la sospensione e la ripresa possono passare giorni: il
sistema può anche essere riavviato.

## La soglia

Di base è **10.000 €** e si può cambiare senza toccare il codice, con l'impostazione `APPROVAL_THRESHOLD_EUR`.
Il confronto è stretto: un ordine esattamente pari alla soglia **non** richiede approvazione.

Le regole in sé, invece, non sono configurabili: aggiungerne una, toglierla o cambiarne la condizione
richiede una modifica al codice, una compilazione e dei test. È voluto — è il punto in cui il sistema
decide se servono una firma e dei soldi, e non deve poter cambiare per errore.

## Su cosa vengono applicate

Non su quello che l'assistente automatico racconta, ma su dati riletti dai sistemi: il totale è
ricalcolato dalle righe proposte e le giacenze sono verificate dal sistema stesso, riga per riga, con le
quantità effettivamente richieste. Se una verifica non riesce, la riga conta come non disponibile: nel
dubbio si chiede un permesso in più, non uno in meno.
