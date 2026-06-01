using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Silundi Vision // Silundi Isle (Zendikar Rising, {2}{U}).
///
/// Instant. Oracle text (front, verified against Scryfall):
///   "Look at the top six cards of your library. You may reveal an instant
///    or sorcery card from among them and put it into your hand. Put the
///    rest on the bottom of your library in a random order."
///
/// Back face — <see cref="SilundiIsleFactory"/> (Land — "This land enters
/// tapped." / "{T}: Add {U}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Cast-either-face is modelled by two independent <c>[CardName]</c>-dispatched
/// factories — the same architecture as
/// <see cref="SinkIntoStuporFactory"/> / <see cref="SoporificSpringsFactory"/>
/// and <see cref="AgadeemsAwakeningFactory"/> /
/// <see cref="AgadeemTheUndercryptFactory"/>.
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost are loaded from the embedded JSON definition
/// (<c>silundi-vision.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The <see cref="MdfcState"/> face
/// tracker and the resolve-time dig behaviour are attached in code (the JSON
/// schema models neither MDFC faces nor look-at-top-N digs).
///
/// ## Implemented (v1)
///
/// - Instant identity at <c>{2}{U}</c>, mono-blue (one {U} pip),
///   owner / controller wired.
/// - <see cref="MdfcState"/> attached (front = "Silundi Vision",
///   back = "Silundi Isle"); starts on the front face.
/// - On-resolve dig via <see cref="BuildResolveEffect"/>:
///     <list type="bullet">
///       <item>Peeks the top six cards of the caster's library
///         (CR 701.21 — short library is fine).</item>
///       <item>Runs a selector to decide which (if any) of those is
///         revealed and moved to hand. The default selector picks the first
///         instant-or-sorcery card per <see cref="CardType.Instant"/> /
///         <see cref="CardType.Sorcery"/>. When no peeked card is an
///         instant or sorcery, no card moves to hand (the "you may" reveal
///         opt-out — CR 116.1b — is also exercised by passing a selector
///         that declines).</item>
///       <item>Bottoms the rest in a random order
///         (<see cref="GameRandom.Shuffle"/> from
///         <see cref="GameRandomRegistry.Get"/> — deterministic when tests
///         seed it), honouring the "random order" clause (CR 701.20a).</item>
///     </list>
///
/// ## Why a named factory (not template broaden)
///
/// Same rationale as <see cref="AncientStirringsFactory"/>: the
/// instant/sorcery type filter is the entire point of the card, and the
/// bottom reorder is explicitly random rather than caster-ordered. Wiring
/// the predicate into the shared dig template would change behaviour for
/// other cards that intentionally rely on the lossy stub; the named factory
/// carries the predicate locally.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Agent-driven "may reveal" prompt</b>: the default selector always
///   reveals if an instant/sorcery is present. The selector seam lets
///   tests / future agent wiring override this to model the "may" opt-out
///   (CR 116.1b) — same posture as <see cref="AncientStirringsFactory"/>.
/// - <b>Reveal event</b>: the peek does not publish a per-card reveal
///   event. Same gap as <see cref="AncientStirringsFactory"/> /
///   <see cref="TurntimberSymbiosisFactory"/>.
///
/// ## References
///
/// - <see cref="AncientStirringsFactory"/> — the look-at-top-N / may-reveal
///   filtered / bottom-rest-randomly body this directly cribs (swapping the
///   colourless filter for an instant/sorcery filter, 5 → 6).
/// - <see cref="SinkIntoStuporFactory"/> — companion instant//land MDFC
///   front face with the same MdfcState shape.
/// </summary>
[CardName("Silundi Vision")]
public static class SilundiVisionFactory
{
    public const string CardName = "Silundi Vision";
    public const string BackName = "Silundi Isle";
    public const int PeekCount = 6;

