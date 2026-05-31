using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sneak Attack (Urza's Saga, {2}{R}).
///
/// Enchantment. Oracle text:
///   "{R}: You may put a creature card from your hand onto the battlefield.
///    That creature gains haste. Sacrifice it at the beginning of the next
///    end step."
///
/// ## Implemented (v1)
/// - Enchantment shape, mana cost {2}{R}, owner / controller wired.
/// - <b>{R} activated ability (CR 602)</b> wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/>("{R}").
///   No tap cost — the ability is repeatable in the same turn provided the
///   controller can keep paying {R}. Each activation triggers an independent
///   put-from-hand + haste-grant + delayed end-step sac for the picked
///   creature.
/// - Resolution effect (per activation):
///   1. Picks a creature card from the controller's hand. v1 deterministic
///      first-creature pick — same "you may" auto-accept shape as Aether
///      Vial / Through the Breach. If no creature is in hand the activation
///      is a clean no-op (CR 117.x — "you may" with no valid target).
///   2. Moves the picked creature from hand to the battlefield. Routes
///      through <see cref="ZoneService.MoveCard"/> when supplied so ETB
///      triggers on the placed creature fire (CR 603.6a). Falls back to
///      raw zone manipulation otherwise (shape-only path).
///   3. Grants Haste until end of turn via
///      <see cref="GrantKeywordUntilEndOfTurnEffect"/> on the placed
///      creature's <see cref="Creature.ActiveEffects"/> when one is
///      attached (CR 613.1c Layer 6 / CR 702.10). The printed card says
///      "gains haste" with no duration; since the creature is sacrificed
///      at the next end step anyway (and the keyword grant expires at the
///      same boundary), the EOT-scoped grant is observationally equivalent
///      to the printed "no duration" wording for the creature's lifetime.
///   4. Registers a one-shot <see cref="DelayedTriggeredAbility"/>
///      (CR 603.7) on the supplied <see cref="TriggerManager"/> that
///      sacrifices the placed creature at the start of the next end step
///      (CR 500.4 / CR 701.16 — controller's battlefield → owner's
///      graveyard). The trigger fence-checks <c>e.Timestamp &gt; resolvedAt</c>
///      so the current end step (if any) doesn't trip it (mirrors the
///      activation-time fence used by Mishra's Bauble / Wrenn's Resolve /
///      Splinter Twin / Through the Breach).
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" prompt</b>: consults the optional
///   <see cref="IPlayerAgent"/> via
///   <see cref="IPlayerAgent.ChooseYesNoAsync"/>
///   (<see cref="BotIntent.CheatIntoPlay"/>) +
///   <see cref="IPlayerAgent.ChooseFromHandAsync"/>; with no agent passed
///   the legacy deterministic first-creature pick + auto-accept posture
///   remains (Aether Vial / Goblin Lackey shape).
/// - <b>Empty-hand / no-creature-in-hand</b>: clean no-op. The ability
///   still resolves and the {R} is still paid (CR 117.6 / 602.5b — an
///   activation that produces no effect is legal); no creature is put
///   onto the battlefield and no delayed trigger is registered (there
///   is nothing to sacrifice).
/// - <b>ActiveEffects on placed creature</b>: if the picked creature has
///   no <see cref="Creature.ActiveEffects"/> wired (test/shape mode),
///   the Haste grant is skipped silently and the engine falls back to
///   clearing the summoning-sickness flag so attack-declaration sees
///   the creature as haste-ready (CR 702.10b). Production callers wire
///   a <see cref="ContinuousEffectsService"/> on creatures before they
///   move to the battlefield (same shape as Reckless Charge's pump path).
/// </summary>
[CardName("Sneak Attack")]
public static class SneakAttackFactory
{
    public const string CardName = "Sneak Attack";
    public const string PrintedManaCost = "{2}{R}";

    /// <summary>Granted keyword. CR 702.10 — Haste.</summary>
    public const string GrantedKeyword = "Haste";

    /// <summary>Activation cost (mana only). CR 602.</summary>
    public const string ActivationCost = "{R}";

    /// <summary>
    /// Construct Sneak Attack with no live runtime wiring. The {R}
    /// activated ability is attached to the card shape; its resolution
    /// uses raw zone manipulation and no delayed trigger is registered.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, zoneService: null, triggers: null, agent: null);

    /// <summary>
    /// Construct a fully-wired Sneak Attack. When
    /// <paramref name="zoneService"/> is supplied the put-from-hand move
    /// routes through <see cref="ZoneService.MoveCard"/> so ETB triggers
    /// on the placed creature fire (CR 603.6a). When
    /// <paramref name="triggers"/> is supplied each activation registers
    /// its own delayed end-step sacrifice trigger (CR 603.7).
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
        => Create(owner, zoneService, triggers, agent: null);

    /// <summary>
    /// Construct a fully-wired Sneak Attack with agent-prompt wiring.
    /// <paramref name="agent"/> is consulted at resolution time for the
    /// "you may" + creature pick — see class xmldoc. Null preserves the
    /// legacy deterministic v1 behavior.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers,
        IPlayerAgent? agent)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // {R}: You may put a creature card from your hand onto the
        // battlefield. That creature gains haste. Sacrifice it at the
        // beginning of the next end step. CR 602 — activated ability.
        //
        // No tap cost — repeatable each time the controller can pay {R}.
        // Each activation closes over its own resolve-time creature pick
        // and (when wired) its own delayed end-step sac. Multiple
        // activations in the same turn each register an independent
        // delayed trigger so every cheated-in creature gets sacrificed.
        // ----------------------------------------------------------------
        var effect = new Effect(
            $"{CardName}: put creature from hand → battlefield, haste, sac next end step",
            ctx => ResolveActivationAsync(card, owner, zoneService, triggers, agent, ctx));

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(ActivationCost) },
            effects: new IEffect[] { effect });

        card.AddAbility(ability);

        return card;
    }

    /// <summary>
    /// Resolve a single activation: pick a creature from the controller's
    /// hand, move it to the battlefield, grant Haste, and (when
    /// <paramref name="triggers"/> is supplied) register a delayed
    /// end-step sacrifice trigger that closes over the placed creature.
    /// No-ops cleanly when no creature is in hand.
    /// </summary>
    private static async ValueTask ResolveActivationAsync(
        Enchantment source,
        Player controller,
        ZoneService? zoneService,
        TriggerManager? triggers,
        IPlayerAgent? agent,
        ResolutionContext ctx)
    {
        agent = ctx.Agent ?? agent ?? AgentRegistry.Get(controller);
        // -------------------------------------------------------------------
        // "You may put a creature card from your hand onto the battlefield."
        // Agent path (prompts MVP): ChooseYesNoAsync(CheatIntoPlay) +
        // ChooseFromHandAsync. No agent → deterministic v1 first-creature
        // pick + auto-accept (preserves Aether Vial / Goblin Lackey shape).
        // No creature in hand → no-op.
        // -------------------------------------------------------------------
        var creatures = controller.Zones.Hand.GetCards()
            .OfType<Creature>()
            .Cast<ICard>()
            .ToList();
        if (creatures.Count == 0) return;

        Creature? pick;
        if (agent != null)
        {
            // CR 117.x — "you may" prompt. Decline = no-op (the {R} was
            // still paid for the activation).
            var yes = await agent.ChooseYesNoAsync(
                "Put a creature card from your hand onto the battlefield?",
                BotIntent.CheatIntoPlay).ConfigureAwait(false);
            if (!yes) return;

            var chosen = await agent.ChooseFromHandAsync(
                controller, creatures, BotIntent.CheatIntoPlay)
                .ConfigureAwait(false);
            if (chosen is not Creature c) return;
            // Sanity — pick must still be in hand.
            if (c.Zone != ZoneType.Hand) return;
            pick = c;
        }
        else
        {
            pick = (Creature)creatures[0];
        }

        // -------------------------------------------------------------------
        // Hand → Battlefield. Routes through ZoneService when supplied so
        // CardMovedEvent publishes (ETB triggers on the placed creature
        // fire — CR 603.6a). Raw zone manipulation is the shape-only path
        // (mirrors Through the Breach / Aether Vial / Goblin Lackey).
        // -------------------------------------------------------------------
        if (zoneService != null)
        {
            zoneService.MoveCard(pick, ZoneType.Hand, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Hand.RemoveCard(pick);
            controller.Zones.Battlefield.AddCard(pick);
            pick.SetZone(ZoneType.Battlefield);
            pick.SetController(controller);
        }

        // -------------------------------------------------------------------
        // "That creature gains haste."
        // CR 613.1c (Layer 6) — keyword grant. Printed card has no explicit
        // duration; since the creature is sacrificed at the next end step
        // (the same boundary at which an EOT-scoped grant expires), the
        // EOT-scoped GrantKeywordUntilEndOfTurnEffect is observationally
        // equivalent to a no-duration grant for the creature's lifetime.
        // No-op silently when no ActiveEffects service is wired (test/shape
        // mode); the summoning-sickness clear below still applies so
        // attack-declaration sees haste behaviour (CR 702.10b).
        // -------------------------------------------------------------------
        if (pick.ActiveEffects != null)
        {
            pick.ActiveEffects.Register(
                new GrantKeywordUntilEndOfTurnEffect(pick, GrantedKeyword));
        }
        // CR 702.10b — Haste lifts summoning sickness for attack-declaration.
        pick.HasSummoningSickness = false;

        // -------------------------------------------------------------------
        // "Sacrifice it at the beginning of the next end step."
        // CR 603.7 — one-shot delayed triggered ability. Fires on the first
        // StepStartedEvent(End) strictly after this activation resolves
        // (activation-time fence mirrors Through the Breach / Splinter
        // Twin). Resolution moves the creature from controller's battlefield
        // to owner's graveyard (CR 701.16). Guards the zone check at fire
        // time so a creature that's already left the battlefield (bounce,
        // destroy, exile) doesn't get yanked from elsewhere.
        // -------------------------------------------------------------------
        if (triggers == null) return;

        var resolvedAt = DateTime.UtcNow;
        var sacEffect = new Effect(
            $"{CardName}: sacrifice {pick.Name} at next end step",
            () =>
            {
                if (pick.Zone != ZoneType.Battlefield) return;
                var battlefield = pick.Controller?.Zones.Battlefield;
                if (battlefield == null) return;
                if (!battlefield.GetCards().Contains(pick)) return;

                // CR 701.16 — sacrifice: controller's battlefield → owner's
                // graveyard. ZoneService routes the publish when supplied.
                var bfPlayer = pick.Controller!;
                var graveyardOwner = pick.Owner ?? controller;
                if (zoneService != null)
                {
                    zoneService.MoveCard(
                        pick, ZoneType.Battlefield, ZoneType.Graveyard, bfPlayer);
                }
                else
                {
                    bfPlayer.Zones.Battlefield.RemoveCard(pick);
                    graveyardOwner.Zones.Graveyard.AddCard(pick);
                    pick.SetZone(ZoneType.Graveyard);
                }
            });

        var delayed = new DelayedTriggeredAbility(
            source: source,
            controller: controller,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == PhaseStateType.End
                          && e.Timestamp > resolvedAt),
            effects: new IEffect[] { sacEffect });

        triggers.RegisterDelayed(delayed);
    }
}
