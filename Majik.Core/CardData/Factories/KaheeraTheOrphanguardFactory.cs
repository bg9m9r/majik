using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kaheera, the Orphanguard (Ikoria,
/// {1}{G/W}{G/W}).
///
/// Legendary Creature — Cat Beast 3/2. Oracle text (verified against
/// Scryfall):
///   "Companion — Each creature card in your starting deck is a Cat,
///    Elemental, Nightmare, Dinosaur, or Beast card. (If this card is your
///    chosen companion, you may put it into your hand from outside the
///    game for {3} as a sorcery.)
///    Vigilance
///    Each other creature you control that's a Cat, Elemental, Nightmare,
///    Dinosaur, or Beast gets +1/+1 and has vigilance."
///
/// The base shape (name, Legendary supertype, Creature, Cat + Beast
/// subtypes, {1}{G/W}{G/W} hybrid cost, 3/2) is materialised from the
/// embedded JSON definition (<c>kaheera-the-orphanguard.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The Vigilance keyword and
/// the tribal anthem are layered on here.
///
/// ## Implemented (v1)
///
/// - <b>3/2 Legendary Creature — Cat Beast</b> at {1}{G/W}{G/W}.
/// - <b>Vigilance (CR 702.20)</b>: a <see cref="KeywordAbility"/> marker.
///   The combat-abilities subsystem reads this marker so Kaheera does not
///   tap when declared as an attacker.
/// - <b>Tribal anthem (CR 613.7c — Layer 7c for P/T; CR 613.1f — Layer 6
///   for the granted keyword, applied here in 7c via direct chars
///   mutation, same posture as <see cref="LordStaticEffect"/>)</b>: "Each
///   other creature you control that's a Cat, Elemental, Nightmare,
///   Dinosaur, or Beast gets +1/+1 and has vigilance." Wired via
///   <see cref="KaheeraAnthemEffect"/> (private below).
///   <list type="bullet">
///     <item>"you control" — CR 109.5: controller-scoped. Only creatures
///       controlled by Kaheera's controller are buffed.</item>
///     <item>"Each OTHER creature" — Kaheera itself is excluded (it would
///       otherwise match the Cat / Beast filter).</item>
///     <item>The generic <see cref="LordStaticEffect"/> filters on a
///       SINGLE subtype; Kaheera matches ANY of five subtypes, so a
///       tailored multi-subtype variant is shipped here (same posture as
///       <see cref="SliverLegionFactory"/>, whose pump the generic lord
///       can't express).</item>
///   </list>
///
/// ## Companion (deck-construction half)
///
/// The companion deck-construction rule (CR 702.139 — "Each creature card
/// in your starting deck is a Cat, Elemental, Nightmare, Dinosaur, or
/// Beast card") is exposed via <see cref="CompanionRestriction"/>, an
/// <see cref="ICompanionRestriction"/> that
/// <see cref="Majik.Core.Rules.CompanionValidator"/> consumes at
/// deck-registration time. The runtime "cast from outside the game"
/// pipeline is still deferred — the engine has no sideboard zone yet
/// (see <see cref="Majik.Core.Zones.ZoneType"/>), same posture as
/// <see cref="LurrusOfTheDreamDenFactory"/>.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. No anthem registered
///   (no continuous-effects service). Suitable for dispatcher / identity
///   tests. This is the overload <see cref="NamedCardFactory"/> dispatches
///   to.
/// - <see cref="Create(Player, ContinuousEffectsService)"/> — fully wired.
///   The tribal anthem registers against the layers service.
///
/// ## Deferred (v1 gaps)
///
/// - <b>LTB unregister</b>: the registered anthem stays on the
///   <see cref="ContinuousEffectsService"/> across zone changes;
///   <see cref="KaheeraAnthemEffect.IsActive"/> short-circuits when
///   Kaheera isn't on the battlefield so the grant lifts correctly, but a
///   future Prune pass could drop the entry. Same shape as
///   <see cref="SliverLegionFactory"/>.
/// </summary>
[CardName("Kaheera, the Orphanguard")]
public static class KaheeraTheOrphanguardFactory
{
    public const string CardName = "Kaheera, the Orphanguard";
    public const string Slug = "kaheera-the-orphanguard";

    /// <summary>
    /// The creature subtypes Kaheera cares about for both its anthem and
    /// its companion restriction: Cat, Elemental, Nightmare, Dinosaur,
    /// Beast. Surfaced as a constant so tests and callers don't repeat the
    /// list.
    /// </summary>
    public static readonly IReadOnlyList<CardSubtype> MatchingSubtypes = new[]
    {
        CardSubtype.Cat,
        CardSubtype.Elemental,
        CardSubtype.Nightmare,
        CardSubtype.Dinosaur,
        CardSubtype.Beast,
    };

    /// <summary>
    /// CR 702.139 — Kaheera's companion deck-construction predicate: "Each
    /// creature card in your starting deck is a Cat, Elemental, Nightmare,
    /// Dinosaur, or Beast card." Surfaced as a static singleton so
    /// deck-registration call sites can validate without instantiating
    /// Kaheera.
    /// </summary>
    public static ICompanionRestriction CompanionRestriction { get; } =
        new KaheeraCompanionRestriction();

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// True if the card has at least one of Kaheera's matching subtypes
    /// (Cat / Elemental / Nightmare / Dinosaur / Beast).
    /// </summary>
    internal static bool MatchesSubtype(ICard card)
    {
        if (card == null) return false;
        foreach (var st in MatchingSubtypes)
        {
            if (card.HasSubtype(st)) return true;
        }
        return false;
    }

    /// <summary>
    /// Construct Kaheera with the Vigilance marker but NO anthem registered
    /// (no continuous-effects service). Suitable for shape / dispatcher
    /// tests. This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Kaheera. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="KaheeraAnthemEffect"/> granting +1/+1 and Vigilance to
    /// every OTHER matching creature its controller controls is registered
    /// against the layers service.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// tribal anthem against. May be null — no live grant.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Creature, Cat + Beast subtypes, {1}{G/W}{G/W}, 3/2). The JSON
        // carries no abilities — Vigilance + the anthem are layered on below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.20 — Vigilance marker. The combat-abilities subsystem reads
        // this marker so the creature does not tap when attacking.
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        // CR 613.7c — "Each other creature you control that's a Cat,
        // Elemental, Nightmare, Dinosaur, or Beast gets +1/+1 and has
        // vigilance." Registered only when a continuous-effects service is
        // supplied (matches Sliver Legion's posture).
        if (continuousEffects != null)
        {
            continuousEffects.Register(new KaheeraAnthemEffect(card));
        }

        return card;
    }
}

