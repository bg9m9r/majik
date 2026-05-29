using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hall of Storm Giants (Strixhaven: School of
/// Mages). Land.
///
/// Oracle text (verified Scryfall 2026-05-28):
///   "If you control two or more other lands, this land enters tapped.
///    {T}: Add {U}.
///    {5}{U}: Until end of turn, this land becomes a 7/7 blue Giant
///    creature with ward {3}. It's still a land. (Whenever it becomes the
///    target of a spell or ability an opponent controls, counter it unless
///    that player pays {3}.)"
///
/// Same conditional-ETB creature-land shape as
/// <see cref="HiveOfTheEyeTyrantFactory"/> (the AFR analogue). Built
/// imperatively rather than through the JSON
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>
/// path because the JSON <c>EffectDefinition</c> set has no "becomes a
/// creature until end of turn" (animate) effect — the animate ability is
/// modelled via the shared <see cref="ManlandCycleAnimateEffect"/> /
/// <see cref="ManlandCycleBecomesPTEffect"/> continuous-effect primitives,
/// matching the rest of the manland cycle.
///
/// ## Implemented (v1)
/// - Plain Land identity (no printed subtypes, no supertype).
/// - <b>Conditional ETB-tapped (CR 614.1c)</b> — registered as a
///   <see cref="ConditionalEntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Predicate: enters untapped iff the
///   controller controls one or fewer OTHER lands (i.e. enters tapped
///   when ≥ 2 other lands are present). Mirrors the
///   <see cref="ConditionalEntersTappedBinder"/> "N or more other lands"
///   predicate at <c>threshold = 2, direction = more</c>. In the
///   production card-load path the binder layer already supplies this; the
///   factory wires it directly when a bus is passed (test convenience),
///   matching <see cref="HiveOfTheEyeTyrantFactory"/>.
/// - <b>{T}: Add {U}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1,
///   no stack).
/// - <b>{5}{U}: animate until EOT</b> — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/> of
///   <c>{5}{U}</c>. Resolution registers two end-of-turn-expirable
///   continuous effects against the supplied
///   <see cref="ContinuousEffectsService"/>:
///     - Layer 4 (<see cref="ManlandCycleAnimateEffect"/>) — adds
///       <see cref="CardType.Creature"/> and the
///       <see cref="CardSubtype.Giant"/> subtype, and grants the Ward
///       keyword marker (CR 702.21). The printed Land type is left intact
///       ("It's still a land", CR 613.1c).
///     - Layer 7b (<see cref="ManlandCycleBecomesPTEffect"/>) — set-base
///       P/T 7/7 (CR 613.7b).
///   Both effects carry <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>
///   = true so <see cref="ContinuousEffectsService.ExpireEndOfTurn"/>
///   (CR 514.2 cleanup step) lifts the animation.
///
/// ## Deferred (v1 gaps — shared with the manland cycle)
/// - <b>Blue colour identity of the animated form</b> — the engine's
///   colour layer (Layer 5) has no colour-setting effect primitive yet
///   (same gap as Creeping Tar Pit / Hive of the Eye Tyrant). The Giant
///   body should be blue while animated; v1 records the intent in the
///   effect-name string but doesn't apply.
/// - <b>Ward {3} cost enforcement</b> — Ward is recorded as a keyword
///   marker on the effective characteristics (CR 702.21). The
///   spell-resolution Ward-cost consultation (counter unless the
///   targeting opponent pays {3}) is wired by the engine's Ward handling
///   where present; same marker-only posture as the Ward grants on
///   Kappa Cannoneer / Colossal Skyturtle / Aboleth Spawn.
/// - <b>Combat math through Compute</b>: same gap as Mutavault / Creeping
///   Tar Pit / Hive — until
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> upgrades to
///   a <see cref="CreatureCharacteristics"/> row when Layer 4 grants
///   <see cref="CardType.Creature"/>, the 7/7 doesn't surface for combat
///   resolution. The P/T is recorded for inspection via
///   <see cref="ManlandCycleBecomesPTEffect"/>.
/// </summary>
[CardName("Hall of Storm Giants")]
public static class HallOfStormGiantsFactory
{
    public const string CardName = "Hall of Storm Giants";

    /// <summary>CR 702.21 — printed Ward cost: {3}.</summary>
    public const string WardCost = "{3}";

    /// <summary>
    /// Construct Hall of Storm Giants with no
    /// <see cref="ContinuousEffectsService"/> or <see cref="ReplacementBus"/>
    /// wired. The mana ability + the animate ability are attached so the
    /// card surface is complete; the layer effects are not registered, and
    /// the conditional ETB-tapped replacement is omitted (single-arg
    /// shape-only path).
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null);

    /// <summary>
    /// Construct Hall of Storm Giants.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 and
    /// Layer 7b registration of the animate ability. May be null — the
    /// ability still resolves but no continuous effects are recorded.</param>
    /// <param name="replacements">Replacement bus for the conditional
    /// "enters tapped if you control two or more other lands" rider
    /// (CR 614.1c). May be null — land enters untapped unconditionally in
    /// that posture (mirrors how every other conditional-tapped factory
    /// defers this to the production binder).</param>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Non-basic land, no supertype, no printed subtype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // Conditional ETB-tapped (CR 614.1c) — "If you control two or
        // more other lands, this land enters tapped."
        // Predicate: enters untapped iff controller controls ≤ 1 OTHER
        // land. Same shape as the ConditionalEntersTappedBinder's
        // "N or more other lands" → tapped form at threshold = 2.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    CountOtherLands(controller, self) <= 1));
        }

        // ----------------------------------------------------------------
        // {T}: Add {U}
        // CR 605.1 — mana ability, no stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("U")));

        // ----------------------------------------------------------------
        // {5}{U}: Until end of turn, this land becomes a 7/7 blue Giant
        // creature with ward {3}. It's still a land.
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost =
        // {5}{U}, no tap rider. Resolution registers Layer 4 + Layer 7b
        // continuous effects flagged ExpiresAtEndOfTurn (CR 514.2).
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes 7/7 blue Giant creature with ward {WardCost} until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature type + Giant subtype + Ward
                // keyword marker. Printed Land type stays ("it's still a
                // land", CR 613.1c).
                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: new[] { "Ward" },
                    subtypes: new[] { CardSubtype.Giant },
                    extraTypes: null));

                // Layer 7b — set base P/T 7/7.
                effects.Register(new ManlandCycleBecomesPTEffect(land, 7, 7));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{5}{U}") },
            effects: new IEffect[] { animateEffect }));

        return land;
    }

    /// <summary>
    /// CR 614 helper — count lands the controller controls excluding the
    /// candidate <paramref name="self"/>. Used by the conditional ETB-
    /// tapped predicate ("two or more OTHER lands").
    /// </summary>
    private static int CountOtherLands(Player controller, ICard self) =>
        controller.Zones.Battlefield.GetCards()
            .Count(c => !ReferenceEquals(c, self) && c.HasType(CardType.Land));
}
