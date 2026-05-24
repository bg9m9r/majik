using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dig Through Time (Khans of Tarkir, {6}{U}{U}).
///
/// Instant. Oracle text:
///   "Delve (Each card you exile from your graveyard while casting this
///    spell pays for {1}.)
///    Look at the top seven cards of your library. Put two of them into
///    your hand and the rest on the bottom of your library in any order."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {6}{U}{U}.
/// - "Delve" marker keyword via <see cref="KeywordAbility"/> — the actual
///   mechanic lives in <see cref="Majik.Core.Costs.DelveCost"/>.
/// - On-resolve "Look at top 7, hand 2, bottom rest" effect via
///   <see cref="BuildResolveEffect"/>. The selector argument lets tests
///   (and future agent-driven plumbing) deterministically choose which
///   two cards go to hand and in what order the rest are placed at the
///   bottom — <see cref="IPlayerAgent"/> doesn't currently expose a
///   "look at N, pick K" prompt, so this seam mirrors the pattern that
///   the task brief calls out for the Delve graveyard selector.
///
/// ## Deferred (v1 gaps)
/// - Agent-driven choose-2 prompt. Bots auto-pick the first two
///   candidates; UI clients must build the selector themselves.
/// </summary>
[CardName("Dig Through Time")]
public static class DigThroughTimeFactory
{
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(name: "Dig Through Time", manaCost: "{6}{U}{U}");
        card.SetOwner(owner);
        card.SetController(owner);

        card.AddAbility(new KeywordAbility("Delve", card, owner));

        return card;
    }

    /// <summary>Selector signature: from the peeked 7 cards, choose
    /// (toHand, toBottomInOrder). Implementations must return cards that
    /// partition the input (no duplicates, no extras).</summary>
    public delegate (IReadOnlyList<ICard> toHand, IReadOnlyList<ICard> toBottom) DigSelector(
        IReadOnlyList<ICard> peeked);

    /// <summary>
    /// Default selector: first two cards to hand, remainder bottom in
    /// their peeked order. Deterministic so tests can assert specific
    /// cards moved.
    /// </summary>
    public static (IReadOnlyList<ICard> toHand, IReadOnlyList<ICard> toBottom)
        DefaultDigSelector(IReadOnlyList<ICard> peeked)
    {
        var hand = peeked.Take(2).ToList();
        var bottom = peeked.Skip(2).ToList();
        return (hand, bottom);
    }

    /// <summary>Build the resolution effect. Pass <see cref="DefaultDigSelector"/>
    /// for vanilla "first two" behavior.</summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster, DigSelector? selector = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        var pick = selector ?? DefaultDigSelector;
        return new IEffect[]
        {
            new Effect("Dig Through Time: peek 7, hand 2, bottom 5.", () =>
            {
                var lib = caster.Zones.Library;
                var peeked = lib.GetCards().Take(7).ToList();
                if (peeked.Count == 0) return;

                var (toHand, toBottom) = pick(peeked);

                // Move chosen to hand.
                foreach (var c in toHand)
                {
                    lib.RemoveCard(c);
                    caster.Zones.Hand.AddCard(c);
                    c.SetZone(ZoneType.Hand);
                }
                // Remaining cards: remove from current library position,
                // then re-append in the requested order so they end up
                // at the bottom in that order. Zone.AddCard appends —
                // matches the "bottom in any order" semantics (CR 701.18).
                foreach (var c in toBottom)
                {
                    lib.RemoveCard(c);
                }
                foreach (var c in toBottom)
                {
                    lib.AddCard(c);
                    c.SetZone(ZoneType.Library);
                }
            }),
        };
    }
}
