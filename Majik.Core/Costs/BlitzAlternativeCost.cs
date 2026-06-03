using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.152 — Blitz. "Blitz—[cost]" is an alternative cost (CR 702.152a)
/// that lets a player cast a creature card for its blitz cost rather than its
/// mana cost. CR 702.152b — a creature cast for its blitz cost gains haste and
/// "When this creature dies, draw a card", and its controller sacrifices it at
/// the beginning of the next end step. CR 702.152c — those three riders only
/// apply when blitz was actually paid.
///
/// Casting via this alternative cost:
///   1. Replaces the spell's printed mana cost with
///      <see cref="AlternativeManaCost"/> at cost-determination time.
///   2. On resolution, sets <see cref="Creature.BlitzWasPaid"/> on the spell's
///      Creature so the three printed blitz riders (added generically by
///      <see cref="Majik.Core.Keywords.BlitzFactory"/>) all see a true gate
///      when they evaluate after the card enters the battlefield. Mirror of
///      <see cref="EvokeAlternativeCost"/>'s <c>EvokeWasPaid</c> flip.
///
/// The blitz riders themselves are NOT registered by this cost — they are
/// printed abilities on the creature (see <see cref="Majik.Core.Keywords.BlitzFactory"/>),
/// each gated on <see cref="Creature.BlitzWasPaid"/>. The cost only flips the
/// flag the riders read.
///
/// ## Cast zone
/// Most blitz cards are cast from hand. Tenacious Underdog additionally reads
/// "You may cast this card from your graveyard using its blitz ability", so
/// its blitz alt-cost is legal from the graveyard. The legal source zone is
/// captured in <see cref="SourceZone"/>; use <see cref="FromHand"/> /
/// <see cref="FromGraveyard"/> to build the right variant.
///
/// ## Bundled life payment
/// Tenacious Underdog's blitz is "Blitz—{2}{B}{B}, Pay 2 life." Only the mana
/// portion rides here ({2}{B}{B}); the "Pay 2 life" additional cost rides as a
/// <see cref="PayLifeAdditionalCost"/> fed alongside this alt-cost through
/// <see cref="Majik.Core.Game.SpellCastFlow"/> so it is paid as part of casting
/// (CR 601.2f), exactly like Escape's exile rider or Kicker.
/// </summary>
public sealed class BlitzAlternativeCost : IAlternativeCost
{
    /// <summary>Mana portion of the blitz cost (e.g. <c>{2}{B}{B}</c> for
    /// Tenacious Underdog — printed mana cost {1}{B}, blitz cost {2}{B}{B}).</summary>
    public ManaCost AlternativeManaCost { get; }

    /// <summary>The zone the card must be in for this blitz cost to be legal.
    /// Hand for the generic case; Graveyard for Tenacious Underdog
    /// ("cast this card from your graveyard using its blitz ability").</summary>
    public ZoneType SourceZone { get; }

    public string Description => $"Blitz {AlternativeManaCost}";

    private BlitzAlternativeCost(ManaCost blitzManaCost, ZoneType sourceZone)
    {
        AlternativeManaCost = blitzManaCost ?? throw new ArgumentNullException(nameof(blitzManaCost));
        SourceZone = sourceZone;
    }

    /// <summary>Build a blitz cost cast from the owner's hand (the default —
    /// most blitz creatures).</summary>
    public static BlitzAlternativeCost FromHand(ManaCost blitzManaCost) =>
        new(blitzManaCost, ZoneType.Hand);

    /// <summary>Build a blitz cost cast from the owner's graveyard
    /// (Tenacious Underdog — "cast this card from your graveyard using its
    /// blitz ability").</summary>
    public static BlitzAlternativeCost FromGraveyard(ManaCost blitzManaCost) =>
        new(blitzManaCost, ZoneType.Graveyard);

    /// <summary>
    /// Blitz is announced at the same step as a normal cast (CR 601.2b), so the
    /// spell's card must still be in its legal source zone and owned by the
    /// caster (only the card's owner may cast it via blitz — CR 702.152a).
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        if (card.Zone != SourceZone) return false;
        if (!ReferenceEquals(card.Owner, caster)) return false;
        return true;
    }

    /// <summary>
    /// CR 702.152b — flip <see cref="Creature.BlitzWasPaid"/> so the three
    /// printed blitz riders (haste, dies-draw, delayed end-step sacrifice) have
    /// a true gate when they evaluate after the card enters the battlefield.
    /// Runs during the alt-cost cleanup pass, before
    /// <see cref="Majik.Core.Services.StackResolver"/> moves the card to the
    /// battlefield and fires its ETB event (same ordering Evoke relies on).
    /// </summary>
    public void OnResolved(ICard card, Player caster)
    {
        if (card is Creature creature)
        {
            creature.BlitzWasPaid = true;
        }
    }
}
