using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vivien, Champion of the Wilds (War of the Spark,
/// {2}{G}).
///
/// Legendary Planeswalker — Vivien. Starting loyalty 4. Oracle text (verified
/// against Scryfall):
///   "You may cast creature spells as though they had flash.
///    +1: Until your next turn, up to one target creature gains vigilance and
///        reach.
///    −2: Look at the top three cards of your library. Exile one face down and
///        put the rest on the bottom of your library in any order. For as long
///        as it remains exiled, you may cast it if it's a creature spell."
///
/// The base shape (name, Legendary Planeswalker — Vivien, {2}{G}, loyalty 4)
/// is materialised from the embedded JSON definition
/// (<c>vivien-champion-of-the-wilds.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The static + two loyalty
/// abilities are layered on here (same posture as <see cref="VivienReidFactory"/>).
///
/// ## Implemented (v1)
/// - <b>"You may cast creature spells as though they had flash"</b>
///   (CR 117.1a / 702.8): a battlefield-gated <see cref="FlashGrantStaticEffect"/>
///   registers a <see cref="FlashGrantRegistry"/> predicate matching every
///   CREATURE card owned (= controlled at cast time per CR 108.4) by Vivien's
///   controller, so they may cast their creatures at instant speed while Vivien
///   is on the battlefield. Lifts automatically on LTB. Same surface as
///   <see cref="ValleyFloodcallerFactory"/>'s noncreature-flash static (inverted
///   type predicate).
/// - <b>+1: up to one target creature gains vigilance and reach until your next
///   turn (CR 606 + CR 611 + CR 514.2)</b>: routed via the
///   <paramref name="targetResolver"/> — the first offered creature gains
///   Vigilance + Reach. When the continuous-effects service is wired the grant
///   is a controller-keyed <see cref="GrantKeywordsUntilControllersNextTurnEffect"/>
///   that ends precisely at Vivien's controller's next untap step; the
///   no-service shape build falls back to structural
///   <see cref="KeywordAbility"/> markers. No resolver / no target ⇒ no-op
///   (loyalty change still applies, CR 606.3).
/// - <b>−2: Look at the top three, exile one face down, you may cast it if it's
///   a creature spell (CR 606 + CR 701.15 + CR 601.3e)</b>: peeks the top three
///   (clamped to library size) via <see cref="RevealAndChoose.RevealTopAndChoose"/>,
///   exiles the chosen card face down, and stamps a runtime exile-cast grant
///   (<see cref="Card.GrantRuntimeExileCast"/>) so the controller may later cast
///   it from exile for its printed cost IF it is a creature card. A noncreature
///   pick is exiled with no cast grant (it stays exiled — CR 701.15). Rest go to
///   the bottom of the library.
///
/// ## Deferred (v1 gaps, isolated)
/// - <b>+1 target prompt</b>: <see cref="LoyaltyAbility"/> doesn't declare a
///   <see cref="Majik.Core.Targeting.TargetRequest"/>; the buffed creature is
///   picked from <paramref name="targetResolver"/>. Same gap Vivien Reid shares.
/// - <b>−2 "in any order" re-bottom</b>: order-preserving (library order is
///   hidden — cosmetic), same posture as the shared reveal-and-choose primitive.
/// </summary>
[CardName("Vivien, Champion of the Wilds")]
public static class VivienChampionOfTheWildsFactory
{
    public const string CardName = "Vivien, Champion of the Wilds";
    public const string Slug = "vivien-champion-of-the-wilds";
    public const int StartingLoyalty = 4;
    public const int Plus1Loyalty = +1;
    public const int Minus2Loyalty = -2;

    /// <summary>CR 701.15 — the −2 looks at the top three cards.</summary>
    public const int DigCount = 3;

    private const string Vigilance = "Vigilance";
    private const string Reach = "Reach";

    /// <summary>
    /// Construct Vivien with no live wiring — the flash-grant static is created
    /// but not attached (registry untouched), the +1 no-ops (no target
    /// resolver), and the −2 digs three (deterministic first-eligible pick,
    /// exiled face down with a cast-if-creature grant). Loyalty changes still
    /// apply. The overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, continuousEffects: null, targetResolver: null);

    /// <summary>
    /// Effects-aware build — the overload the production
    /// <c>NamedCardFactory.CreateGeneratedWithEffects</c> dispatch invokes. When
    /// <paramref name="continuousEffects"/> carries an event bus the
    /// cast-creature-spells-as-though-flash static attaches (live while Vivien is
    /// on the battlefield).
    /// </summary>
    public static Planeswalker Create(Player owner, ContinuousEffectsService? continuousEffects)
        => Create(owner, continuousEffects, targetResolver: null);

    /// <summary>
    /// Construct Vivien, Champion of the Wilds.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">When non-null, its event bus drives the
    /// flash-grant static lifecycle.</param>
    /// <param name="targetResolver">Returns candidate creatures for the +1
    /// "up to one target creature gains vigilance and reach" clause. v1 buffs the
    /// first legal candidate. May be null — the clause no-ops.</param>
    public static Planeswalker Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        Func<IReadOnlyList<Creature>>? targetResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var vivien = (Planeswalker)CardDefinitionFactory.Build(definition, owner);

        // CR 117.1a / 702.8 — "You may cast creature spells as though they had
        // flash." Battlefield-gated FlashGrantRegistry predicate matching the
        // controller's CREATURE cards (inverse of Valley Floodcaller's
        // noncreature predicate).
        var bus = continuousEffects?.EventBus;
        if (bus != null)
        {
            new FlashGrantStaticEffect(
                source: vivien,
                eventBus: bus,
                predicate: c => c.HasType(CardType.Creature)
                    && ReferenceEquals(c.Owner, owner)).Attach();
        }

        // -- +1: Until your next turn, up to one target creature gains vigilance
        //    and reach. (CR 606 + CR 611) -------------------------------------
        vivien.AddAbility(new LoyaltyAbility(vivien, Plus1Loyalty, () =>
        {
            var candidates = targetResolver?.Invoke();
            if (candidates == null) return;
            foreach (var creature in candidates)
            {
                if (creature == null || creature.Zone != ZoneType.Battlefield) continue;

                // CR 514.2 — "until your next turn": when the continuous-effects
                // service is wired, model the duration precisely via the
                // controller-keyed expiry primitive (Layer 6 keyword grant that
                // drops at Vivien's controller's next untap step). Falls back to
                // a structural keyword-marker grant when no service is wired
                // (pure card-shape tests) — the markers persist, matching the
                // pre-duration posture.
                var controller = vivien.Controller ?? owner;
                if (continuousEffects != null)
                {
                    if (creature.ActiveEffects == null) creature.ActiveEffects = continuousEffects;
                    continuousEffects.Register(
                        new GrantKeywordsUntilControllersNextTurnEffect(
                            creature, controller, Vigilance, Reach));
                }
                else
                {
                    GrantKeywordIfMissing(creature, Vigilance);
                    GrantKeywordIfMissing(creature, Reach);
                }
                return; // "up to one target".
            }
        }));

        // -- −2: Look at the top three cards of your library. Exile one face
        //    down and put the rest on the bottom. For as long as it remains
        //    exiled, you may cast it if it's a creature spell.
        //    (CR 606 + CR 701.15 + CR 601.3e) ----------------------------------
        vivien.AddAbility(new LoyaltyAbility(vivien, Minus2Loyalty, () =>
        {
            var controller = vivien.Controller ?? owner;
            var picked = RevealAndChoose.RevealTopAndChoose(
                caster: controller,
                count: DigCount,
                eligiblePredicate: _ => true, // exile ANY one of the top three
                optional: false,
                label: "Card to exile face down",
                pickedDestination: ZoneType.Exile,
                restDestination: ZoneType.Library,
                sourceTag: Slug);

            // CR 601.3e — "you may cast it if it's a creature spell." Stamp a
            // runtime exile-cast grant for the printed cost on a creature pick;
            // a noncreature pick stays exiled with no cast permission.
            if (picked is Card { } pickedCard && pickedCard.HasType(CardType.Creature))
            {
                pickedCard.GrantRuntimeExileCast(
                    controller, pickedCard.ManaCostValue);
            }
        }));

        return vivien;
    }

    private static void GrantKeywordIfMissing(Creature creature, string keyword)
    {
        var already = creature.Abilities
            .OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, keyword, StringComparison.OrdinalIgnoreCase));
        if (!already)
        {
            creature.AddAbility(new KeywordAbility(keyword, creature, creature.Controller ?? creature.Owner));
        }
    }
}
