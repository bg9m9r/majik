using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Restless Bivouac (March of the Machine "Restless"
/// creature-land cycle, red/white member). Land.
///
/// Oracle text (verified against Scryfall 2026-05-29):
///   "This land enters tapped.
///    {T}: Add {R} or {W}.
///    {1}{R}{W}: This land becomes a 2/2 red and white Ox creature until
///    end of turn. It's still a land.
///    Whenever this land attacks, put a +1/+1 counter on target creature
///    you control."
///
/// Shares the manland shape used by <see cref="StirringWildwoodFactory"/> /
/// <see cref="RagingRavineFactory"/>: unconditional ETB-tapped (CR 614.1c),
/// two mana abilities (one per colour, CR 605.1), and a {cost}: animate-
/// until-EOT <see cref="ActivatedAbility"/> whose resolution registers a
/// <see cref="ManlandCycleAnimateEffect"/> (Layer 4 — add Creature + Ox
/// subtype) and a <see cref="ManlandCycleBecomesPTEffect"/> (Layer 7b — set
/// base P/T 2/2), both flagged <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>
/// so <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> (CR 514.2
/// cleanup step) lifts the animation.
///
/// The base shape (plain nonbasic Land + the two colour mana abilities) is
/// materialised from the embedded JSON definition
/// (<c>restless-bivouac.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the ETB-tapped rider, the
/// animate ability, and the attack trigger are layered on here because the
/// JSON <c>AbilityDefinition</c> schema expresses none of them yet (same
/// posture as <see cref="CaveOfTheFrostDragonFactory"/> /
/// <see cref="StormscaleScionFactory"/>).
///
/// ## What is unique to Restless Bivouac vs the rest of the cycle
/// The land has an intrinsic triggered ability — "Whenever this land
/// attacks, put a +1/+1 counter on target creature you control."
/// (CR 508.1f). Crucially the trigger is printed on the <b>land</b>, not on
/// the animated creature body (contrast <see cref="RagingRavineFactory"/>,
/// whose attack trigger is part of the granted creature's ability set), so
/// it is attached to the land unconditionally and registered with the
/// supplied <see cref="TriggerManager"/> up front. A land can only attack
/// while it is the animated 2/2 creature (CR 508.1a), so the trigger is
/// unreachable until the controller activates the {1}{R}{W} ability the
/// same turn. v1 wires it as a <see cref="TriggeredAbility"/> over
/// <see cref="CreatureAttacksEvent"/> with a 1..1 "target creature you
/// control" <see cref="TargetRequest"/>; resolution reads
/// <see cref="TriggeredAbility.ChosenTargets"/>, rechecks legality
/// (CR 608.2b — target must still be a Creature this player controls on the
/// battlefield), and places one <see cref="CounterType.PlusOnePlusOne"/>
/// counter (mirrors <see cref="GenerousVisitorFactory"/>'s targeted-counter
/// trigger).
///
/// ## v1 posture (shared with the manland cycle)
/// - <b>Red/white colour identity of the animated form</b> — the engine has
///   no Layer-5 colour-set primitive yet (same gap as Raging Ravine /
///   Needle Spires). Recorded only in the effect-name string.
/// - <b>Combat math through Compute</b> — same gap as every other manland:
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> seeds a plain
///   <see cref="PermanentCharacteristics"/> row for a Land runtime instance,
///   so the 2/2 is recorded for inspection but doesn't surface for combat
///   resolution yet.
/// - <b>Agent-driven target prompt</b> — the attack trigger honours pre-set
///   <see cref="TriggeredAbility.ChosenTargets"/>; the factory does not wire
///   an <see cref="IPlayerAgent"/> prompt (same posture as Generous
///   Visitor). Tests set chosen targets via
///   <see cref="TriggeredAbility.SetChosenTargets"/> directly.
/// </summary>
[CardName("Restless Bivouac")]
public static class RestlessBivouacFactory
{
    public const string CardName = "Restless Bivouac";
    public const string Slug = "restless-bivouac";
    public const int AnimatedPower = 2;
    public const int AnimatedToughness = 2;

    /// <summary>
    /// Construct Restless Bivouac with no <see cref="ContinuousEffectsService"/>,
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
    /// Construct Restless Bivouac with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 + Layer
    /// 7b registration of the animate ability. May be null — the ability
    /// still resolves but no continuous effects are recorded.</param>
    /// <param name="replacements">Replacement bus for the unconditional
    /// enters-tapped rider (CR 614.1c). May be null.</param>
    /// <param name="triggers">Trigger manager the intrinsic "Whenever this
    /// land attacks, put a +1/+1 counter on target creature you control"
    /// ability is registered with. May be null — the trigger is still
    /// attached to the land's ability list but won't fire from the event
    /// bus.</param>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type,
        // {T}: Add {R} / {T}: Add {W} mana abilities). The ETB-tapped rider,
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
        // {1}{R}{W}: This land becomes a 2/2 red and white Ox creature until
        // end of turn. It's still a land.
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost =
        // {1}{R}{W}, no tap rider. Resolution registers Layer 4 + Layer 7b
        // continuous effects flagged ExpiresAtEndOfTurn (CR 514.2).
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes {AnimatedPower}/{AnimatedToughness} red and white Ox creature until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature type + Ox subtype. No printed
                // keywords on the animated body. Printed Land type stays
                // ("it's still a land", CR 613.1c).
                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: Array.Empty<string>(),
                    subtypes: new[] { CardSubtype.Ox },
                    extraTypes: null));

                // Layer 7b — set base P/T 2/2.
                effects.Register(new ManlandCycleBecomesPTEffect(
                    land, AnimatedPower, AnimatedToughness));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{1}{R}{W}") },
            effects: new IEffect[] { animateEffect }));

        // ----------------------------------------------------------------
        // Intrinsic attack trigger (printed on the land, CR 508.1f):
        //   "Whenever this land attacks, put a +1/+1 counter on target
        //    creature you control."
        //
        // Attached unconditionally (NOT granted by the animate effect — the
        // trigger is printed on the land itself; contrast Raging Ravine
        // whose attack trigger belongs to the animated creature body). A
        // land can only attack while it's the animated 2/2 (CR 508.1a), so
        // the trigger is unreachable until the {1}{R}{W} ability resolves
        // the same turn. On resolution it reads the chosen target, rechecks
        // legality (CR 608.2b — still a creature this player controls on the
        // battlefield), and adds one +1/+1 counter.
        // ----------------------------------------------------------------
        TriggeredAbility? attackTrigger = null;
        var counterEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on target creature you control",
            () =>
            {
                if (attackTrigger == null) return;
                if (attackTrigger.ChosenTargets.Count == 0) return;
                if (attackTrigger.ChosenTargets[0].Count == 0) return;

                var raw = attackTrigger.ChosenTargets[0][0];
                if (raw is not Permanent target) return;

                // CR 608.2b — resolve-time legality recheck. The chosen
                // target must still be a Creature on the battlefield that
                // this trigger's controller controls ("you control").
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Creature)) return;
                if (!ReferenceEquals(target.Controller, land.Controller)) return;

                target.Counters.Add(CounterType.PlusOnePlusOne, 1);
            });

        attackTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnAttackSelf(land),
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff),
            });

        land.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return land;
    }
}
