using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Monumental Henge (Murders at Karlov Manor
/// Commander / The Big Score — reprinted into Modern-legal sets).
///
/// Land. Oracle text (verified against Scryfall):
///   "This land enters tapped unless you control a Plains.
///    {T}: Add {W}.
///    {2}{W}{W}, {T}: Look at the top five cards of your library. You may
///    reveal a historic card from among them and put it into your hand. Put
///    the rest on the bottom of your library in a random order. (Artifacts,
///    legendaries, and Sagas are historic.)"
///
/// ## Implemented (v1)
/// - <b>Land identity + {T}: Add {W}</b> — the card shape (name, Land type)
///   and the white mana ability are data-driven, loaded from
///   <c>Majik.Core/CardData/Cards/monumental-henge.json</c> via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built by
///   <see cref="CardDefinitionFactory"/> (same posture as
///   <see cref="TempleOfMaliceFactory"/>).
/// - <b>ETB tapped unless you control a Plains (CR 614.1c)</b> — registered
///   as a <see cref="ConditionalEntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Predicate: the land enters untapped iff
///   the controller controls another land (excluding this one) with the
///   Plains subtype. Mirrors the single-subtype branch
///   <see cref="CheckLandCycleFactory"/> uses (the engine canonicalises on
///   subtype matching, so any Plains — basic or typed dual — qualifies,
///   matching the printed oracle). The single-arg dispatcher path omits the
///   replacement (shape-only posture shared by every ETB-replacement
///   factory); the full overload wires it when a bus is supplied.
/// - <b>{2}{W}{W}, {T}: look at top 5, may reveal a historic card to hand,
///   rest to the bottom in a random order</b> — an
///   <see cref="ActivatedAbility"/> whose resolution body is supplied in
///   code (the JSON ability schema does not model this multi-step library
///   manipulation). Built via the same selector seam as
///   <see cref="AncientStirringsFactory"/>; the default selector picks the
///   first <em>historic</em> card. Per the reminder text, historic =
///   Artifact (CR 205.2b) OR Legendary supertype (CR 205.4) OR a Saga
///   subtype (CR 714). Remaining cards are shuffled before being re-bottomed
///   so the "random order" clause (CR 701.20a) is honoured.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven "may reveal" prompt</b> — the default selector always
///   reveals if a historic card is present. The selector seam lets tests /
///   future agent wiring model the "may" opt-out (CR 116.1b). Same posture
///   as <see cref="AncientStirringsFactory"/>.
/// - Bottom order is randomised via <see cref="System.Random.Shared"/>; once
///   the engine exposes a deterministic RNG seam for replay, this should
///   consume it instead.
/// </summary>
[CardName("Monumental Henge")]
public static class MonumentalHengeFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("monumental-henge");

    /// <summary>
    /// Construct Monumental Henge owned and controlled by
    /// <paramref name="owner"/>. Single-arg dispatcher path — no
    /// <see cref="ReplacementBus"/> wired, so the ETB-tapped-unless-Plains
    /// replacement is omitted (shape-only posture). The {T}: Add {W} mana
    /// ability and the {2}{W}{W}, {T}: look-at-top-five activated ability are
    /// always attached.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Monumental Henge with an optional <see cref="ReplacementBus"/>
    /// for full ETB-tapped wiring. When <paramref name="replacements"/> is
    /// supplied, the "enters tapped unless you control a Plains" replacement
    /// is registered (CR 614.1c).
    /// </summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // Enters tapped unless you control a Plains (CR 614.1c). Predicate
        // returns true => enters untapped, false => enters tapped. The card
        // itself is excluded via reference equality. Same single-subtype
        // shape as CheckLandCycleFactory; HasSubtype(Plains) so any Plains
        // (basic or typed dual) qualifies, matching the printed oracle.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    ControllerControlsPlains(controller, self)));
        }

        // ----------------------------------------------------------------
        // {2}{W}{W}, {T}: Look at the top five cards of your library. You may
        // reveal a historic card from among them and put it into your hand.
        // Put the rest on the bottom of your library in a random order.
        // CR 117 — the "may reveal" choice is made at resolution time; the
        // resolution body is supplied in code (the JSON ability schema does
        // not model this multi-step library manipulation).
        // ----------------------------------------------------------------
        var lookEffect = new Effect(
            "Monumental Henge: look at top 5, may reveal a historic card to hand, " +
            "rest to the bottom of the library in a random order.",
            () => ResolveLook(owner, DefaultHistoricSelector));

        var ability = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}{W}{W}"),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { lookEffect });

        land.AddAbility(ability);

        return land;
    }

    /// <summary>Selector signature: from the peeked up-to-5 cards, choose
    /// (toHand, toBottomInOrder). <c>toHand</c> is 0 or 1 cards; <c>toBottom</c>
    /// is the remainder in the order they should be re-appended to the bottom
    /// of the library. Mirrors <see cref="AncientStirringsFactory"/>.</summary>
    public delegate (IReadOnlyList<ICard> toHand, IReadOnlyList<ICard> toBottom) HengeSelector(
        IReadOnlyList<ICard> peeked);

    /// <summary>
    /// Default selector: pick the first <em>historic</em> card. Per the
    /// reminder text, historic = Artifact (CR 205.2b) OR Legendary supertype
    /// (CR 205.4) OR a Saga subtype (CR 714). If none are historic, no card
    /// moves to hand. Remaining cards are shuffled (CR 701.20a) before being
    /// placed at the bottom.
    /// </summary>
    public static (IReadOnlyList<ICard> toHand, IReadOnlyList<ICard> toBottom)
        DefaultHistoricSelector(IReadOnlyList<ICard> peeked)
    {
        ArgumentNullException.ThrowIfNull(peeked);

        ICard? historic = null;
        foreach (var c in peeked)
        {
            if (IsHistoric(c))
            {
                historic = c;
                break;
            }
        }

        var toHand = historic == null
            ? Array.Empty<ICard>()
            : new[] { historic };

        var bottom = new List<ICard>(peeked.Count);
        foreach (var c in peeked)
        {
            if (!ReferenceEquals(c, historic)) bottom.Add(c);
        }
        Shuffle(bottom);
        return (toHand, bottom);
    }

    /// <summary>
    /// CR 205.2b / 205.4 / 714 — a card is historic if it is an artifact, is
    /// legendary, or is a Saga.
    /// </summary>
    public static bool IsHistoric(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card.HasType(CardType.Artifact)
            || card.HasSupertype(CardSupertype.Legendary)
            || card.HasSubtype(CardSubtype.Saga);
    }

    /// <summary>
    /// Run the look-at-top-five resolution body with the supplied selector.
    /// Peeks the top up-to-5 cards, moves the selector's pick (0 or 1) to
    /// hand, and re-bottoms the rest in the selector-returned order.
    /// </summary>
    public static void ResolveLook(Player caster, HengeSelector selector)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(selector);

        var lib = caster.Zones.Library;
        var peeked = lib.GetCards().Take(5).ToList();
        if (peeked.Count == 0) return;

        var (toHand, toBottom) = selector(peeked);

        // Move chosen (0 or 1) to hand.
        foreach (var c in toHand)
        {
            lib.RemoveCard(c);
            caster.Zones.Hand.AddCard(c);
            c.SetZone(ZoneType.Hand);
        }
        // Re-bottom the rest. Zone.AddCard appends — appending in the
        // (already-shuffled) toBottom order gives the random bottom
        // placement required by the printed text (CR 701.20a).
        foreach (var c in toBottom)
        {
            lib.RemoveCard(c);
        }
        foreach (var c in toBottom)
        {
            lib.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }

    private static bool ControllerControlsPlains(Player controller, ICard self) =>
        controller.Zones.Battlefield.GetCards()
            .Any(c => !ReferenceEquals(c, self) && c.HasSubtype(CardSubtype.Plains));

    private static void Shuffle<T>(IList<T> list)
    {
        // Fisher-Yates via Random.Shared. Tests that need determinism should
        // pass a custom selector instead of relying on the default.
        var rng = System.Random.Shared;
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
