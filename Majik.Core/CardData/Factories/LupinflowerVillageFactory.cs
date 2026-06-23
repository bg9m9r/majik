using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lupinflower Village (Bloomburrow).
///
/// Land. Oracle text (verified against the embedded Modern seed / Scryfall):
///   "{T}: Add {C}."
///   "{T}: Add {W}. Spend this mana only to cast a creature spell."
///   "{1}{W}, {T}, Sacrifice this land: Look at the top six cards of your
///    library. You may reveal a Bat, Bird, Mouse, or Rabbit card from among
///    them and put it into your hand. Put the rest on the bottom of your
///    library in a random order."
///
/// ## Implemented (v1)
///
/// - <b>Land identity</b> — plain non-basic <see cref="Land"/>, no printed
///   subtype, no mana cost (CR 305.1). Loaded from
///   <c>Majik.Core/CardData/Cards/lupinflower-village.json</c> via
///   <see cref="CardDefinitionFactory"/>.
/// - <b>{T}: Add {C}</b> — unrestricted single colourless
///   <see cref="ManaAbility"/>, declared in JSON (CR 605.1 — mana ability,
///   never uses the stack).
/// - <b>{T}: Add {W}. Spend this mana only to cast a creature spell.</b> —
///   a second <see cref="ManaAbility"/> producing {W} that stamps a
///   <see cref="SpendRestriction"/> with the predicate
///   <c>spell => spell.Card.HasType(CardType.Creature)</c> (CR 106.4 —
///   mana with a spend restriction can only pay for objects matching the
///   restriction). The payment-gate enforcement is LIVE via
///   <see cref="Majik.Core.Costs.ManaPaymentResolver"/> /
///   <see cref="Majik.Core.Mana.ManaProvenanceSlot"/> — same gate as
///   Ancient Ziggurat / Cavern of Souls. Wired in the factory (not JSON)
///   because the JSON <c>{ "kind": "mana" }</c> shape produces only
///   unrestricted abilities.
/// - <b>{1}{W}, {T}, Sacrifice this land: dig six</b> — an
///   <see cref="ActivatedAbility"/> whose costs are
///   <see cref="ManaCostCost"/>("{1}{W}") + <see cref="AdditionalCost.Tap"/>
///   + <see cref="AdditionalCost.Sacrifice"/>(self), and whose resolution
///   runs the <see cref="BuildDigEffect"/> look-at-top-six dig (CR 701.21).
///   The selector reveals the first Bat / Bird / Mouse / Rabbit card found
///   (CR 205.3m subtypes) and moves it to hand; the rest go to the bottom of
///   the library in a random order (CR 701.20a), randomised via the caster's
///   <see cref="GameRandom"/> (deterministic when tests seed it). The dig
///   body directly cribs <see cref="SilundiVisionFactory"/> (look-at-top-N /
///   may-reveal-by-filter / bottom-rest-randomly), swapping the instant/sorcery
///   filter for the four Bloomburrow creature subtypes.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Agent-driven "may reveal" prompt</b>: the default selector always
///   reveals if a qualifying card is present. The selector seam lets tests /
///   future agent wiring model the "you may" opt-out (CR 116.1b) — same
///   posture as <see cref="SilundiVisionFactory"/> /
///   <see cref="AncientStirringsFactory"/>.
/// - <b>Reveal event</b>: the peek does not publish a per-card reveal event
///   (same gap as Silundi Vision).
/// - <b>Single-arg dispatcher path</b>: <see cref="NamedCardFactory"/>
///   dispatches the parameterless <see cref="Create"/>; the activated ability
///   uses the bare <see cref="AdditionalCost.Sacrifice"/> overload (no event
///   bus), matching the shape-only posture of the rest of the land family.
///
/// ## References
///
/// - <see cref="AncientZigguratFactory"/> — the creature-spell-only
///   <see cref="SpendRestriction"/> mana modelling cribbed for the {W} pip.
/// - <see cref="SilundiVisionFactory"/> — the look-at-top-six / may-reveal /
///   bottom-rest-randomly dig body cribbed for the sac ability.
/// - <see cref="MistriseVillageFactory"/> — sibling utility-land with a
///   mana-cost + tap activated ability.
/// </summary>
[CardName("Lupinflower Village")]
public static class LupinflowerVillageFactory
{
    public const string CardName = "Lupinflower Village";
    public const string Slug = "lupinflower-village";
    public const int PeekCount = 6;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    // CR 106.4 — "Spend this mana only to cast a creature spell." Shared
    // static restriction (delegate-by-ref equality) — same posture as Ancient
    // Ziggurat.
    private static readonly SpendRestriction CreatureSpellOnly =
        new("creature spell",
            spell => spell.Card.HasType(CardType.Creature));

