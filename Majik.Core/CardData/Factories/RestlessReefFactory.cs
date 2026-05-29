using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Restless Reef (Outlaws of Thunder Junction
/// "restless" land cycle). Land.
///
/// Oracle text (verified Scryfall 2026-05-29):
///   "This land enters tapped.
///    {T}: Add {U} or {B}.
///    {2}{U}{B}: Until end of turn, this land becomes a 4/4 blue and black
///    Shark creature with deathtouch. It's still a land.
///    Whenever this land attacks, target player mills four cards."
///
/// Same posture as the Worldwake / BFZ / OGW manland cycle
/// (<see cref="ManlandCycleAnimateEffect"/> /
/// <see cref="ManlandCycleBecomesPTEffect"/>) plus a per-card attack
/// trigger (mirrors <see cref="HiveOfTheEyeTyrantFactory"/>'s
/// <see cref="Triggers.OnAttackSelf"/> shape). The restless lands' attack
/// trigger is printed on the LAND ("Whenever this land attacks"), not gated
/// behind the animated body, so it is attached unconditionally — while not
/// animated the land can't attack, so the trigger is unreachable until the
/// {2}{U}{B} ability turns it into a creature (CR 508.1f).
///
/// ## Implemented (v1)
/// - Plain nonbasic Land identity (no printed subtypes / supertype) + the
///   <c>{T}: Add {U}</c> / <c>{T}: Add {B}</c> mana abilities — both from
///   the embedded JSON definition (<c>restless-reef.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> (CR 605.1, mana ability,
///   no stack).
/// - <b>ETB tapped (CR 614.1c)</b> — unconditional
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/> (same posture as
///   <see cref="HissingQuagmireFactory"/>; the restless lands have no
///   conditional clause).
/// - <b>{2}{U}{B}: animate until EOT</b> — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/> of
///   <c>{2}{U}{B}</c>. Resolution registers two end-of-turn-expirable
///   continuous effects against the supplied
///   <see cref="ContinuousEffectsService"/>:
///     - Layer 4 (<see cref="ManlandCycleAnimateEffect"/>) — adds
///       <see cref="CardType.Creature"/>, the <see cref="CardSubtype.Shark"/>
///       subtype, and a Deathtouch keyword marker (CR 702.2). Printed Land
///       type stays ("It's still a land", CR 613.1c).
///     - Layer 7b (<see cref="ManlandCycleBecomesPTEffect"/>) — set-base
///       P/T 4/4 (CR 613.7b).
///   Both carry <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> = true so
///   the cleanup-step expiry (CR 514.2) lifts the animation.
/// - <b>Whenever this land attacks, target player mills four cards</b> —
///   a <see cref="TriggeredAbility"/> on <see cref="Triggers.OnAttackSelf"/>
///   with a single 1..1 "target player" <see cref="TargetRequest"/>;
///   resolution reads <see cref="TriggeredAbility.ChosenTargets"/>[0][0],
///   and mills four cards from the chosen <see cref="Player"/> via
///   <see cref="MillAction.Apply"/> (CR 701.13). When the chosen target
///   token does not resolve to a <see cref="Player"/> the trigger no-ops
///   per CR 608.2b (illegal target at resolution).
///
/// ## Deferred (v1 gaps)
/// - <b>Blue/black colour identity of the animated form</b> — same gap as
///   the rest of the manland cycle: the engine's colour layer (Layer 5)
///   has no colour-setting effect primitive yet. The Shark body should be
///   blue and black while animated; v1 records the intent in the effect
///   name only.
/// - <b>Combat math through Compute</b>: same gap as the rest of the cycle
///   (Mutavault / Hive / Cave) — until
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> upgrades to
///   a <see cref="CreatureCharacteristics"/> row when Layer 4 grants
///   <see cref="CardType.Creature"/>, the 4/4 doesn't surface for combat
///   resolution.
/// - <b>Activation gate / sorcery-speed</b>: none — the animate ability is
///   instant-speed per oracle, no restriction needed.
/// </summary>
[CardName("Restless Reef")]
public static class RestlessReefFactory
{
    public const string CardName = "Restless Reef";
    public const string Slug = "restless-reef";
    public const int Power = 4;
    public const int Toughness = 4;
    public const int MillCount = 4;

    /// <summary>
    /// Construct Restless Reef with no
    /// <see cref="ContinuousEffectsService"/> or <see cref="ReplacementBus"/>
    /// wired. The two mana abilities (from JSON) + the animate ability + the
    /// attack-trigger shape are all attached so the card surface is complete;
    /// the layer effects are not registered and the ETB-tapped replacement
    /// is omitted (single-arg shape-only path). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null);

    /// <summary>
    /// Construct Restless Reef.
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
        // {T}: Add {U} / {T}: Add {B} mana abilities). The ETB-tapped rider,
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
        // {2}{U}{B}: Until end of turn, this land becomes a 4/4 blue and
        // black Shark creature with deathtouch. It's still a land.
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost =
        // {2}{U}{B}, no tap rider. Resolution registers Layer 4 + Layer 7b
        // continuous effects flagged ExpiresAtEndOfTurn.
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes 4/4 blue and black Shark creature with deathtouch until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature type, Shark subtype, Deathtouch.
                // Printed Land type stays ("it's still a land").
                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: new[] { "Deathtouch" },
                    subtypes: new[] { CardSubtype.Shark },
                    extraTypes: null));

                // Layer 7b — set base P/T 4/4.
                effects.Register(new ManlandCycleBecomesPTEffect(land, Power, Toughness));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{2}{U}{B}") },
            effects: new IEffect[] { animateEffect }));

        // ----------------------------------------------------------------
        // Whenever this land attacks, target player mills four cards.
        //
        // CR 508.1f — "Whenever ~ attacks" per-attacker trigger. The trigger
        // is printed on the land itself (not gated behind the animated body),
        // so it is attached unconditionally; while not animated the land
        // can't attack so the trigger is unreachable in practice. Target
        // prompt is a 1..1 "target player" TargetRequest (mirrors Mind
        // Sculpt's player-target mill); resolution reads ChosenTargets[0][0]
        // and mills MillCount cards via MillAction.Apply (CR 701.13). When
        // no Player target was chosen the trigger no-ops (CR 608.2b).
        // ----------------------------------------------------------------
        TriggeredAbility? attackTrigger = null;
        var millEffect = new Effect(
            $"{CardName}: target player mills {MillCount} cards",
            () =>
            {
                if (attackTrigger == null) return;
                if (attackTrigger.ChosenTargets.Count == 0) return;
                if (attackTrigger.ChosenTargets[0].Count == 0) return;
                if (attackTrigger.ChosenTargets[0][0] is not Player target) return;

                MillAction.Apply(target, MillCount);
            });

        attackTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnAttackSelf(land),
            effects: new IEffect[] { millEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        land.AddAbility(attackTrigger);

        return land;
    }
}
