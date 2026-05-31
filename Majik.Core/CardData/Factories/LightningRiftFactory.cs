using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lightning Rift (Onslaught, {1}{R}).
///
/// Enchantment. Oracle text:
///   "Whenever a player cycles a card, you may pay {1}. If you do,
///    Lightning Rift deals 2 damage to any target."
///
/// ## Implemented (v1)
/// - <b>Enchantment {1}{R}</b> — vanilla card shape, owner / controller
///   wired.
/// - <b>"Whenever a player cycles a card" trigger (CR 603.1)</b> over
///   the new <see cref="CardCycledEvent"/> published by
///   <see cref="Majik.Core.Keywords.CyclingFactory"/> on cycling-ability
///   resolve. The trigger is symmetric — fires whether the cycling
///   player is the Rift's controller or an opponent, matching the
///   printed "a player" wording.
/// - <b>"You may pay {1}" optional rider</b> — on resolve the controller
///   pays {1} from their mana pool if able (v1 auto-pay-from-pool
///   posture; same shape as Daze / Mana Leak / Cursecatcher).
///   <see cref="IPlayerAgent.ChooseYesNoAsync"/> overrides the
///   auto-pay when an agent is registered for the Rift's controller —
///   "yes" → attempt PayMana; "no" → trigger fizzles harmlessly
///   (CR 117.5 the optional may-pay is a no-op on decline).
/// - <b>"Deals 2 damage to any target" effect (CR 119.3)</b> — single
///   1..1 "any target" <see cref="TargetRequest"/>; on resolve routes
///   2 damage through <see cref="Fx.DealDamageAny"/> (Player /
///   Creature / Planeswalker — CR 306.7 loyalty removal on planeswalker
///   targets). Mirrors the Burst Lightning / Lava Dart "any target"
///   shape.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. Trigger attached
///   for shape inspection but not registered with a
///   <see cref="TriggerManager"/>; the cycling event never reaches it.
///   Suitable for dispatcher / shape tests.
/// - <see cref="Create(Player, TriggerManager?)"/> — fully wired. The
///   trigger is registered so cycling events auto-queue the ability.
///
/// ## Deferred (v1 gaps)
/// - <b>Target choice prompt at trigger queue time</b>: the
///   <see cref="TriggeredAbility.TargetRequests"/> entry is declared so
///   the <see cref="TriggerManager.PutPendingTriggersOnStackAsync"/>
///   agent-prompt path picks a target before the trigger hits the
///   stack. The sync path's pre-supplied
///   <see cref="ActivatedAbility.SetChosenTargets"/> is honoured;
///   absent a target the damage no-ops (CR 608.2b).
/// - <b>Damage-dealt event surface</b>: the damage routes through
///   <see cref="Fx.DealDamageAny"/> which (for creature / planeswalker
///   targets) goes through the standard damage pipeline; player damage
///   currently routes via <see cref="Player.LoseLife"/>. Damage-
///   prevention shields gate uniformly through Fx.
/// </summary>
[CardName("Lightning Rift")]
public static class LightningRiftFactory
{
    public const string CardName = "Lightning Rift";
    public const string PrintedManaCost = "{1}{R}";
    public const int DamageAmount = 2;
    public const int OptionalManaCost = 1;

    /// <summary>
    /// Construct Lightning Rift with no live trigger wiring. The
    /// cycling trigger is attached to the card for shape inspection but
    /// not registered with a <see cref="TriggerManager"/>; cycling
    /// events do not reach it.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Lightning Rift. When <paramref name="triggers"/> is
    /// supplied the cycling trigger is registered so
    /// <see cref="CardCycledEvent"/> publications auto-queue it.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager — when supplied the
    /// trigger is registered so the bus drives it automatically.</param>
    public static Enchantment Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "Whenever a player cycles a card, you may pay {1}. If you do,
        // Lightning Rift deals 2 damage to any target." (CR 603.1)
        //
        // EventTriggerCondition over CardCycledEvent — symmetric (any
        // player cycling fires the trigger, matching the printed "a
        // player" wording).
        // ----------------------------------------------------------------
        TriggeredAbility? trigger = null;

        var rideEffect = new Effect(
            $"{CardName}: may pay {{1}} → 2 damage to any target",
            async ctx =>
            {
                if (trigger is null) return;
                // Source must still be on the battlefield to deal damage
                // (CR 603.6c — leaves-the-battlefield triggers exempt;
                // this trigger doesn't, so if Rift left the battlefield
                // between event and resolve it no-ops).
                if (card.Zone != ZoneType.Battlefield) return;

                // "You may pay {1}" — consult the controller's agent. v1
                // falls back to "auto-pay if able" (Daze / Mana Leak
                // posture) when no agent is registered.
                var oneGeneric = ManaCost.Zero.AddGenericCost(OptionalManaCost);
                var rifController = card.Controller ?? owner;
                var agent = ctx.Agent ?? AgentRegistry.Get(rifController);
                bool pay;
                if (agent != null)
                {
                    pay = (await agent.ChooseYesNoAsync(
                        $"Pay {{{OptionalManaCost}}} for {CardName} to deal {DamageAmount} damage?",
                        BotIntent.Burn).ConfigureAwait(false));
                }
                else
                {
                    // No agent: auto-pay if able (v1 posture).
                    pay = true;
                }

                if (!pay) return;

                // CR 117.5 — optional may-pay; trigger fizzles if the
                // mana isn't available. PayMana returns false when the
                // pool can't satisfy {1}.
                if (!rifController.PayMana(oneGeneric)) return;

                // Resolve target → 2 damage. v1 deterministic-no-op when
                // the agent didn't pre-supply a target (CR 608.2b).
                if (trigger.ChosenTargets.Count == 0
                    || trigger.ChosenTargets[0].Count == 0)
                {
                    return;
                }
                var target = trigger.ChosenTargets[0][0];
                Fx.DealDamageAny(target, DamageAmount);
            });

        var condition = new EventTriggerCondition<CardCycledEvent>((_, _) => true);

        trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { rideEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Burn),
            });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
