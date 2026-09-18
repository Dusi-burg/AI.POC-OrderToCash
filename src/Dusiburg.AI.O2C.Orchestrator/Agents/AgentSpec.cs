using System.Collections.Frozen;
using System.Reflection;

namespace Dusiburg.AI.O2C.Orchestrator.Agents;

/// <summary>
/// Specifica di un agente, letta dal file <c>&lt;Nome&gt;.agent.md</c> incluso come risorsa nell'assembly (D63).
/// <para>
/// Il file è un documento leggibile anche da chi non sviluppa: il titolo dà il nome dell'agente, la citazione
/// sotto il titolo la descrizione, e ogni <c>## Sezione</c> è un testo che il modello riceve. Fa eccezione
/// <c>## Note (non inviate al modello)</c>, che è commento per chi legge e non entra da nessuna parte.
/// </para>
/// <para>
/// Quello che tiene a freno l'agente — l'allow-list dei tool, il verdetto di arresto, la topologia del workflow —
/// resta nel codice: un refuso lì deve restare un errore di compilazione, non un guasto al primo deal.
/// </para>
/// </summary>
public sealed class AgentSpec
{
    public const string InstructionsSection = "Instructions";

    public const string HandoffSection = "Handoff";

    /// <summary>Sezione riservata alle note per chi legge: non viene mai inviata al modello.</summary>
    public const string NotesSection = "Note (non inviate al modello)";

    private const string ResourceSuffix = ".agent.md";

    private readonly FrozenDictionary<string, string> _sections;

    private AgentSpec(string name, string description, FrozenDictionary<string, string> sections)
    {
        Name = name;
        Description = description;
        _sections = sections;
    }

    public string Name { get; }

    public string Description { get; }

    public string Instructions => Section(InstructionsSection);

    /// <summary>Condizione d'uso del tool di handoff; assente per l'ultimo agente della catena.</summary>
    public string? Handoff => _sections.GetValueOrDefault(HandoffSection);

    /// <summary>Testo di una sezione obbligatoria.</summary>
    public string Section(string section) => _sections.TryGetValue(section, out var text)
        ? text
        : throw new InvalidOperationException($"La specifica di {Name} non ha la sezione '## {section}'.");

    /// <summary>Carica <c>&lt;agentName&gt;.agent.md</c> dalle risorse dell'assembly.</summary>
    public static AgentSpec Load(string agentName)
    {
        var resource = agentName + ResourceSuffix;
        var assembly = typeof(AgentSpec).Assembly;

        using Stream stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                $"Risorsa '{resource}' non trovata nell'assembly. Risorse presenti: {string.Join(", ", assembly.GetManifestResourceNames())}.");

        using var reader = new StreamReader(stream);

        return Parse(resource, reader.ReadToEnd());
    }

    /// <summary>Tutte le specifiche incluse nell'assembly, in ordine di nome.</summary>
    public static IReadOnlyList<AgentSpec> LoadAll() =>
    [
        .. typeof(AgentSpec).Assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => Load(name[..^ResourceSuffix.Length]))
    ];

    /// <summary>
    /// I fine riga si normalizzano a <c>\n</c> come fa il compilatore con i letterali grezzi: il testo inviato al
    /// modello non deve cambiare per il solo fatto di venire da un file salvato con CRLF.
    /// </summary>
    internal static AgentSpec Parse(string resource, string content)
    {
        string[] lines = content.ReplaceLineEndings("\n").Split('\n');

        string? name = null;
        var description = new List<string>();
        var sections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        string? current = null;

        foreach (var line in lines)
        {
            if (line.StartsWith("# ", StringComparison.Ordinal) && name is null)
            {
                name = line[2..].Trim();
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                current = line[3..].Trim();
                sections[current] = [];
            }
            else if (current is not null)
            {
                sections[current].Add(line);
            }
            else if (line.StartsWith("> ", StringComparison.Ordinal))
            {
                description.Add(line[2..].Trim());
            }
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException($"La specifica '{resource}' non ha il titolo '# NomeAgente' come prima riga.");
        }

        if (description.Count == 0)
        {
            throw new InvalidOperationException($"La specifica '{resource}' non ha la descrizione, la citazione '> ...' sotto il titolo.");
        }

        var texts = sections
            .Where(section => section.Key != NotesSection)
            .ToFrozenDictionary(section => section.Key, section => string.Join("\n", section.Value).Trim(), StringComparer.Ordinal);

        foreach (var (section, text) in texts)
        {
            if (text.Length == 0)
            {
                throw new InvalidOperationException($"La specifica '{resource}' ha la sezione '## {section}' vuota.");
            }
        }

        if (!texts.ContainsKey(InstructionsSection))
        {
            throw new InvalidOperationException($"La specifica '{resource}' non ha la sezione '## {InstructionsSection}'.");
        }

        return new AgentSpec(name, string.Join(" ", description), texts);
    }
}
