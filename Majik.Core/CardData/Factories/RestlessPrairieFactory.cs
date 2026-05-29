using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Restless Prairie (Murders at Karlov Manor "Restless"
/// creature-land cycle, green/white member). Land.
///
/// Oracle text (verified against Scryfall 2026-05-29):
///   "This land enters tapped.
///    {T}: Add {G} or {W}.
///    {2}{G}{W}: This land becomes a 3/3 green and white Llama creature until
///    end of turn. It's still a land.
///    Whenever this land attacks, other creatures you control get +1/+1 until
///    end of turn."
///
/// Shares the manland shape used by <see cref="RestlessBivouacFactory"/> /
/// <see cref="RestlessRidgelineFactory"/>: unconditional ETB-tapped
/// (CR 614.1c), two mana abilities (one per colour, CR 605.1), and a
/// {2}{G}{W} animate-until-EOT <see cref="ActivatedAbility"/> whose resolution
/// registers a <see cref="ManlandCycleAnimateEffect"/> (Layer 4 — add
/// Creature + Llama subtype) and a <see cref="ManlandCycleBecomesPTEffect"/>
/// (Layer 7b — set base P/T 3/3), both flagged
/// <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> so
/// <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> (CR 514.2 cleanup
/// step) lifts the animation.
///
/// The base shape (plain nonbasic Land + the two colour mana abilities) is
/// materialised from the embedded JSON definition
/// (<c>restless-prairie.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the ETB-tapped rider, the
/// animate ability, and the attack trigger are layered on here because the
/// JSON <c>AbilityDefinition</c> schema expresses none of them yet (same
/// posture as <see cref="RestlessBivouacFactory"/>).
///
/// ## What is unique to Restless Prairie vs the rest of the cycle
/// The intrinsic attack trigger — "Whenever this land attacks, other creatures
/// you control get +1/+1 until end of turn." (CR 508.1f) — is printed on the
/// <b>land</b>, not granted by the animate ability, so it is attached
/// unconditionally at construction and registered with the supplied
/// <see cref="TriggerManager"/> up front. A land can only attack while it is
/// the animated 3/3 creature (CR 508.1a), so the trigger is unreachable until
/// the controller activates {2}{G}{W} the same turn.
///
/// Unlike <see cref="RestlessBivouacFactory"/> / <see cref="RestlessRidgelineFactory"/>
/// (which target a single creature), this trigger is <b>non-targeted</b> — a
/// one-shot anthem over "other creatures you control" (CR 611 — a one-shot
/// pump, NOT a continuous static). It carries no
/// <see cref="TargetRequest"/>. On resolution it snapshots the controller's
/// battlefield creatures at that moment (CR 608.2 — effects resolve against
/// current game state), excludes the source land itself ("other"), and
/// registers a +1/+1 <see cref="PumpUntilEndOfTurnEffect"/> (Layer 7c,
/// CR 613.7c, ExpiresAtEndOfTurn — CR 514.2) on each. Creatures that enter
/// after resolution do NOT get the buff (same one-shot-snapshot posture as
/// <see cref="ViolentOutburstFactory.ApplyPumpAndHaste"/>).
///
/// ## v1 posture (shared with the manland cycle)
/// - <b>Green/white colour identity of the animated form</b> — no Layer-5
///   colour-set primitive yet (same gap as Raging Ravine / Needle Spires).
///   Recorded only in the effect-name string; the Llama subtype + 3/3 body DO
///   apply via Compute.
/// - <b>Combat math through Compute on the land itself</b> — same gap as
///   every other manland: <see cref="ContinuousEffectsService.Compute(Permanent)"/>
///   seeds a plain <see cref="PermanentCharacteristics"/> row for a Land
///   runtime instance, so the 3/3 is recorded for inspection but doesn't
///   surface for combat resolution on the land. The +1/+1 pump on each OTHER
///   creature (already a Creature row) is fully observable via Compute.
/// - <b>Pump targets the supplied effects service</b> — the trigger registers
///   each pump into the same <see cref="ContinuousEffectsService"/> supplied
///   to <see cref="Create(Player, ContinuousEffectsService?, ReplacementBus?, TriggerManager?)"/>,
///   not the per-creature <see cref="Creature.ActiveEffects"/>, matching the
///   shared-service test posture of the rest of the Restless cycle.
/// </summary>
[CardName("Restless Prairie")]
public static class RestlessPrairieFactory
{
    public const string CardName = "Restless Prairie";
    public const string Slug = "restless-prairie";
    public const int AnimatedPower = 3;
    public const int AnimatedToughness = 3;
    public const int PumpPower = 1;
    public const int PumpToughness = 1;

    /// <summary>
    /// Construct Restless Prairie with no <see cref="ContinuousEffectsService"/>,
    /// <see cref="ReplacementBus"/>, or <see cref="TriggerManager"/> wired.
    /// The two mana abilities (from JSON) + the animate ability + the attack
    /// trigger shape are attached so the card surface is complete; the layer
    /// effects are not registered, the ETB-tapped replacement is omitted, and
    /// the attack trigger is not auto-registered (its effect still runs when
    /// driven manually, but no-ops without an effects service). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null, triggers: null);

    /// <summary>
    /// Construct Restless Prairie with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 + Layer
    /// 7b registration of the animate ability and the attack trigger's +1/+1
    /// pump on each other creature. May be null — the abilities still resolve
    /// but no continuous effects are recorded.</param>
    /// <param name="replacements">Replacement bus for the unconditional
    /// enters-tapped rider (CR 614.1c). May be null.</param>
    /// <param name="triggers">Trigger manager the intrinsic attack trigger is
    /// registered with. May be null — the trigger is still attached to the
    /// land's ability list but won't fire from the event bus.</param>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type,
        // {T}: Add {G} / {T}: Add {W} mana abilities). The ETB-tapped rider,
        // the animate ability, and the attack trigger are layered on below —
        // none is expressible in the current JSON AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB-tapped restriction (CR 614.1c) — "This land enters tapped."
        // Unconditional; no gate.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {2}{G}{W}: This land becomes a 3/3 green and white Llama creature
        // until end of turn. It's still a land.
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost =
        // {2}{G}{W}, no tap rider. Resolution registers Layer 4 + Layer 7b
        // continuous effects flagged ExpiresAtEndOfTurn (CR 514.2).
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes {AnimatedPower}/{AnimatedToughness} green and white Llama creature until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature type + Llama subtype. No printed
                // keywords on the animated body. Printed Land type stays
                // ("it's still a land", CR 613.1c).
                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: Array.Empty<string>(),
                    subtypes: new[] { CardSubtype.Llama },
                    extraTypes: null));

                // Layer 7b — set base P/T 3/3.
                effects.Register(new ManlandCycleBecomesPTEffect(
                    land, AnimatedPower, AnimatedToughness));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{2}{G}{W}") },
            effects: new IEffect[] { animateEffect }));

        // ----------------------------------------------------------------
        // Intrinsic attack trigger (printed on the land, CR 508.1f):
        //   "Whenever this land attacks, other creatures you control get
        //    +1/+1 until end of turn."
        //
        // Attached unconditionally (NOT granted by the animate effect — the
        // trigger is printed on the land itself). A land can only attack
        // while it's the animated 3/3 (CR 508.1a), so the trigger is
        // unreachable until the {2}{G}{W} ability resolves the same turn.
        //
        // NON-TARGETED (CR 611 — a one-shot pump, not a continuous static and
        // not a targeted ability). On resolution it snapshots the
        // controller's battlefield creatures at that moment (CR 608.2),
        // excludes the source land ("other"), and registers a +1/+1
        // PumpUntilEndOfTurnEffect (CR 613.7c, expires EOT per CR 514.2) on
        // each. The snapshot is taken to a list first so any same-step zone
        // moves don't disturb the enumeration (same posture as Violent
        // Outburst / Pyroclasm).
        // ----------------------------------------------------------------
        var anthemEffect = new Effect(
            $"{CardName}: other creatures you control get +{PumpPower}/+{PumpToughness} until end of turn",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                var controller = land.Controller ?? owner;
                var others = controller.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .Where(c => !ReferenceEquals(c, land))
                    .ToList();

                foreach (var creature in others)
                {
                    // CR 613.7c — +1/+1 with CR 514.2 end-of-turn expiry.
                    effects.Register(new PumpUntilEndOfTurnEffect(
                        creature, PumpPower, PumpToughness));
                }
            });

        var attackTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnAttackSelf(land),
            effects: new IEffect[] { anthemEffect },
            activeZones: new[] { ZoneType.Battlefield });

        land.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return land;
    }
}
