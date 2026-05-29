using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Restless Vinestalk (Wilds of Eldraine "Restless"
/// creature-land cycle, G/U member — sibling of the Worldwake/BFZ/OGW
/// manland cycle handled by <see cref="LumberingFallsFactory"/> /
/// <see cref="DenOfTheBugbearFactory"/>). Land.
///
/// Oracle text (verified against Scryfall, WOE printing):
///   "This land enters tapped.
///    {T}: Add {G} or {U}.
///    {3}{G}{U}: Until end of turn, this land becomes a 5/5 green and blue
///    Plant creature with trample. It's still a land.
///    Whenever this land attacks, up to one other target creature has base
///    power and toughness 3/3 until end of turn."
///
/// ## Implemented (v1)
/// - Plain Land identity (no printed subtypes, no supertype).
/// - <b>Unconditional ETB-tapped (CR 614.1c)</b> — registered as an
///   <see cref="EntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/> (same shape as
///   <see cref="LumberingFallsFactory"/>). On the production load path the
///   <see cref="Majik.Core.CardData.EntersTappedBinder"/> applies it; this
///   factory builds the land without it when no bus is supplied (test
///   convenience).
/// - <b>{T}: Add {G} or {U}</b> — two vanilla <see cref="ManaAbility"/>
///   (CR 605.1, no stack), one per producible colour. Same shape as
///   Lumbering Falls.
/// - <b>{3}{G}{U}: animate until EOT</b> — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/> of
///   <c>{3}{G}{U}</c>. Resolution registers two end-of-turn-expirable
///   continuous effects against the supplied
///   <see cref="ContinuousEffectsService"/> (shared manland-cycle effects):
///     - Layer 4 (<see cref="ManlandCycleAnimateEffect"/>) — adds
///       <see cref="CardType.Creature"/>, the <see cref="CardSubtype.Plant"/>
///       subtype, and the "Trample" keyword. The printed Land type is left
///       intact ("It's still a land", CR 613.1c).
///     - Layer 7b (<see cref="ManlandCycleBecomesPTEffect"/>) — set-base
///       P/T 5/5 (CR 613.7b).
///   Both effects carry <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>
///   = true so <see cref="ContinuousEffectsService.ExpireEndOfTurn"/>
///   (CR 514.2 cleanup step) lifts the animation.
/// - <b>"Whenever this land attacks, up to one other target creature has
///   base power and toughness 3/3 until end of turn" trigger</b> — wired via
///   <see cref="Triggers.OnAttackSelf"/> against
///   <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>
///   (CR 508.1f). A 0..1 <see cref="TargetRequest"/> models "up to one OTHER
///   target creature" (CR 601.2c — optional, the candidate gatherer excludes
///   the land itself). On resolution, when a target is chosen, a
///   <see cref="BecomesPTUntilEndOfTurnEffect"/> (Layer 7b, ExpiresAtEndOfTurn)
///   is registered against the chosen creature setting its base P/T to 3/3
///   until end of turn (CR 613.7b). Zero targets → no-op.
///
/// ## Deferred (v1 gaps)
/// - <b>Green+blue colour of the animated form</b> — same gap as the entire
///   manland cycle (Lumbering Falls / Hive of the Eye Tyrant / Creeping Tar
///   Pit): the engine's colour layer (Layer 5) has no colour-setting effect
///   primitive yet. The Plant body should be green and blue while animated;
///   v1 records the intent but doesn't apply colour to the animated land.
/// - <b>Combat math through Compute</b>: same gap as the rest of the manland
///   cycle — until <see cref="ContinuousEffectsService.Compute(Permanent)"/>
///   upgrades to a <see cref="CreatureCharacteristics"/> row when Layer 4
///   grants <see cref="CardType.Creature"/>, the 5/5 doesn't surface for
///   combat resolution on the land itself. The 3/3 set-base on a TARGET
///   creature (already a Creature row) is fully observable via Compute.
/// - <b>Activation gate / sorcery-speed</b>: none — the animate ability is
///   instant-speed per oracle, no restriction needed.
/// </summary>
[CardName("Restless Vinestalk")]
public static class RestlessVinestalkFactory
{
    public const string CardName = "Restless Vinestalk";
    public const int AnimatedPower = 5;
    public const int AnimatedToughness = 5;
    public const int TargetBasePower = 3;
    public const int TargetBaseToughness = 3;

    /// <summary>
    /// Construct Restless Vinestalk with no <see cref="ContinuousEffectsService"/>,
    /// <see cref="ReplacementBus"/>, or <see cref="TriggerManager"/> wired.
    /// The mana abilities + the animate ability + the structural attack
    /// trigger are all attached so the card surface is complete; the layer
    /// effects are not registered, the ETB-tapped replacement is omitted,
    /// and the attack trigger is not auto-registered (its effect still runs
    /// when driven manually). Suitable for dispatcher / shape tests.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, effects: null, replacements: null, triggers: null);

    /// <summary>
    /// Construct Restless Vinestalk with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for the Layer 4 /
    /// Layer 7b animate registration and the attack trigger's set-base 3/3.
    /// May be null — the abilities still resolve but no continuous effects
    /// are recorded.</param>
    /// <param name="replacements">Replacement bus for the unconditional
    /// "this land enters tapped" rider (CR 614.1c). May be null — land
    /// enters untapped in that posture (mirrors how Lumbering Falls defers
    /// this to the production binder).</param>
    /// <param name="triggers">TriggerManager — when supplied the attack
    /// trigger is registered so a CreatureAttacksEvent matching this land
    /// queues the ability automatically. May be null — the trigger is still
    /// attached to the card shape and resolvable when driven manually.</param>
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
        // Unconditional ETB-tapped (CR 614.1c) — "This land enters tapped."
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {T}: Add {G} or {U}
        // CR 605.1 — mana abilities, no stack (one per producible colour).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("G")));
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("U")));

        // ----------------------------------------------------------------
        // {3}{G}{U}: Until end of turn, this land becomes a 5/5 green and
        // blue Plant creature with trample. It's still a land.
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost =
        // {3}{G}{U}, no tap rider. Resolution registers Layer 4 + Layer 7b
        // continuous effects flagged ExpiresAtEndOfTurn. Same posture as
        // LumberingFallsFactory.
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes {AnimatedPower}/{AnimatedToughness} green and blue Plant creature with trample until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature type + Plant subtype + Trample.
                // Printed Land type stays ("it's still a land", CR 613.1c).
                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: new[] { "Trample" },
                    subtypes: new[] { CardSubtype.Plant },
                    extraTypes: null));

                // Layer 7b — set base P/T 5/5.
                effects.Register(new ManlandCycleBecomesPTEffect(
                    land, AnimatedPower, AnimatedToughness));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{3}{G}{U}") },
            effects: new IEffect[] { animateEffect }));

        // ----------------------------------------------------------------
        // Attack trigger (animated form): "Whenever this land attacks, up to
        // one other target creature has base power and toughness 3/3 until
        // end of turn."
        //
        // CR 508.1f (attack trigger) / CR 601.2c (optional "up to one" =
        // 0..1 target) / CR 613.7b (set-base P/T) / CR 514.2 (EOT expiry).
        // The candidate gatherer enumerates OTHER creatures (excludes the
        // land itself per "other target creature"). On resolution, when a
        // target was chosen, registers a BecomesPTUntilEndOfTurnEffect against
        // that creature setting base P/T 3/3, flagged ExpiresAtEndOfTurn.
        // Zero targets chosen → no-op.
        // ----------------------------------------------------------------
        TriggeredAbility? attackTrigger = null;

        var targetRequest = new TargetRequest(
            Description: "up to one other target creature",
            MinTargets: 0,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            CandidateGatherer: ctx => GatherOtherCreatures(land));

        var pumpEffect = new Effect(
            $"{CardName}: up to one OTHER target creature has base P/T {TargetBasePower}/{TargetBaseToughness} until EOT (CR 613.7b)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                var target = ResolveTargetCreature(attackTrigger, land);
                if (target == null) return; // zero targets chosen → no-op

                // CR 613.7b set-base P/T 3/3 with CR 514.2 end-of-turn
                // expiry. BecomesPTUntilEndOfTurnEffect is the purpose-built
                // primitive for a set-base rider on a target that already has
                // the Creature row (unlike ManlandCycleBecomesPTEffect, which
                // pairs with a Layer-4 type-add on the manland itself and
                // no-ops on the PermanentCharacteristics path).
                effects.Register(new BecomesPTUntilEndOfTurnEffect(
                    target, TargetBasePower, TargetBaseToughness));
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
    /// CR 601.2c — candidate pool for "up to one OTHER target creature":
    /// every battlefield <see cref="Creature"/> except the source land
    /// itself (the land can be a creature while animated, but the oracle
    /// says "other"). Scans both players' battlefields.
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
    /// CR 608.2c — read the chosen "up to one other target creature" from the
    /// trigger's <see cref="TriggeredAbility.ChosenTargets"/>. Returns null
    /// when zero targets were chosen (legal under "up to one") or the chosen
    /// object is the source land itself (defensive — "other").
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
