using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// Generic "cast this card from exile" alternative cost. Used by Suspend,
/// Foretell, Cascade, Adventure follow-ups, Plot, Impulse-draw effects,
/// etc. By default the card resolves into its normal destination
/// (graveyard for instant/sorcery, battlefield for permanent) — same as
/// any other spell.
///
/// <para>The optional <see cref="IsSuspendCast"/> flag is set <c>true</c>
/// when this alt-cost represents the CR 702.62d "cast for free" payoff
/// triggered by <see cref="SuspendedCardRegistry"/>. Downstream
/// consumers (the cast flow's haste-grant for suspended creatures;
/// trigger gates that branch on "cast from suspend") read the flag off
/// the alt-cost or off the resulting
/// <see cref="Majik.Core.Spells.Spell.WasCastFromSuspend"/> /
/// <see cref="ICard.WasCastFromSuspend"/> stamps the flow propagates.</para>
/// </summary>
public sealed class CastFromExileAlternativeCost : IAlternativeCost
{
    public string Description { get; }
    public ManaCost AlternativeManaCost { get; }

    /// <summary>
    /// CR 702.62d — true iff this alt-cost is the suspend "cast without
    /// paying its mana cost" payoff. Used by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> to stamp the spell +
    /// resolving card as cast-from-suspend so the creature-suspend haste
    /// rider (CR 702.62g) and any future "if cast via suspend" gates can
    /// observe it without re-deriving the path.
    /// </summary>
    public bool IsSuspendCast { get; }

    public CastFromExileAlternativeCost(string description, ManaCost cost)
        : this(description, cost, isSuspendCast: false) { }

    public CastFromExileAlternativeCost(string description, ManaCost cost, bool isSuspendCast)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
        AlternativeManaCost = cost ?? throw new ArgumentNullException(nameof(cost));
        IsSuspendCast = isSuspendCast;
    }

    public bool CanCastFor(ICard card, Player caster) =>
        card.Zone == ZoneType.Exile && ReferenceEquals(card.Owner, caster);

    public void OnResolved(ICard card, Player caster) { /* default destination */ }
}
