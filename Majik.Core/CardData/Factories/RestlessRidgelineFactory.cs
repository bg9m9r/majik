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
/// Named-card factory for Restless Ridgeline (The Lost Caverns of Ixalan
/// "Restless" creature-land cycle, red/green member). Land.
///
/// Oracle text (verified against Scryfall 2026-05-29):
///   "This land enters tapped.
///    {T}: Add {R} or {G}.
///    {2}{R}{G}: This land becomes a 3/4 red and green Dinosaur creature
///    until end of turn. It's still a land.
///    Whenever this land attacks, another target attacking creature gets
///    +2/+0 until end of turn. Untap that creature."
///
/// Shares the manland shape used by <see cref="RestlessBivouacFactory"/> /
/// <see cref="RestlessFortressFactory"/>: unconditional ETB-tapped
/// (CR 614.1c), two mana abilities (one per colour, CR 605.1), and a
/// {2}{R}{G} animate-until-EOT <see cref="ActivatedAbility"/> whose
/// resolution registers a <see cref="ManlandCycleAnimateEffect"/> (Layer 4 —
/// add Creature + Dinosaur subtype) and a
/// <see cref="ManlandCycleBecomesPTEffect"/> (Layer 7b — set base P/T 3/4),
/// both flagged <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> so
/// <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> (CR 514.2 cleanup
/// step) lifts the animation.
///
/// The base shape (plain nonbasic Land + the two colour mana abilities) is
/// materialised from the embedded JSON definition
/// (<c>restless-ridgeline.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the ETB-tapped rider, the
/// animate ability, and the attack trigger are layered on here because the
/// JSON <c>AbilityDefinition</c> schema expresses none of them yet (same
/// posture as <see cref="RestlessBivouacFactory"/>).
///
/// ## What is unique to Restless Ridgeline vs the rest of the cycle
/// The intrinsic attack trigger — "Whenever this land attacks, another target
/// attacking creature gets +2/+0 until end of turn. Untap that creature."
/// (CR 508.1f) — is printed on the <b>land</b>, not granted by the animate
/// ability, so it is attached unconditionally at construction and registered
/// with the supplied <see cref="TriggerManager"/> up front. A land can only
/// attack while it is the animated 3/4 creature (CR 508.1a), so the trigger
/// is unreachable until the controller activates {2}{R}{G} the same turn.
/// "Another target attacking creature" is a mandatory 1..1
/// <see cref="TargetRequest"/> (CR 601.2c) whose candidate gatherer excludes
/// the source land itself (the "other" rider). On resolution it
///   - registers a <see cref="PumpUntilEndOfTurnEffect"/> (+2/+0, Layer 7c,
///     ExpiresAtEndOfTurn — CR 613.7c / CR 514.2), and
///   - untaps the chosen creature (CR 701.21). <see cref="Permanent.Untap"/>
///     throws if the permanent is already untapped, so the untap is gated on
///     <see cref="Permanent.IsTapped"/> — a no-op when the attacker is
///     untapped (e.g. it has vigilance), which matches "Untap that creature"
///     doing nothing observable when there is nothing to untap.
///
/// ## v1 posture (shared with the manland cycle)
/// - <b>"attacking" candidate restriction</b> — the engine has no
///   per-<see cref="Creature"/> "is attacking" flag (attacking state lives on
///   the <see cref="Majik.Core.Combat.Combat"/> object, not reachable from
///   this factory closure). The candidate gatherer therefore narrows to OTHER
///   battlefield creatures; the "attacking" qualifier is recorded in the
///   request description (same v1 narrowing as the cycle's colour/combat
///   gaps). Resolution honours whatever target the controller/agent supplied.
/// - <b>Red/green colour identity of the animated form</b> — no Layer-5
///   colour-set primitive yet (same gap as Raging Ravine / Needle Spires).
///   Recorded only in the effect-name string; the Dinosaur subtype + 3/4
///   body DO apply via Compute.
/// - <b>Combat math through Compute on the land itself</b> — same gap as
///   every other manland: <see cref="ContinuousEffectsService.Compute(Permanent)"/>
///   seeds a plain <see cref="PermanentCharacteristics"/> row for a Land
///   runtime instance, so the 3/4 is recorded for inspection but doesn't
///   surface for combat resolution on the land. The +2/+0 pump on a TARGET
///   creature (already a Creature row) is fully observable via Compute.
/// - <b>Agent-driven target prompt</b> — the trigger honours pre-set
///   <see cref="TriggeredAbility.ChosenTargets"/>; the factory does not wire
///   an <see cref="IPlayerAgent"/> prompt (same posture as Restless Bivouac).
/// </summary>
[CardName("Restless Ridgeline")]
public static class RestlessRidgelineFactory
{
    public const string CardName = "Restless Ridgeline";
    public const string Slug = "restless-ridgeline";
    public const int AnimatedPower = 3;
    public const int AnimatedToughness = 4;
    public const int PumpPower = 2;
    public const int PumpToughness = 0;