/// <summary>
/// Kaheera's "Each other creature you control that's a Cat, Elemental,
/// Nightmare, Dinosaur, or Beast gets +1/+1 and has vigilance" static
/// (CR 613.7c — Layer 7c for P/T; the granted keyword is applied in the
/// same pass, same posture as <see cref="LordStaticEffect"/>).
///
/// The generic <see cref="LordStaticEffect"/> filters on a SINGLE subtype;
/// Kaheera matches ANY of five subtypes, so a tailored variant is shipped
/// here (same posture as <see cref="SliverLegionAnthemEffect"/>).
///
/// Filter (CR 613.7c — continuous effects apply only to permanents):
///   - Target is on the battlefield.
///   - Target is controlled by Kaheera's controller ("you control").
///   - Target is not Kaheera itself ("Each OTHER").
///   - Target has at least one of the five matching subtypes.
/// </summary>
public sealed class KaheeraAnthemEffect : ContinuousEffect
{
    private readonly Permanent _source;

    public KaheeraAnthemEffect(Permanent source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public override Layer Layer => Layer.PT_Modify;

    /// <summary>CR 613.1g — the lord permanent generating this effect.</summary>
    public override Permanent? Source => _source;

    public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature)
    {
        if (creature.Zone != ZoneType.Battlefield) return false;
        // "Each OTHER creature" — exclude Kaheera itself.
        if (ReferenceEquals(creature, _source)) return false;
        // CR 109.5 — "you control": controller-scoped.
        if (!ReferenceEquals(creature.Controller, _source.Controller)) return false;
        // One of Cat / Elemental / Nightmare / Dinosaur / Beast.
        return KaheeraTheOrphanguardFactory.MatchesSubtype(creature);
    }

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power += 1;
        chars.Toughness += 1;
        // CR 702.20 — granted Vigilance.
        chars.Keywords.Add("Vigilance");
    }
}

/// <summary>
/// CR 702.139 — Kaheera's deck-construction predicate: "Each creature card
/// in your starting deck is a Cat, Elemental, Nightmare, Dinosaur, or Beast
/// card." Only CREATURE cards are constrained (non-creature cards — lands,
/// instants, sorceries, etc. — are unconstrained per the printed wording
/// "each creature card"). Among creature cards, every one must have at
/// least one of the five matching subtypes.
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
            // Only creature cards are constrained.
            if (!card.HasType(CardType.Creature)) continue;
            if (!KaheeraTheOrphanguardFactory.MatchesSubtype(card)) return false;
        }
        return true;
    }
}