    /// <summary>
    /// Construct Lupinflower Village owned and controlled by
    /// <paramref name="owner"/>. The {T}: Add {C} mana ability comes from
    /// JSON; the restricted {W} mana ability and the {1}{W}, {T}, Sacrifice
    /// dig ability are wired here.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + the {T}: Add {C} unrestricted mana ability come from JSON.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // {T}: Add {W}. Spend this mana only to cast a creature spell.
        // CR 106.4 — restricted mana, stamped with CreatureSpellOnly.
        land.AddAbility(new ManaAbility(
            land, owner, ManaCost.Parse("W"),
            canActivateCheck: null,
            spendRestriction: CreatureSpellOnly));

        // {1}{W}, {T}, Sacrifice this land: Look at the top six cards of your
        // library. You may reveal a Bat, Bird, Mouse, or Rabbit card from among
        // them and put it into your hand. Put the rest on the bottom of your
        // library in a random order. CR 602 — activated ability.
        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}{W}"),
                AdditionalCost.Tap(land),
                AdditionalCost.Sacrifice(land),
            },
            effects: BuildDigEffect(owner)));

        return land;
    }

    /// <summary>Selector signature: from the peeked up-to-six cards, choose
    /// (toHand, toBottom). <c>toHand</c> is 0 or 1 cards; <c>toBottom</c> is
    /// the remainder in their pre-randomisation order. The two lists must
    /// partition the input — no duplicates, no extras.</summary>
    public delegate (IReadOnlyList<ICard> toHand, IReadOnlyList<ICard> toBottom) DigSelector(
        IReadOnlyList<ICard> peeked);

    /// <summary>
    /// Default selector: reveal the first Bat / Bird / Mouse / Rabbit card
    /// (CR 205.3m); if none qualify, nothing moves to hand. The resolve effect
    /// applies the random bottom ordering (CR 701.20a), so the remainder is
    /// returned in its peeked order here.
    /// </summary>
    public static (IReadOnlyList<ICard> toHand, IReadOnlyList<ICard> toBottom)
        DefaultDigSelector(IReadOnlyList<ICard> peeked)
    {
        ArgumentNullException.ThrowIfNull(peeked);

        ICard? revealed = null;
        foreach (var c in peeked)
        {
            if (IsQualifyingType(c))
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

    /// <summary>A card qualifies if it has the Bat, Bird, Mouse, or Rabbit
    /// creature subtype (CR 205.3m). A card with any of those subtypes is a
    /// "Bat, Bird, Mouse, or Rabbit card" regardless of its other types.</summary>
    public static bool IsQualifyingType(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card.HasSubtype(CardSubtype.Bat)
            || card.HasSubtype(CardSubtype.Bird)
            || card.HasSubtype(CardSubtype.Mouse)
            || card.HasSubtype(CardSubtype.Rabbit);
    }

    /// <summary>
    /// Build the dig resolution effect. Pass <see cref="DefaultDigSelector"/>
    /// (the default) for the printed-card behaviour. The bottom-of-library
    /// reorder is randomised via the caster's <see cref="GameRandom"/>
    /// (CR 701.20a); tests seed it via <see cref="GameRandomRegistry.SetDefault"/>
    /// for determinism.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildDigEffect(
        Player caster, DigSelector? selector = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        var pick = selector ?? DefaultDigSelector;
        return new IEffect[]
        {
            new Effect(
                "Lupinflower Village: look at top six, may reveal a Bat, Bird, " +
                "Mouse, or Rabbit card to hand, rest to the bottom of the " +
                "library in a random order.",
                () => Resolve(caster, pick)),
        };
    }

    private static void Resolve(Player caster, DigSelector pick)
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

        // Bottom the rest in a random order (CR 701.20a). Per-game RNG; tests
        // seed it for determinism.
        var remainder = toBottom.ToList();
        if (remainder.Count == 0) return;

        var rng = GameRandomRegistry.Get(caster);
        rng.Shuffle(remainder);

        // Library.AddCard appends to the bottom; remove-then-add each remainder
        // card so the new bottom order is the shuffled order.
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
