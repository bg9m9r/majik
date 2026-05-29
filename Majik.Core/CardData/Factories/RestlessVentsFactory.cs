using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Restless Vents (March of the Machine manland
/// cycle, B/R member — sibling of <see cref="DenOfTheBugbearFactory"/> and
/// <see cref="HissingQuagmireFactory"/>). Land.
///
/// Oracle text (verified against Scryfall, MOM printing):
///   "This land enters tapped.
///    {T}: Add {B} or {R}.
///    {1}{B}{R}: Until end of turn, this land becomes a 2/3 black and red
///    Insect creature with menace. It's still a land.
///    Whenever this land attacks, you may discard a card. If you do, draw a
///    card."
///
/// ## Implemented (v1)
/// - Plain Land identity (no printed subtypes, no supertype).
/// - <b>Unconditional ETB-tapped (CR 614.1c)</b> — registered as an
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/> (same shape as
///   <see cref="HissingQuagmireFactory"/>).
/// - <b>{T}: Add {B} or {R}</b> — two vanilla <see cref="ManaAbility"/>
///   instances (CR 605.1, no stack), one per producible colour, matching
///   the dual-colour manland convention.
/// - <b>{1}{B}{R}: animate until EOT</b> — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/> of
///   <c>{1}{B}{R}</c>. Resolution registers two end-of-turn-expirable
///   continuous effects against the supplied
///   <see cref="ContinuousEffectsService"/> (shared manland-cycle effects):
///     - Layer 4 (<see cref="ManlandCycleAnimateEffect"/>) — adds
///       <see cref="CardType.Creature"/>, the <see cref="CardSubtype.Insect"/>
///       subtype, and the "Menace" keyword (CR 702.111). The printed Land
///       type is left intact ("It's still a land", CR 613.1c).
///     - Layer 7b (<see cref="ManlandCycleBecomesPTEffect"/>) — set-base
///       P/T 2/3 (CR 613.7b).
///   Both effects carry <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>
///   = true so <see cref="ContinuousEffectsService.ExpireEndOfTurn"/>
///   (CR 514.2 cleanup step) lifts the animation.
/// - <b>Per-instance "Whenever this land attacks, you may discard a card.
///   If you do, draw a card" trigger</b> — wired via
///   <see cref="Triggers.OnAttackSelf"/> against
///   <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>
///   (CR 508.1f). This is a "rummage" (discard FIRST, then draw): the
///   effect discards a card via <see cref="Majik.Core.Primitives.Fx.Discard"/>;
///   only if a card was actually discarded does the intervening "if you do"
///   clause (CR 603.4) allow the draw via
///   <see cref="Majik.Core.Primitives.Fx.DrawCards"/>. The trigger is
///   attached unconditionally so the shape is inspectable; while not
///   animated the land can't attack, so it is unreachable in practice
///   (the body inherits its ability set from the animate layer effect).
///
/// ## Deferred (v1 gaps)
/// - <b>Black/red colour identity of the animated form</b> — same gap as
///   the rest of the manland cycle: the engine's colour layer (Layer 5)
///   has no colour-setting effect primitive yet. The Insect body should be
///   black and red while animated; v1 records the intent in the effect-name
///   string but doesn't apply it to the animated land.
/// - <b>"You may" prompt + discard choice</b> — same gap as every other
///   looter (Smuggler's Copter, Psychic Frog, Faithless Looting): v1 takes
///   the rummage unconditionally and <see cref="Majik.Core.Primitives.Fx.Discard"/>
///   picks the first card in hand deterministically. The "if you do, draw"
///   intervening clause IS honoured (empty hand → no draw).
/// - <b>Combat math through Compute</b>: same gap as the rest of the
///   manland cycle — until
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> upgrades to
///   a <see cref="CreatureCharacteristics"/> row when Layer 4 grants
///   <see cref="CardType.Creature"/>, the 2/3 doesn't surface for combat
///   resolution.
/// </summary>
[CardName("Restless Vents")]
public static class RestlessVentsFactory
{
    public const string CardName = "Restless Vents";
    public const int AnimatedPower = 2;
    public const int AnimatedToughness = 3;

    /// <summary>
    /// Construct Restless Vents with no <see cref="ContinuousEffectsService"/>,
    /// <see cref="ReplacementBus"/>, or <see cref="TriggerManager"/> wired.
    /// The dual mana abilities + the animate ability + the structural attack
    /// trigger are all attached so the card surface is complete; the layer
    /// effects are not registered, the ETB-tapped replacement is omitted, and
    /// the attack trigger is not auto-registered (its effect still runs when
    /// driven manually). Suitable for dispatcher / shape tests.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null, triggers: null);

    /// <summary>
    /// Construct Restless Vents with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 and
    /// Layer 7b registration of the animate ability. May be null — the
    /// ability still resolves but no continuous effects are recorded.</param>
    /// <param name="replacements">Replacement bus for the unconditional
    /// "enters tapped" rider (CR 614.1c). May be null — land enters untapped
    /// in that posture (mirrors how every other tapped-land factory defers
    /// this to the production binder when no bus is supplied).</param>
    /// <param name="triggers">TriggerManager — when supplied the attack
    /// trigger is registered so a CreatureAttacksEvent matching this land
    /// lands it on the stack automatically. May be null — the trigger is
    /// still attached to the card shape and resolvable when driven
    /// manually.</param>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Non-basic land, no supertype, no printed subtype.
        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // ETB-tapped (CR 614.1c) — "This land enters tapped." Unconditional.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {T}: Add {B} or {R}
        // CR 605.1 — two mana abilities, no stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("B")));
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("R")));

        // ----------------------------------------------------------------
        // {1}{B}{R}: Until end of turn, this land becomes a 2/3 black and
        // red Insect creature with menace. It's still a land.
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost =
        // {1}{B}{R}, no tap rider. Resolution registers Layer 4 + Layer 7b
        // continuous effects flagged ExpiresAtEndOfTurn.
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes {AnimatedPower}/{AnimatedToughness} black and red Insect creature with menace until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature type + Insect subtype + Menace
                // (CR 702.111). Printed Land type stays ("it's still a
                // land", CR 613.1c).
                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: new[] { "Menace" },
                    subtypes: new[] { CardSubtype.Insect },
                    extraTypes: null));

                // Layer 7b — set base P/T 2/3.
                effects.Register(new ManlandCycleBecomesPTEffect(
                    land, AnimatedPower, AnimatedToughness));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{1}{B}{R}") },
            effects: new IEffect[] { animateEffect }));

        // ----------------------------------------------------------------
        // Per-instance attack trigger: "Whenever this land attacks, you may
        // discard a card. If you do, draw a card."
        //
        // CR 508.1f / CR 603.4 (intervening "if you do"). This is a
        // "rummage": discard FIRST, then draw only if a card was discarded.
        // v1 takes the rummage unconditionally (the "you may" opt-out and
        // the discard pick are deferred — same gap as the looter family),
        // but the intervening "if you do" clause is honoured: an empty hand
        // discards nothing and therefore draws nothing.
        // ----------------------------------------------------------------
        var rummageEffect = new Effect(
            $"{CardName}: discard a card, if you do draw a card (attack trigger, CR 508.1f)",
            () =>
            {
                var controller = land.Controller ?? owner;
                var discarded = Majik.Core.Primitives.Fx.Discard(controller, 1);
                if (discarded.Count == 0) return; // "if you do" — no discard, no draw
                Majik.Core.Primitives.Fx.DrawCards(controller, 1);
            });

        var attackTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnAttackSelf(land),
            effects: new IEffect[] { rummageEffect },
            activeZones: new[] { ZoneType.Battlefield });

        land.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return land;
    }
}
