using System.Globalization;

namespace Dusiburg.AI.O2C.Orchestrator.Governance;

/// <summary>
/// Tempi delle sweep sulle approvazioni (§7, 5.5, 5.6). Il timeout accetta i decimali così una demo può scadere in un
/// minuto (<c>0.02</c>) senza toccare il codice (G5.5).
/// </summary>
public sealed record ApprovalSweepOptions(TimeSpan Timeout, TimeSpan Interval)
{
    public const string TimeoutSetting = "APPROVAL_TIMEOUT_HOURS";

    public const string IntervalSetting = "APPROVAL_SWEEP_MINUTES";

    public const double DefaultTimeoutHours = 24;

    public const double DefaultIntervalMinutes = 5;

    /// <summary>Sotto questo limite la sweep girerebbe di continuo: è il passo minimo accettato.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Attesa minima prima di considerare abbandonato il possesso di una ripresa. Deve stare molto sopra la durata di un
    /// run ripreso — con il modello locale è dell'ordine del minuto — altrimenti una ripresa lenta verrebbe riprovata
    /// mentre è ancora in corso, che è esattamente il doppione da evitare.
    /// </summary>
    public static readonly TimeSpan MinimumResumeClaimTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Dopo quanto un workflow rimasto in <c>Resuming</c> torna disponibile: è il caso del processo morto durante la
    /// ripresa. Generoso per costruzione, perché il rischio di attendere troppo è un ritardo, quello di attendere
    /// troppo poco è un ordine annunciato due volte.
    /// </summary>
    public TimeSpan ResumeClaimTimeout
    {
        get
        {
            var fromInterval = Interval * 5;

            return fromInterval > MinimumResumeClaimTimeout ? fromInterval : MinimumResumeClaimTimeout;
        }
    }

    public static ApprovalSweepOptions FromConfiguration(IConfiguration configuration)
    {
        var timeout = TimeSpan.FromHours(Read(configuration, TimeoutSetting, DefaultTimeoutHours));
        var interval = TimeSpan.FromMinutes(Read(configuration, IntervalSetting, DefaultIntervalMinutes));

        return new ApprovalSweepOptions(timeout, interval < MinimumInterval ? MinimumInterval : interval);
    }

    private static double Read(IConfiguration configuration, string key, double fallback) =>
        double.TryParse(configuration[key], CultureInfo.InvariantCulture, out var value) && value > 0 ? value : fallback;
}
