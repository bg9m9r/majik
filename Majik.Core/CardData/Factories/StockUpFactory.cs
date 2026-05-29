using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stock Up (Outlaws of Thunder Junction, {2}{U}).
///
/// Sorcery. Oracle text:
///   "Look at the top five cards of your library. Put two of them into your
///    hand and the rest on the bottom of your library in any order."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{U}.
/// - On-resolve "Look at top 5, hand 2, bottom rest" effect via
///   <see cref="BuildResolveEffect"/>. The selector argument lets tests
///   (and future agent-driven plumbing) deterministically choose which two
///   cards go to hand and in what order the rest are placed at the bottom —
///   <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> doesn't currently
///   expose a "look at N, pick K, reorder the rest" prompt, so this seam
///   mirrors the structural / textual twin
///   <see cref="DigThroughTimeFactory"/> (look-at-top-N, put 2 in hand,
///   rest on bottom in any order).
///
/// ## Notes
/// - "On the bottom of your library in any order" — CR 701.18. The default
///   selector preserves peek order; a custom selector may reorder the rest.
///
/// ## Deferred (v1 gaps)
/// - Agent-driven choose-2 / reorder prompt. Bots auto-pick the first two
///   candidates and bottom the rest in peek order; UI clients must build the
///   selector themselves (same posture as <see cref="DigThroughTimeFactory"/>).
/// </summary>
[CardName("Stock Up")]
public static class StockUpFactory
{
    public const string CardName = "Stock Up";
    public const string PrintedManaCost = "{2}{U}";
    private const int PeekAmount = 5;
    private const int HandAmount = 2;

    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(name: CardName, manaCost: PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>Selector signature: from the peeked cards, choose
    /// (toHand, toBottomInOrder). Implementations must return cards that
    /// partition the input (no duplicates, no extras).</summary>
    public delegate (IReadOnlyList<ICard> toHand, IReadOnlyList<ICard> toBottom) Selector(
        IReadOnlyList<ICard> peeked);

    /// <summary>
    /// Default selector: first two cards to hand, remainder bottom in their
    /// peeked order. Deterministic so tests can assert specific cards moved.
    /// Hands fewer than two when the library held fewer than two cards.
    /// </summary>
    public static (IReadOnlyList<ICard> toHand, IReadOnlyList<ICard> toBottom)
        DefaultSelector(IReadOnlyList<ICard> peeked)
    {
        var hand = peeked.Take(HandAmount).ToList();
        var bottom = peeked.Skip(HandAmount).ToList();
        return (hand, bottom);
    }

    /// <summary>Build the resolution effect. Pass <see cref="DefaultSelector"/>
    /// for vanilla "first two" behavior.</summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster, Selector? selector = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        var pick = selector ?? DefaultSelector;
        return new IEffect[]
        {
            new Effect("Stock Up: look at top 5, hand 2, bottom rest.", () =>
            {
                var lib = caster.Zones.Library;
                var peeked = lib.GetCards().Take(PeekAmount).ToList();
                if (peeked.Count == 0) return;

                var (toHand, toBottom) = pick(peeked);

                // Move chosen cards to hand.
                foreach (var c in toHand)
                {
                    lib.RemoveCard(c);
                    caster.Zones.Hand.AddCard(c);
                    c.SetZone(ZoneType.Hand);
                }

                // Remaining cards go on the bottom of the library in the
                // chosen order (CR 701.18). Remove each from its current
                // position, then re-append — Zone.AddCard appends, so the
                // first re-added card ends up highest among the bottomed
                // group and they land at the bottom in selector order.
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
