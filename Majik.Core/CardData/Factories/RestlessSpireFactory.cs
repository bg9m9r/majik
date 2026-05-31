using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Restless Spire (The Lost Caverns of Ixalan
/// "Restless" creature-land cycle, blue/red member — sibling of
/// <see cref="RestlessBivouacFactory"/> / <see cref="RestlessVinestalkFactory"/>).
/// Land.
///
/// Oracle text (verified against Scryfall 2026-05-29):
///   "This land enters tapped.
///    {T}: Add {U} or {R}.
///    {U}{R}: Until end of turn, this land becomes a 2/1 blue and red
///    Elemental creature with \"During your turn, this creature has first
///    strike.\" It's still a land.
///    Whenever this land attacks, scry 1."
///
/// Shares the manland animate shape used by <see cref="RestlessBivouacFactory"/>:
/// unconditional ETB-tapped (CR 614.1c), two mana abilities (one per colour,
/// CR 605.1), and a {U}{R} animate-until-EOT <see cref="ActivatedAbility"/>
/// whose resolution registers a <see cref="ManlandCycleAnimateEffect"/>
/// (Layer 4 — add Creature + Elemental subtype + First Strike keyword) and a
/// <see cref="ManlandCycleBecomesPTEffect"/> (Layer 7b — set base P/T 2/1),
/// both flagged <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> so
/// <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> (CR 514.2 cleanup
/// step) lifts the animation.
///
/// The base shape (plain nonbasic Land + the two colour mana abilities) is
/// materialised from the embedded JSON definition (<c>restless-spire.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the ETB-tapped rider, the
/// animate ability, and the attack trigger are layered on here because the
/// JSON <c>AbilityDefinition</c> schema expresses none of them yet (same
/// posture as <see cref="RestlessBivouacFactory"/>).
///
/// ## What is unique to Restless Spire vs the rest of the cycle
/// The land has an intrinsic triggered ability printed on the land (CR 508.1f):
/// "Whenever this land attacks, scry 1." It is attached to the land
/// unconditionally and registered with the supplied <see cref="TriggerManager"/>
/// up front. A land can only attack while it is the animated 2/1 creature
/// (CR 508.1a), so the trigger is unreachable until the controller activates
/// the {U}{R} ability the same turn. Resolution scries the controller's
/// library by 1 (CR 701.20) via <see cref="ScryAction"/> — agent-driven when
/// an <see cref="IPlayerAgent"/> is registered, default-to-bottom otherwise
/// (mirrors <see cref="CuratorOfMysteriesFactory"/>).
///
/// ## v1 posture (shared with the manland cycle)
/// - <b>Blue/red colour identity of the animated form</b> — the engine has no
///   Layer-5 colour-set primitive yet (same gap as Raging Ravine / Restless
///   Bivouac). Recorded only in the effect-name string. The Elemental
///   subtype, 2/1 body, and First Strike keyword DO apply via Compute.
/// - <b>"During your turn" first-strike qualifier</b> — the engine has no
///   conditional-keyword (intervening-condition keyword grant) primitive.
///   v1 grants First Strike flatly while animated. This is observationally
///   equivalent for this card: animation expires at the cleanup step of the
///   turn it was activated (CR 514.2), so the land is never a creature
///   outside its controller's turn, and a land can only attack during its
///   controller's combat (CR 508.1a). The "during your turn" qualifier is
///   recorded here only.
/// - <b>Combat math through Compute</b> — same gap as every other manland:
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> seeds a plain
///   <see cref="PermanentCharacteristics"/> row for a Land runtime instance,
///   so the 2/1 + First Strike are recorded for inspection but don't surface
///   for combat resolution yet.
/// - <b>Attack-trigger reachability</b> — <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent.Attacker"/>
///   is typed <see cref="Creature"/>, but a Restless Spire runtime instance
///   is always a <see cref="Land"/>; production never passes the land as the
///   event's Attacker, so the printed trigger is observationally equivalent
///   to one reachable only while animated. The scry effect is wired and
///   correct; only the production fire-path is the shared cycle gap. It runs
///   when driven manually.
/// </summary>
[CardName("Restless Spire")]
public static class RestlessSpireFactory
{
    public const string CardName = "Restless Spire";
    public const string Slug = "restless-spire";
    public const int AnimatedPower = 2;
    public const int AnimatedToughness = 1;
    public const int ScryAmount = 1;

    /// <summary>
    /// Construct Restless Spire with no <see cref="ContinuousEffectsService"/>,
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
    /// Construct Restless Spire with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for Layer 4 + Layer
    /// 7b registration of the animate ability. May be null — the ability
    /// still resolves but no continuous effects are recorded.</param>
    /// <param name="replacements">Replacement bus for the unconditional
    /// enters-tapped rider (CR 614.1c). May be null.</param>
    /// <param name="triggers">Trigger manager the intrinsic "Whenever this
    /// land attacks, scry 1" ability is registered with. May be null — the
    /// trigger is still attached to the land's ability list but won't fire
    /// from the event bus.</param>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type,
        // {T}: Add {U} / {T}: Add {R} mana abilities). The ETB-tapped rider,
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
        // {U}{R}: Until end of turn, this land becomes a 2/1 blue and red
        // Elemental creature with "During your turn, this creature has first
        // strike." It's still a land.
        //
        // CR 602 — ordinary activated ability (uses the stack). Resolution
        // registers Layer 4 + Layer 7b continuous effects flagged
        // ExpiresAtEndOfTurn (CR 514.2). See class doc for the "during your
        // turn" first-strike v1 posture.
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: becomes {AnimatedPower}/{AnimatedToughness} blue and red Elemental creature with first strike (during your turn) until EOT (still a land)",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 4 — add Creature type + Elemental subtype + First
                // Strike keyword. Printed Land type stays ("it's still a
                // land", CR 613.1c).
                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: new[] { "First Strike" },
                    subtypes: new[] { CardSubtype.Elemental },
                    extraTypes: null));

                // Layer 7b — set base P/T 2/1.
                effects.Register(new ManlandCycleBecomesPTEffect(
                    land, AnimatedPower, AnimatedToughness));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{U}{R}") },
            effects: new IEffect[] { animateEffect }));

        // ----------------------------------------------------------------
        // Intrinsic attack trigger (printed on the land, CR 508.1f):
        //   "Whenever this land attacks, scry 1."
        //
        // Attached unconditionally (the trigger is printed on the land
        // itself). A land can only attack while it's the animated 2/1
        // (CR 508.1a), so the trigger is unreachable until the {U}{R} ability
        // resolves the same turn. No targets. On resolution it scries the
        // controller's library by 1 (CR 701.20) — agent-driven when an agent
        // is registered, default-to-bottom otherwise (mirrors Curator of
        // Mysteries / Preordain).
        // ----------------------------------------------------------------
        var scryEffect = new Effect(
            $"{CardName}: scry {ScryAmount}",
            async ctx =>
            {
                var controller = land.Controller ?? owner;
                var peeked = ScryAction.Peek(controller, ScryAmount);
                if (peeked.Count == 0) return;

                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                ScryAction.ScryDecision decision;
                if (agent != null)
                {
                    // TODO: drop sync-over-async once IEffect.Execute becomes async.
                    decision = (await agent.ChooseScryDecisionAsync( ctx.Game, peeked).ConfigureAwait(false));
                }
                else
                {
                    // Pre-agent default: send to bottom (matches Curator of
                    // Mysteries / PreordainFactory / LibrarySpellFactory).
                    decision = new ScryAction.ScryDecision(
                        ToBottom: peeked.ToList(),
                        TopOrder: Array.Empty<ICard>());
                }
                ScryAction.Apply(controller, peeked.Count, decision);
            });

        var attackTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnAttackSelf(land),
            effects: new IEffect[] { scryEffect },
            activeZones: new[] { ZoneType.Battlefield });

        land.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return land;
    }
}
