using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mistbreath Elder (Bloomburrow, {G}).
///
/// Creature — Frog Warrior 2/2. Oracle text (verified against the embedded
/// Modern seed, 2026-06-24):
///   "At the beginning of your upkeep, return another creature you control to
///    its owner's hand. If you do, put a +1/+1 counter on this creature.
///    Otherwise, you may return this creature to its owner's hand."
///
/// ## Shape source
/// Card identity (name, {G}, 2/2, Frog Warrior) is declarative JSON in
/// <c>Majik.Core/CardData/Cards/mistbreath-elder.json</c>, built through
/// <see cref="CardDefinitionFactory"/>. The upkeep ability is a BESPOKE
/// hand-rolled <see cref="TriggeredAbility"/> (mirroring
/// <see cref="RagavanNimblePilfererFactory"/> /
/// <see cref="SolitaryConfinementFactory"/>) because the declarative effect
/// vocabulary has no "if you do / otherwise" branch verb — the conditional
/// counter-vs-bounce-self split is the residual that lives here.
///
/// ## Upkeep branch (CR 500.1 / CR 603.1 / CR 603.6e)
/// At the beginning of the controller's upkeep:
///   1. <b>"return another creature you control"</b> (CR 109.5 "another" =
///      self-excluded; CR 701.20 bounce). This is MANDATORY — if the
///      controller controls one or more OTHER creatures, one MUST be returned.
///      The controller's agent chooses WHICH (a choice, not a "may") via
///      <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/>; with no agent
///      the deterministic default returns the first candidate.
///   2. <b>"If you do, put a +1/+1 counter on this creature"</b> (CR 122.1c) —
///      runs only when step 1 actually returned a creature (the source must
///      still be on the battlefield to receive the counter, CR 603.6e).
///   3. <b>"Otherwise, you may return this creature to its owner's hand"</b>
///      (CR 603.6e "otherwise" branch; CR 701.20) — reached only when the
///      controller controlled NO other creature, so nothing was returned in
///      step 1. The controller's agent is prompted yes/no whether to bounce
///      Mistbreath Elder itself; declining is a clean no-op.
///
/// Steps 1+2 and step 3 are mutually exclusive (CR 603.6e — exactly one branch
/// of an "if you do … otherwise" runs per resolution).
/// </summary>
[CardName("Mistbreath Elder")]
public static class MistbreathElderFactory
{
    public const string CardName = "Mistbreath Elder";
    public const string PrintedManaCost = "{G}";
    public const int Power = 2;
    public const int Toughness = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("mistbreath-elder");

    /// <summary>
    /// Shape-only build (no live ZoneService / TriggerManager). The upkeep
    /// trigger is attached but not registered; bounces use raw zone moves.
    /// Suitable for shape / dispatcher tests and the contract suite.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null);

    /// <summary>
    /// Fully wired build. <paramref name="zoneService"/> backs the bounces (so
    /// the returns publish <see cref="Majik.Core.Events.CardMovedEvent"/>);
    /// <paramref name="triggers"/> registers the upkeep ability so a
    /// <see cref="Majik.Core.Events.StepStartedEvent"/> on the controller's
    /// upkeep automatically queues it. Either may be null in shape paths.
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        var upkeepEffect = new Effect(
            $"{CardName}: return another creature you control (+1/+1 counter) "
            + "or you may return this creature",
            () => ResolveUpkeep(card, zoneService));

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, StepStateType.Upkeep),
            effects: new IEffect[] { upkeepEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>
    /// CR 603.1 — resolve the upkeep trigger. Exposed for tests / bots. See the
    /// class remarks for the full "if you do / otherwise" branch (CR 603.6e).
    /// </summary>
    public static void ResolveUpkeep(Creature card, ZoneService? zoneService)
    {
        if (card.Zone != ZoneType.Battlefield) return;
        var controller = card.Controller;
        if (controller == null) return;

        // "another creature you control" (CR 109.5 — self-excluded).
        var others = controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => !ReferenceEquals(c, card))
            .Cast<ICard>()
            .ToList();

        var agent = AgentRegistry.Get(controller);

        if (others.Count > 0)
        {
            // Mandatory return — the agent only chooses WHICH (CR 701.20).
            ICard? chosen = others[0];
            if (agent != null)
            {
                chosen = agent
                    .ChooseFromBattlefieldAsync(controller, others, BotIntent.Bounce)
                    .GetAwaiter().GetResult()
                    ?? others[0];
            }

            Fx.BounceToHand(chosen, zoneService);

            // "If you do, put a +1/+1 counter on this creature." (CR 122.1c)
            if (card.Zone == ZoneType.Battlefield)
                card.Counters.Add(CounterType.PlusOnePlusOne);

            return;
        }

        // "Otherwise, you may return this creature to its owner's hand."
        // (CR 603.6e otherwise-branch; CR 701.20.) Reached only when the
        // controller controls no other creature.
        bool bounceSelf = true; // deterministic default: bounce self.
        if (agent != null)
        {
            bounceSelf = agent
                .ChooseYesNoAsync(
                    $"{CardName}: return it to your hand?", BotIntent.Bounce)
                .GetAwaiter().GetResult();
        }

        if (bounceSelf) Fx.BounceToHand(card, zoneService);
    }
}
