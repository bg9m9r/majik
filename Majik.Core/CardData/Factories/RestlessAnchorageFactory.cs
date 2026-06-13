using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Restless Anchorage (Murders at Karlov Manor
/// "restless" land cycle, W/U member). Land.
///
/// Oracle text (verified Scryfall 2026-06-13):
///   "This land enters tapped.
///    {T}: Add {W} or {U}.
///    {1}{W}{U}: Until end of turn, this land becomes a 2/3 white and blue
///    Bird creature with flying. It's still a land.
///    Whenever this land attacks, create a Map token."
///
/// Same posture as the Worldwake / BFZ / OGW manland cycle
/// (<see cref="ManlandCycleAnimateEffect"/> /
/// <see cref="ManlandCycleBecomesPTEffect"/>) plus a per-card attack
/// trigger (mirrors <see cref="RestlessReefFactory"/>'s
/// <see cref="Triggers.OnAttackSelf"/> shape). The trigger is printed on the
/// LAND ("Whenever this land attacks"), not gated behind the animated body,
/// so it is attached unconditionally — while not animated the land can't
/// attack, so the trigger is unreachable until the {1}{W}{U} ability turns
/// it into a creature (CR 508.1f).
///
/// ## Implemented (v1)
/// - Plain nonbasic Land identity (no printed subtypes / supertype) + the
///   <c>{T}: Add {W}</c> / <c>{T}: Add {U}</c> mana abilities — both from the
///   embedded JSON definition (<c>restless-anchorage.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> (CR 605.1, mana ability, no
///   stack).
/// - <b>ETB tapped (CR 614.1c)</b> — unconditional
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/> (same posture as
///   <see cref="RestlessReefFactory"/>; the restless lands have no
///   conditional clause).
/// - <b>{1}{W}{U}: animate until EOT</b> — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/> of
///   <c>{1}{W}{U}</c>. Resolution registers two end-of-turn-expirable
///   continuous effects against the supplied
///   <see cref="ContinuousEffectsService"/>:
///     - Layer 4 (<see cref="ManlandCycleAnimateEffect"/>) — adds
///       <see cref="CardType.Creature"/>, the <see cref="CardSubtype.Bird"/>
///       subtype, and a Flying keyword marker (CR 702.9). Printed Land type
///       stays ("It's still a land", CR 613.1c).
///     - Layer 7b (<see cref="ManlandCycleBecomesPTEffect"/>) — set-base
///       P/T 2/3 (CR 613.7b).
///   Both carry <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> = true so
///   the cleanup-step expiry (CR 514.2) lifts the animation.
/// - <b>Whenever this land attacks, create a Map token</b> — a non-targeted
///   <see cref="TriggeredAbility"/> on <see cref="Triggers.OnAttackSelf"/>;
///   resolution mints one Map token (CR 111.10) for the land's controller via
///   <see cref="TokenFactory.CreateMap"/>, which ships the Map's
///   "{1},{T},Sacrifice this token: Target creature you control explores"
///   ability.
///
/// ## Deferred (v1 gaps)
/// - <b>White/blue colour identity of the animated form</b> — same gap as the
///   rest of the manland cycle: the engine's colour layer (Layer 5) has no
///   colour-setting effect primitive yet. The Bird body should be white and
///   blue while animated; v1 records the intent in the effect name only.
/// - <b>Combat math through Compute</b>: same gap as the rest of the cycle —
///   until <see cref="ContinuousEffectsService.Compute(Permanent)"/> upgrades
///   to a <see cref="CreatureCharacteristics"/> row when Layer 4 grants
///   <see cref="CardType.Creature"/>, the 2/3 doesn't surface for combat
///   resolution on the land itself.
/// - <b>Activation gate / sorcery-speed</b>: none — the animate ability is
///   instant-speed per oracle, no restriction needed.
/// </summary>
[CardName("Restless Anchorage")]
public static class RestlessAnchorageFactory
{
    public const string CardName = "Restless Anchorage";
    public const string Slug = "restless-anchorage";
    public const int Power = 2;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Restless Anchorage with no
    /// <see cref="ContinuousEffectsService"/> or <see cref="ReplacementBus"/>
    /// wired. The two mana abilities (from JSON) + the animate ability + the
    /// attack-trigger shape are all attached so the card surface is complete;
    /// the layer effects are not registered and the ETB-tapped replacement is
    /// omitted (single-arg shape-only path). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null);

    /// <summary>
    /// Construct Restless Anchorage.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 and
    /// Layer 7b registration of the animate ability. May be null — the
    /// ability still resolves but no continuous effects are recorded.</param>
    /// <param name="replacements">Replacement bus for the unconditional
    /// "this land enters tapped" rider (CR 614.1c). May be null — the land
    /// enters untapped in that posture (mirrors how the sibling manland
    /// factories defer this to the production binder).</param>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type,
        // {T}: Add {W} / {T}: Add {U} mana abilities). The ETB-tapped rider,
        // the animate ability, and the attack trigger are layered on below —
        // none is expressible in the current JSON AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // "This land enters tapped." (CR 614.1c) — unconditional.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {1}{W}{U}: Until end of turn, this land becomes a 2/3 white and
        // blue Bird creature with flying. It's still a land.
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost =
        // {1}{W}{U}, no tap rider. Resolution registers Layer 4 + Layer 7b
        // continuous effects flagged ExpiresAtEndOfTurn.
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes {Power}/{Toughness} white and blue Bird creature with flying until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature type, Bird subtype, Flying.
                // Printed Land type stays ("it's still a land").
                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: new[] { "Flying" },
                    subtypes: new[] { CardSubtype.Bird },
                    extraTypes: null));

                // Layer 7b — set base P/T 2/3.
                effects.Register(new ManlandCycleBecomesPTEffect(land, Power, Toughness));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{1}{W}{U}") },
            effects: new IEffect[] { animateEffect }));

        // ----------------------------------------------------------------
        // Whenever this land attacks, create a Map token.
        //
        // CR 508.1f — "Whenever ~ attacks" per-attacker trigger. Printed on
        // the land itself (not gated behind the animated body), so attached
        // unconditionally; while not animated the land can't attack so the
        // trigger is unreachable in practice. Non-targeted: resolution mints
        // one Map artifact token (CR 111.10) for the controller via
        // TokenFactory.CreateMap.
        // ----------------------------------------------------------------
        var mapEffect = new Effect(
            $"{CardName}: create a Map token (CR 111.10)",
            () =>
            {
                var controller = land.Controller ?? owner;
                TokenFactory.CreateMap(controller);
            });

        land.AddAbility(new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnAttackSelf(land),
            effects: new IEffect[] { mapEffect },
            activeZones: new[] { ZoneType.Battlefield }));

        return land;
    }
}
