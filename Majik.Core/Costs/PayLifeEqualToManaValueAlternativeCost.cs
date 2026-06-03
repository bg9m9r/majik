using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Costs;

/// <summary>
/// CR 118.9 / CR 116.3a — "pay life equal to its mana value rather than pay
/// its mana cost." Bolas's Citadel's cast-from-top rider:
///
///   "You may play lands and cast spells from the top of your library. If you
///    cast a spell this way, pay life equal to its mana value rather than pay
///    its mana cost."
///
/// This is a pay-life-only alternative cost (no mana paid, no card pitched):
/// the life paid is the spell's mana value (CR 202.3 — the converted total of
/// the printed mana cost; X counts as the announced value once chosen). It
/// composes the existing <see cref="PayLifeIfControlSwampAlternativeCost"/>
/// pay-life-on-resolve pattern + the existing
/// <see cref="ManaCost.TotalValue"/> mana-value primitive — no new engine
/// mechanic.
///
/// Differences vs. <see cref="PayLifeIfControlSwampAlternativeCost"/>:
///   * The life amount is NOT a fixed constant — it is computed from the cast
///     card's mana value at announce / resolve time
///     (<see cref="LifeAmountFor"/>), so a {3}{B}{B}{B} spell costs 6 life, a
///     {0} spell costs 0 life (a legal no-op).
///   * No land-subtype gate — the only legality predicate is CR 119.4 (you
///     can't pay life you don't have): <see cref="CanCastFor"/> requires the
///     caster's life total to be at least the card's mana value.
///
/// CR 117.7 note: the X in an X-spell's mana value is chosen during casting
/// (CR 601.2e). When this alt cost is used the card's <see cref="ManaCost"/>
/// carries no resolved X (X = 0 in the printed cost), so the life paid is the
/// non-X portion. Bolas's Citadel's pool does not include cards with X in
/// their cost in the v1 deck context; the X-life interaction is a documented
/// boundary, not modelled here.
///
/// The cast goes onto the stack via
/// <see cref="Majik.Core.Game.SpellCastFlow"/> with
/// <see cref="AlternativeManaCost"/> = <see cref="ManaCost.Zero"/> (no mana
/// spent — so the spell is also a "free cast" for CR 118 mana-spent payoffs);
/// the life is paid in <see cref="OnResolved"/> (CR 118.8).
/// </summary>
public sealed class PayLifeEqualToManaValueAlternativeCost : IAlternativeCost
{
    public string Description => "Pay life equal to its mana value";

    /// <summary>No mana is paid — the life payment is the entire cost (CR 118.9).</summary>
    public ManaCost AlternativeManaCost => ManaCost.Zero;

    /// <summary>
    /// CR 116.3a / CR 202.3 — the life that will be paid for
    /// <paramref name="card"/>: its mana value (the converted total of its
    /// printed mana cost). A card with no mana cost (or {0}) yields 0.
    /// </summary>
    public static int LifeAmountFor(ICard card)
    {
        if (card is Card c) return c.ManaCostValue.TotalValue;
        return 0;
    }

    /// <summary>
    /// CR 118.9 — the alternative cost is available only if the caster has
    /// enough life to pay the spell's mana value (CR 119.4). The cast-source
    /// legality (the card must be the top of the caster's library under a live
    /// Bolas's Citadel grant) is enforced separately by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/>'s CR 601.3e check — this
    /// predicate only answers the life-affordability half.
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        if (card == null || caster == null) return false;
        return caster.LifeTotal >= LifeAmountFor(card);
    }

    /// <summary>
    /// Pay the life after resolution (CR 118.8). Routes through
    /// <see cref="Player.LoseLife"/> so any life-loss replacement / triggers
    /// fire. Mirrors <see cref="PayLifeIfControlSwampAlternativeCost.OnResolved"/>.
    /// </summary>
    public void OnResolved(ICard card, Player caster)
    {
        if (card == null || caster == null) return;
        var life = LifeAmountFor(card);
        if (life > 0) caster.LoseLife(life);
    }
}
