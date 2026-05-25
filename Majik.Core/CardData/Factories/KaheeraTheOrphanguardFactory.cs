using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Rules;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kaheera, the Orphanguard (Ikoria, {1}{G}{W}).
///
/// Legendary Creature — Cat Beast 3/2. Oracle text:
///   "Companion — Each creature card in your starting deck is a Cat,
///    Elemental, Nightmare, Dinosaur, or Beast card."
///   "Vigilance"
///   "Other Cat, Elemental, Nightmare, Dinosaur, and Beast creatures you
///    control get +1/+1."
///
/// ## Implemented (v1)
/// - 3/2 Legendary Creature — Cat Beast at {1}{G}{W}.
/// - Vigilance (CR 702.20) as a <see cref="KeywordAbility"/> marker.
/// - <see cref="LordStaticEffect"/> multi-subtype overload: +1/+1 to OTHER
///   creatures the controller controls that share at least one of the five
///   listed subtypes (Cat, Elemental, Nightmare, Dinosaur, Beast). CR
///   613.7c. <c>includeSelf: false</c> — Kaheera doesn't self-pump even
///   though it is a Cat Beast.
/// - Companion deck-construction predicate
///   (<see cref="CompanionRestriction"/>): every CREATURE card in the
///   starting deck must have at least one of the five listed subtypes.
///   Non-creature cards are unconstrained per the printed clause ("Each
///   creature card …"). CR 702.139.
///
/// ## Deferred (v1 gaps)
/// - <b>"Cast from outside the game"</b> runtime: shared deferred surface
///   with Lurrus / Yorion — the engine has no sideboard zone yet
///   (see <see cref="Majik.Core.Zones.ZoneType"/>).
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/>
///   stays on the <see cref="ContinuousEffectsService"/>; its
///   <see cref="ContinuousEffect.IsActive"/> short-circuits when Kaheera
///   isn't on the battlefield (same pattern as Goblin Chieftain / Plague
///   Engineer).
/// </summary>
[CardName("Kaheera, the Orphanguard")]
public static class KaheeraTheOrphanguardFactory
{
    public const string CardName = "Kaheera, the Orphanguard";
    public const string PrintedManaCost = "{1}{G}{W}";
    public const int Power = 3;
    public const int Toughness = 2;

    /// <summary>
    /// The five creature subtypes Kaheera's static and companion clauses
    /// both reference. Surfaced as a static so tests and the companion
    /// predicate share a single source of truth.
    /// </summary>
    public static IReadOnlyList<CardSubtype> EligibleSubtypes { get; } = new[]
    {
        CardSubtype.Cat,
        CardSubtype.Elemental,
        CardSubtype.Nightmare,
        CardSubtype.Dinosaur,
        CardSubtype.Beast,
    };

    /// <summary>
    /// CR 702.139 — Kaheera's companion deck-construction predicate:
    /// "Each creature card in your starting deck is a Cat, Elemental,
    /// Nightmare, Dinosaur, or Beast card." Surfaced as a static
    /// singleton so deck-registration call sites can validate without
    /// instantiating Kaheera.
    /// </summary>
    public static ICompanionRestriction CompanionRestriction { get; } =
        new KaheeraCompanionRestriction();

    /// <summary>
    /// Construct Kaheera with no live continuous-effects service. The
    /// printed Vigilance keyword is wired but the +1/+1 lord static is
    /// not registered. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Kaheera. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="LordStaticEffect"/> granting +1/+1 to other
    /// Cat/Elemental/Nightmare/Dinosaur/Beast creatures the controller
    /// controls is registered against the layers service.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// +1/+1 static effect against. May be null — no live bonus.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Cat, CardSubtype.Beast });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.20 — Vigilance.
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        if (continuousEffects != null)
        {
            // CR 613.7c — Layer 7c +1/+1 lord buff. Multi-subtype OR-match;
            // includeSelf: false so Kaheera (a Cat Beast) doesn't self-pump.
            // Controller-scoped (default — not opponentsOnly / allPlayers).
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtypes: EligibleSubtypes,
                power: 1,
                toughness: 1,
                grantedKeywords: null,
                includeSelf: false,
                opponentsOnly: false));
        }

        return card;
    }
}

/// <summary>
/// CR 702.139 — Kaheera's deck-construction predicate: "Each creature
/// card in your starting deck is a Cat, Elemental, Nightmare, Dinosaur,
/// or Beast card." Non-creature cards (artifacts, instants, sorceries,
/// enchantments, lands, planeswalkers) are unconstrained per the printed
/// wording.
/// </summary>
internal sealed class KaheeraCompanionRestriction : ICompanionRestriction
{
    public string Description =>
        "Each creature card in your starting deck is a Cat, Elemental, "
        + "Nightmare, Dinosaur, or Beast card.";

    public bool IsSatisfiedBy(IEnumerable<ICard> startingDeck)
    {
        ArgumentNullException.ThrowIfNull(startingDeck);
        foreach (var card in startingDeck)
        {
            if (card == null) continue;
            if (!card.HasType(CardType.Creature)) continue;
            var matches = false;
            foreach (var sub in KaheeraTheOrphanguardFactory.EligibleSubtypes)
            {
                if (card.HasSubtype(sub)) { matches = true; break; }
            }
            if (!matches) return false;
        }
        return true;
    }
}