    /// <summary>
    /// Construct Silundi Vision as an Instant (identity from JSON) with the
    /// <see cref="MdfcState"/> face tracker attached. The resolve-time dig
    /// effect is built on demand via <see cref="BuildResolveEffect"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + printed cost come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource("silundi-vision");
        var card = (Instant)CardDefinitionFactory.Build(definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker so the printed
        // back-face name is observable from the front-face card object.
        // CR 712.3 / 712.4 — attach the MDFC face tracker WITH a castable
        // back-face descriptor (deferral #3, real cast-either-face). The
        // back face is the LAND back face played with no stack; MdfcCastFlow
        // offers the controller a face choice at cast time and materializes
        // a fresh back-face land instance when chosen. No transform happens.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, replacements) =>
                SilundiIsleFactory.Create(landOwner, replacements));
        card.MdfcState = new MdfcState(CardName, BackName, backFace);

        return card;
    }

    /// <summary>Selector signature: from the peeked up-to-6 cards, choose
    /// (toHand, toBottom). Implementations must return cards that partition
    /// the input — no duplicates, no extras. <c>toHand</c> is 0 or 1 cards;
    /// <c>toBottom</c> is the remainder in the order they should be
    /// re-appended to the bottom of the library.</summary>
    public delegate (IReadOnlyList<ICard> toHand, IReadOnlyList<ICard> toBottom) VisionSelector(
        IReadOnlyList<ICard> peeked);

    /// <summary>
    /// Default selector: pick the first instant-or-sorcery card
    /// (CR 205.2a); if none qualify, no card moves to hand. The remaining
    /// cards are placed at the bottom; the resolve effect applies the
    /// random ordering (CR 701.20a) so this selector returns them in their
    /// peeked order.
    /// </summary>
    public static (IReadOnlyList<ICard> toHand, IReadOnlyList<ICard> toBottom)
        DefaultVisionSelector(IReadOnlyList<ICard> peeked)
    {
        ArgumentNullException.ThrowIfNull(peeked);

        ICard? revealed = null;
        foreach (var c in peeked)
        {
            if (c.HasType(CardType.Instant) || c.HasType(CardType.Sorcery))
            {
                revealed = c;
                break;
            }
        }

        var toHand = revealed == null
            ? Array.Empty<ICard>()
            : new[] { revealed };

        var bottom = new List<ICard>(peeked.Count);
        foreach (var c in peeked)
        {
            if (!ReferenceEquals(c, revealed)) bottom.Add(c);
        }
        return (toHand, bottom);
    }

    /// <summary>
    /// Build the resolution effect. Pass <see cref="DefaultVisionSelector"/>
    /// for the printed-card behaviour. The bottom-of-library reorder is
    /// randomised via the caster's <see cref="GameRandom"/> (CR 701.20a);
    /// tests seed it via <see cref="GameRandomRegistry.SetDefault"/> for
    /// determinism.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster, VisionSelector? selector = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        var pick = selector ?? DefaultVisionSelector;
        return new IEffect[]
        {
            new Effect(
                "Silundi Vision: look at top six, may reveal an instant or sorcery card " +
                "to hand, rest to the bottom of the library in a random order.",
                () => Resolve(caster, pick)),
        };
    }

    private static void Resolve(Player caster, VisionSelector pick)
    {
        var lib = caster.Zones.Library;
        var peeked = lib.GetCards().Take(PeekCount).ToList();
        if (peeked.Count == 0) return;

        var (toHand, toBottom) = pick(peeked);

        // Move chosen (0 or 1) to hand.
        foreach (var c in toHand)
        {
            lib.RemoveCard(c);
            caster.Zones.Hand.AddCard(c);
            c.SetZone(ZoneType.Hand);
        }

        // Bottom the rest in a random order (CR 701.20a). Per-game RNG;
        // tests seed it for determinism.
        var remainder = toBottom.ToList();
        if (remainder.Count == 0) return;

        var rng = GameRandomRegistry.Get(caster);
        rng.Shuffle(remainder);

        // Library.AddCard appends to the bottom; remove-then-add each
        // remainder card so the new bottom order is the shuffled order.
        foreach (var c in remainder)
        {
            lib.RemoveCard(c);
        }
        foreach (var c in remainder)
        {
            lib.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }
}
