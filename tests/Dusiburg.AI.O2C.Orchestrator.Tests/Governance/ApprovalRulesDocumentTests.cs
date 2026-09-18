using System.Globalization;
using System.Text;
using Dusiburg.AI.O2C.Orchestrator.Governance;

namespace Dusiburg.AI.O2C.Orchestrator.Tests.Governance;

/// <summary>
/// Tiene <c>docs/regole-di-approvazione.md</c> allineato a <see cref="ApprovalPolicy.Rules"/> (D64).
/// <para>
/// Il documento non è scritto a mano: nasce dalla stessa lista che decide, quindi non può raccontare regole diverse da
/// quelle applicate. Se qualcuno aggiunge, toglie o riscrive una regola senza rigenerarlo, questo test riscrive il file
/// e fallisce: la differenza si vede in git e si committa insieme al cambio di codice.
/// </para>
/// </summary>
public class ApprovalRulesDocumentTests
{
    private const string DocumentPath = "docs/regole-di-approvazione.md";

    [Test]
    public void TheDocument_TellsExactlyTheRulesThatAreApplied()
    {
        //SETUP
        var file = RepositoryFile(DocumentPath);
        var expected = Render();

        //SUT
        var actual = File.Exists(file) ? File.ReadAllText(file) : string.Empty;

        if (!string.Equals(Normalize(actual), Normalize(expected), StringComparison.Ordinal))
        {
            File.WriteAllText(file, expected, new UTF8Encoding(false));

            Assert.Fail(
                $"{DocumentPath} non corrispondeva alle regole di ApprovalPolicy ed è stato rigenerato. " +
                "Rileggi la differenza in git e committala insieme alla modifica delle regole.");
        }
    }

    [Test]
    public void EveryRule_HasAReadableExplanation()
    {
        //SUT
        IReadOnlyList<ApprovalRule> rules = ApprovalPolicy.Rules;

        Assert.That(rules, Is.Not.Empty);
        Assert.That(rules.Select(rule => rule.Title), Has.All.Matches<string>(t => t.Length > 3), "ogni regola ha un nome");
        Assert.That(rules.Select(rule => rule.When), Has.All.Matches<string>(w => w.Length > 20), "e una frase che dice quando scatta");
        Assert.That(rules.Select(rule => rule.Reason), Is.Unique, "un motivo per regola");
    }

    /// <summary>Il documento, composto dalle regole. Il testo attorno alla tabella è l'unica parte fissa.</summary>
    private static string Render()
    {
        var threshold = ApprovalPolicy.DefaultThresholdEur.ToString("N0", CultureInfo.GetCultureInfo("it-IT"));
        var builder = new StringBuilder();

        builder.AppendLine("# Regole di approvazione");
        builder.AppendLine();
        builder.AppendLine("> **Documento generato**: non va modificato a mano. Nasce dalle regole che il sistema applica davvero");
        builder.AppendLine("> (`ApprovalPolicy.Rules`), e un test lo rigenera se qualcuno le cambia senza aggiornarlo.");
        builder.AppendLine();
        builder.AppendLine("Preparato l'ordine, prima di crearlo il sistema controlla se la situazione richiede il permesso di una");
        builder.AppendLine("persona. **Basta che scatti una di queste regole**; se ne scattano più d'una, compaiono tutte come motivo");
        builder.AppendLine("della richiesta.");
        builder.AppendLine();
        builder.AppendLine("| Regola | Quando scatta |");
        builder.AppendLine("|--------|---------------|");

        foreach (ApprovalRule rule in ApprovalPolicy.Rules)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"| **{rule.Title}** | {rule.When} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Cosa succede quando una regola scatta");
        builder.AppendLine();
        builder.AppendLine("Il lavoro **si ferma prima di creare l'ordine**: nel gestionale non compare nulla. La proposta viene");
        builder.AppendLine("congelata così com'è e compare fra le richieste da approvare, con righe, totale, giacenze e cliente sotto");
        builder.AppendLine("gli occhi di chi deve decidere.");
        builder.AppendLine();
        builder.AppendLine("Se la richiesta viene **approvata**, il lavoro riprende dal punto esatto in cui si era fermato e crea");
        builder.AppendLine("l'ordine com'era stato proposto. Se viene **rifiutata**, o se scade, non viene creato nessun ordine e la");
        builder.AppendLine("trattativa resta segnata di conseguenza. Fra la sospensione e la ripresa possono passare giorni: il");
        builder.AppendLine("sistema può anche essere riavviato.");
        builder.AppendLine();
        builder.AppendLine("## La soglia");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"Di base è **{threshold} €** e si può cambiare senza toccare il codice, con l'impostazione `{ApprovalPolicy.ThresholdSetting}`.");
        builder.AppendLine("Il confronto è stretto: un ordine esattamente pari alla soglia **non** richiede approvazione.");
        builder.AppendLine();
        builder.AppendLine("Le regole in sé, invece, non sono configurabili: aggiungerne una, toglierla o cambiarne la condizione");
        builder.AppendLine("richiede una modifica al codice, una compilazione e dei test. È voluto — è il punto in cui il sistema");
        builder.AppendLine("decide se servono una firma e dei soldi, e non deve poter cambiare per errore.");
        builder.AppendLine();
        builder.AppendLine("## Su cosa vengono applicate");
        builder.AppendLine();
        builder.AppendLine("Non su quello che l'assistente automatico racconta, ma su dati riletti dai sistemi: il totale è");
        builder.AppendLine("ricalcolato dalle righe proposte e le giacenze sono verificate dal sistema stesso, riga per riga, con le");
        builder.AppendLine("quantità effettivamente richieste. Se una verifica non riesce, la riga conta come non disponibile: nel");
        builder.AppendLine("dubbio si chiede un permesso in più, non uno in meno.");

        return builder.ToString();
    }

    private static string Normalize(string text) => text.ReplaceLineEndings("\n").TrimEnd();

    /// <summary>Percorso di un file del repository, risalendo dalla cartella di esecuzione fino alla solution.</summary>
    private static string RepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Dusiburg.AI.O2C.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            Assert.Ignore("Radice del repository non trovata: il test ha senso solo con i sorgenti presenti.");
        }

        return Path.Combine(directory!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
