using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Formats.Commander;

/// <summary>
/// CR 903 — per-player Commander state. Tracks the commander card,
/// commander tax (number of times cast from the command zone), and
/// combat damage dealt by each opposing commander (CR 903.10a — 21+
/// damage from a single commander = lose).
/// </summary>
public sealed class CommanderState
{
    public Player Owner { get; }
    public ICard Commander { get; }

    /// <summary>Times this commander has been cast from the command zone.</summary>
    public int CastsFromCommandZone { get; private set; }

    /// <summary>Commander damage taken by Owner, keyed by attacking commander.</summary>
    public Dictionary<ICard, int> CommanderDamageTaken { get; } = new();

    public CommanderState(Player owner, ICard commander)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Commander = commander ?? throw new ArgumentNullException(nameof(commander));
    }

    /// <summary>
    /// CR 903.8 — additional {2} for each previous cast from command zone.
    /// Returns the surcharge (in generic mana) on top of the printed cost.
    /// </summary>
    public int CommanderTaxSurcharge() => 2 * CastsFromCommandZone;

    public void NotifyCastFromCommandZone() => CastsFromCommandZone++;

    /// <summary>Mark N combat damage dealt by <paramref name="commander"/>
    /// to this state's owner.</summary>
    public void TakeCommanderDamage(ICard commander, int amount)
    {
        if (amount <= 0) return;
        CommanderDamageTaken.TryGetValue(commander, out var cur);
        CommanderDamageTaken[commander] = cur + amount;
    }

    /// <summary>True if any single opposing commander has dealt 21+ damage.</summary>
    public bool HasLostToCommanderDamage() =>
        CommanderDamageTaken.Values.Any(n => n >= 21);
}
