using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ancient Stirrings (Rise of the Eldrazi, {G}).
///
/// Sorcery. Oracle text:
///   "Look at the top five cards of your library. You may reveal a
///    colorless card from among them and put it into your hand. Then
///    put the rest on the bottom of your library in a random order."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {G}.
/// - On-resolve effect via <see cref="BuildResolveEffect"/>:
///     1. Peek up to top 5 cards of the caster's library.
///     2. Run a selector to decide which (if any) of those is moved to
///        hand. The default selector picks the first colorless card per
///        <see cref="CardColors.GetColors(ICard)"/> (CR 105 — colour is
///        derived from coloured pips in the mana cost; empty set means
///        colourless). When no peeked card is colourless, no card moves
///        to hand.
///     3. Remaining peeked cards return to the bottom of the library.
///        The default selector shuffles those before re-appending so
///        the "random order" clause (CR 701.20a) is honoured.
///
/// ## Why a named factory (not template broaden)
/// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Library.ImpulseMayRevealFilterTemplate"/>
/// already matches Ancient Stirrings' oracle text by shape, but its v1
/// stub drops the type/colour filter — every peeked card is fair game
/// and it always picks the topmost. For Ancient Stirrings the filter is
/// the entire point of the card (colorless-only), and the bottom
/// reorder is explicitly random rather than caster-ordered. Wiring a
/// colour predicate into the shared template would change behaviour for
/// 6+ other cards that intentionally rely on the lossy stub. The named
/// factory carries the predicate locally and leaves the template
/// untouched.
///
/// ## Deferred (v1 gaps)
/// - Agent-driven "may reveal" prompt — the default selector always
///   reveals if a colourless card is present. The selector seam lets
///   tests / future agent wiring override this to model the "may"
///   opt-out (CR 116.1b).
/// - Bottom order is randomised via <see cref="Random.Shared"/>; once
///   the engine exposes a deterministic RNG seam for replay, this
///   should consume it instead.
/// </summary>
[CardName("Ancient Stirrings")]
public static class AncientStirringsFactory
{
    /// <summary>
    /// Construct Ancient Stirrings owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(name: "Ancient Stirrings", manaCost: "{G}");
        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }

    /// <summary>Selector signature: from the peeked up-to-5 cards, choose
    /// (toHand, toBottomInOrder). Implementations must return cards that
    /// partition the input — no duplicates, no extras. <c>toHand</c> is
    /// 0 or 1 cards; <c>toBottom</c> is the remainder in the order they
    /// should be re-appended to the bottom of the library.</summary>
    public delegate (IReadOnlyList<ICard> toHand, IReadOnlyList<ICard> toBottom) StirringsSelector(
        IReadOnlyList<ICard> peeked);

    /// <summary>
    /// Default selector: pick the first colourless card (CR 105) per
    /// <see cref="CardColors.GetColors(ICard)"/>; if none are colourless,
    /// no card moves to hand. Remaining cards are shuffled (CR 701.20a)
    /// before being placed at the bottom.
    /// </summary>
    public static (IReadOnlyList<ICard> toHand, IReadOnlyList<ICard> toBottom)
        DefaultStirringsSelector(IReadOnlyList<ICard> peeked)
    {
        ArgumentNullException.ThrowIfNull(peeked);

        ICard? colorless = null;
        foreach (var c in peeked)
        {
            if (CardColors.GetColors(c).Count == 0)
            {
                colorless = c;
                break;
            }
        }

        var toHand = colorless == null
            ? Array.Empty<ICard>()
            : new[] { colorless };

        var bottom = new List<ICard>(peeked.Count);
        foreach (var c in peeked)
        {
            if (!ReferenceEquals(c, colorless)) bottom.Add(c);
        }
        Shuffle(bottom);
        return (toHand, bottom);
    }

    /// <summary>Build the resolution effect. Pass <see cref="DefaultStirringsSelector"/>
    /// for the printed-card behaviour.</summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster, StirringsSelector? selector = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        var pick = selector ?? DefaultStirringsSelector;
        return new IEffect[]
        {
            new Effect(
                "Ancient Stirrings: look at top 5, may reveal a colorless card to hand, " +
                "rest to the bottom of the library in a random order.",
                () =>
                {
                    var lib = caster.Zones.Library;
                    var peeked = lib.GetCards().Take(5).ToList();
                    if (peeked.Count == 0) return;

                    var (toHand, toBottom) = pick(peeked);

                    // Move chosen (0 or 1) to hand.
                    foreach (var c in toHand)
                    {
                        lib.RemoveCard(c);
                        caster.Zones.Hand.AddCard(c);
                        c.SetZone(ZoneType.Hand);
                    }
                    // Re-bottom the rest. Zone.AddCard appends — appending in
                    // the (already-shuffled) toBottom order gives the random
                    // bottom placement required by the printed text.
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

    private static void Shuffle<T>(IList<T> list)
    {
        // Fisher-Yates via Random.Shared. Tests that need determinism
        // should pass a custom selector instead of relying on the default.
        var rng = System.Random.Shared;
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