    /// <summary>
    /// Construct Restless Ridgeline with no <see cref="ContinuousEffectsService"/>,
    /// <see cref="ReplacementBus"/>, or <see cref="TriggerManager"/> wired.
    /// The two mana abilities (from JSON) + the animate ability + the attack
    /// trigger shape are attached so the card surface is complete; the layer
    /// effects are not registered, the ETB-tapped replacement is omitted, and
    /// the attack trigger is not auto-registered (its effect still runs when
    /// driven manually). This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null, triggers: null);

    /// <summary>
    /// Construct Restless Ridgeline with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 + Layer
    /// 7b registration of the animate ability and the attack trigger's +2/+0
    /// pump. May be null — the abilities still resolve but no continuous
    /// effects are recorded.</param>
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
        // {T}: Add {R} / {T}: Add {G} mana abilities). The ETB-tapped rider,
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
        // {2}{R}{G}: This land becomes a 3/4 red and green Dinosaur creature
        // until end of turn. It's still a land.
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost =
        // {2}{R}{G}, no tap rider. Resolution registers Layer 4 + Layer 7b
        // continuous effects flagged ExpiresAtEndOfTurn (CR 514.2).
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes {AnimatedPower}/{AnimatedToughness} red and green Dinosaur creature until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature type + Dinosaur subtype. No printed
                // keywords on the animated body. Printed Land type stays
                // ("it's still a land", CR 613.1c).
                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: Array.Empty<string>(),
                    subtypes: new[] { CardSubtype.Dinosaur },
                    extraTypes: null));

                // Layer 7b — set base P/T 3/4.
                effects.Register(new ManlandCycleBecomesPTEffect(
                    land, AnimatedPower, AnimatedToughness));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{2}{R}{G}") },
            effects: new IEffect[] { animateEffect }));

        // ----------------------------------------------------------------
        // Intrinsic attack trigger (printed on the land, CR 508.1f):
        //   "Whenever this land attacks, another target attacking creature
        //    gets +2/+0 until end of turn. Untap that creature."
        //
        // Mandatory 1..1 "another target attacking creature" (CR 601.2c). The
        // candidate gatherer excludes the source land ("other"). On resolution
        // it registers a +2/+0 PumpUntilEndOfTurnEffect (CR 613.7c, expires
        // EOT) on the chosen creature and untaps it (CR 701.21) — the untap is
        // gated on IsTapped because Permanent.Untap throws when already
        // untapped.
        // ----------------------------------------------------------------
        TriggeredAbility? attackTrigger = null;

        var targetRequest = new TargetRequest(
            Description: "another target attacking creature",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.Buff,
            CandidateGatherer: _ => GatherOtherCreatures(land));

        var pumpEffect = new Effect(
            $"{CardName}: another target attacking creature gets +{PumpPower}/+{PumpToughness} until EOT; untap it",
            () =>
            {
                var target = ResolveTargetCreature(attackTrigger, land);
                if (target == null) return; // no legal target → no-op

                // CR 613.7c — +2/+0 with CR 514.2 end-of-turn expiry.
                effects?.Register(new PumpUntilEndOfTurnEffect(
                    target, PumpPower, PumpToughness));

                // CR 701.21 — "Untap that creature." Untap is one-shot (not a
                // continuous effect); it does not revert at end of turn.
                // Permanent.Untap throws if the permanent is not tapped, so
                // gate on IsTapped (no-op for e.g. a vigilant attacker).
                if (target.IsTapped) target.Untap();
            });

        attackTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnAttackSelf(land),
            effects: new IEffect[] { pumpEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[] { targetRequest });

        land.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return land;
    }

    /// <summary>
    /// CR 601.2c — candidate pool for "another target attacking creature":
    /// every battlefield <see cref="Creature"/> except the source land itself
    /// (the land can be a creature while animated, but the oracle says
    /// "another"). The "attacking" qualifier is a documented v1 narrowing —
    /// the engine has no per-creature attacking flag reachable here — so all
    /// other creatures are offered. Scans both players' battlefields.
    /// </summary>
    private static IReadOnlyList<object> GatherOtherCreatures(Land self)
    {
        var result = new List<object>();
        foreach (var p in new[] { self.Owner, self.Controller })
        {
            if (p == null) continue;
            foreach (var c in p.Zones.Battlefield.GetCards().OfType<Creature>())
            {
                if (ReferenceEquals(c, self)) continue;
                if (!result.Any(r => ReferenceEquals(r, c))) result.Add(c);
            }
        }
        return result;
    }

    /// <summary>
    /// CR 608.2c — read the chosen "another target attacking creature" from
    /// the trigger's <see cref="TriggeredAbility.ChosenTargets"/>. Returns
    /// null when no target was chosen or the chosen object is the source land
    /// itself (defensive — "another").
    /// </summary>
    private static Creature? ResolveTargetCreature(TriggeredAbility? trigger, Land self)
    {
        if (trigger is null
            || trigger.ChosenTargets.Count == 0
            || trigger.ChosenTargets[0].Count == 0)
        {
            return null;
        }

        var chosen = trigger.ChosenTargets[0][0] as Creature;
        if (chosen == null || ReferenceEquals(chosen, self)) return null;
        return chosen;
    }
}
