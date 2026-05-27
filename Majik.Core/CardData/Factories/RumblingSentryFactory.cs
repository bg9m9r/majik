using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Rumbling Sentry (Onslaught, {3}{W}{W}).
///
/// Creature — Giant 3/6. Oracle text:
///   "When this creature enters, scry 1."
///
/// ## Implementation
///
/// - 3/6 <see cref="Creature"/> with <see cref="CardSubtype.Giant"/>, mana
///   cost {3}{W}{W} (mana value 5, white — CR 202.3 / CR 105.1).
/// - No keyword abilities (no Flying, Menace, or other evasion).
/// - <b>ETB triggered ability</b> (CR 603.1, CR 603.6a): "When this creature
///   enters, scry 1." Unconditional self-ETB via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> — no intervening-if
///   clause (CR 603.4 does not apply). Resolution runs the standard
///   <see cref="ScryAction"/> pipeline for N=1: peek the top card, consult
///   the registered <see cref="IPlayerAgent"/> for the keep/bottom decision,
///   then apply the result. Falls back to the pre-agent default (send peeked
///   card to bottom) when no agent is registered. An empty library
///   short-circuits cleanly (CR 701.20). Active only from the battlefield
///   (CR 603.6a).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. ETB trigger attached to card
///   for shape inspection; not registered with a <see cref="TriggerManager"/>.
///   Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, Events.IEventBus?, TriggerManager?)"/> —
///   fully wired. ETB trigger registered with <paramref name="triggers"/>
///   so <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>s on the
///   bus route it to the stack.
///
/// ## Design notes
/// Mirrors <see cref="CloudkinSeerFactory"/>'s ETB-trigger wiring shape but
/// replaces the draw body with a <see cref="ScryAction"/>(1) body drawn from
/// <see cref="OptFactory"/> / <see cref="PreordainFactory"/>. The controller
/// closure re-resolves at execute time (<c>card.Controller ?? owner</c>) to
/// handle blink / control-change corner cases correctly.
/// </summary>
[CardName("Rumbling Sentry")]
public static class RumblingSentryFactory
{
    public const string CardName = "Rumbling Sentry";
    public const string PrintedManaCost = "{3}{W}{W}";
    public const int Power = 3;
    public const int Toughness = 6;
    private const int ScryAmount = 1;

    /// <summary>
    /// Construct Rumbling Sentry with no live wiring. The ETB trigger is
    /// attached to the card for shape inspection; not registered with any
    /// <see cref="TriggerManager"/>. Suitable for dispatcher / structural
    /// tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Rumbling Sentry with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>s
    /// published on the bus route it to the stack.
    /// </summary>
    public static Creature Create(
        Player owner,
        Events.IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Giant });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.1 / CR 603.6a).
        //   "When this creature enters, scry 1."
        // Unconditional self-ETB — no intervening-if (CR 603.4 does not
        // apply here). Uses the standard ScryAction pipeline so registered
        // agents receive the peek/bottom choice. The controller closure
        // re-resolves at execute time to handle blink / control-change.
        // An empty library short-circuits cleanly per CR 701.20.
        // ----------------------------------------------------------------
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: scry {ScryAmount} (when this creature enters)",
            () =>
            {
                var controller = card.Controller ?? owner;

                var peeked = ScryAction.Peek(controller, ScryAmount);
                if (peeked.Count > 0)
                {
                    var agent = AgentRegistry.Get(controller);
                    ScryAction.ScryDecision decision;
                    if (agent != null)
                    {
                        // TODO: drop sync-over-async once IEffect.Execute becomes async.
                        decision = agent.ChooseScryDecisionAsync(null, peeked)
                            .GetAwaiter().GetResult();
                    }
                    else
                    {
                        // Pre-agent default: send peeked card to bottom.
                        decision = new ScryAction.ScryDecision(
                            ToBottom: peeked.ToList(),
                            TopOrder: Array.Empty<ICard>());
                    }
                    ScryAction.Apply(controller, peeked.Count, decision);
                }
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
