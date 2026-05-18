using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.21 — Ward {cost}: "Whenever this becomes the target of a spell
/// or ability an opponent controls, counter that spell or ability unless
/// its controller pays {cost}."
///
/// Implemented as a triggered ability (Phase 17 replacement infrastructure
/// could also model it, but Ward technically TRIGGERS, then resolves with
/// optional payment). This MVP exposes a check helper that callers (spell
/// resolution path) invoke before applying effects.
/// </summary>
public sealed class WardEffect
{
    public Creature Source { get; }
    public Majik.Core.ValueObjects.ManaCost Cost { get; }

    public WardEffect(Creature source, Majik.Core.ValueObjects.ManaCost cost)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Cost = cost ?? throw new ArgumentNullException(nameof(cost));
    }

    /// <summary>
    /// Return true if the spell is countered (caster didn't pay the ward cost).
    /// </summary>
    public bool ResolvesWard(Player caster, bool casterPaidWardCost)
    {
        // Ward only triggers on opponent-controlled spells/abilities.
        if (ReferenceEquals(Source.Controller, caster)) return false;
        return !casterPaidWardCost;
    }
}
