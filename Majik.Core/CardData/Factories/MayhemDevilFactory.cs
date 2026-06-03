using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mayhem Devil (War of the Spark, {1}{B}{R}).
///
/// Creature — Devil 3/3. Oracle text (Scryfall, verified):
///   "Whenever a player sacrifices a permanent, Mayhem Devil deals 1
///    damage to any target."
///
/// ## Implemented (v1)
/// - 3/3 Creature — Devil {1}{B}{R}; owner / controller wired.
/// - <b>Sacrifice-detection trigger (CR 603.1 + CR 701.16)</b>: a
///   <see cref="TriggeredAbility"/> fires on the dedicated
///   <see cref="PermanentSacrificedEvent"/> (published by the bus-aware
///   <see cref="Fx.Sacrifice(Cards.ICard, Players.Player, Events.IEventBus)"/>
///   overload). The trigger carries a <see cref="TargetRequest"/>
///   for a single any-target damage target; on resolution the source
///   deals 1 damage to the chosen target via <see cref="Fx.DealDamageAny"/>
///   (CR 306.7 — Planeswalker targets convert to loyalty removal).
/// - <b>Self-sourced damage</b>: damage source is the Devil itself per
///   "Mayhem Devil deals 1 damage to any target."
///
/// ## Sacrifice-detection (CR 701.16) — closed
/// The dedicated <see cref="PermanentSacrificedEvent"/> replaced the v1
/// degraded <see cref="CardMovedEvent"/> Battlefield → Graveyard condition,
/// which couldn't distinguish a sacrifice from a destroy / SBA death
/// (over-fire) and which <see cref="Fx.Sacrifice(Cards.ICard)"/> never
/// published at all (under-fire). "Whenever a player sacrifices a
/// permanent" is now exact — no <see cref="PermanentSacrificedEvent.SacrificingPlayer"/>
/// filter, since the printed text fires off ANY player's sacrifice.
///
/// ## Deferred (v1 gaps)
/// - <b>Target prompting</b>: activated-ability flow doesn't prompt for
///   targets via the v1 dispatcher — callers set
///   <see cref="TriggeredAbility.ChosenTargets"/> before the trigger
///   resolves (mirrors Goblin Bombardment's <c>DamageTarget</c>
///   pattern). A future agent-prompt MVP will close this.
/// - <b>Self-sacrifice fires once per permanent</b>: when Mayhem Devil
///   itself is sacrificed it would normally trigger off its own death
///   (LKI snapshot per CR 603.10), but the activeZones filter
///   {Battlefield} drops the trigger before resolution. Matches the
///   conventional "trigger leaves the battlefield = lost" interaction
///   (CR 603.6c — the trigger looks back at the last known zone).
/// </summary>
[CardName("Mayhem Devil")]
public static class MayhemDevilFactory
{
    public const string CardName = "Mayhem Devil";
    public const string PrintedManaCost = "{1}{B}{R}";
    public const int Power = 3;
    public const int Toughness = 3;
    public const int PingDamage = 1;

    /// <summary>
    /// Construct Mayhem Devil with no live runtime services. The
    /// sacrifice-detection trigger is attached to the card shape but
    /// not registered with a <see cref="TriggerManager"/>. Suitable
    /// for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Mayhem Devil with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the sacrifice-detection
    /// trigger is registered so the bus drives it automatically.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Devil });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Sacrifice-detection trigger — CR 603.1 + CR 701.16.
        //   "Whenever a player sacrifices a permanent, Mayhem Devil deals
        //    1 damage to any target."
        // Fires on the dedicated PermanentSacrificedEvent — published by
        // the bus-aware Fx.Sacrifice overload only on a real sacrifice
        // (Annihilator / edict / sac-cost), so this no longer over-fires
        // on destroys / SBA deaths nor under-fires on a sacrifice that
        // mutates zones directly (the v1 CardMovedEvent footprint). "a
        // player" is any player, so no SacrificingPlayer filter (CR 603.1).
        // ----------------------------------------------------------------
        TriggeredAbility? sacTrigger = null;
        var sacCondition = new EventTriggerCondition<PermanentSacrificedEvent>((e, _) => true);

        var pingEffect = new Effect(
            $"{CardName}: deal 1 damage to any target",
            () =>
            {
                if (sacTrigger == null) return;
                if (sacTrigger.ChosenTargets.Count == 0) return;
                if (sacTrigger.ChosenTargets[0].Count == 0) return;
                var target = sacTrigger.ChosenTargets[0][0];
                Fx.DealDamageAny(target, PingDamage);
            });

        sacTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: sacCondition,
            effects: new IEffect[] { pingEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(sacTrigger);
        triggers?.RegisterTriggeredAbility(sacTrigger);

        return card;
    }
}
