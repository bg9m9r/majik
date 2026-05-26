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
///   <see cref="TriggeredAbility"/> fires on a permanent moving
///   Battlefield → Graveyard. The trigger carries a <see cref="TargetRequest"/>
///   for a single any-target damage target; on resolution the source
///   deals 1 damage to the chosen target via <see cref="Fx.DealDamageAny"/>
///   (CR 306.7 — Planeswalker targets convert to loyalty removal).
/// - <b>Self-sourced damage</b>: damage source is the Devil itself per
///   "Mayhem Devil deals 1 damage to any target."
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice-only firing semantics</b>: the engine does not yet
///   distinguish "sacrificed" from "destroyed" / "died from SBA" /
///   "milled from the battlefield" / "exiled" at the
///   <see cref="CardMovedEvent"/> level — <see cref="CardMovedEvent"/>
///   carries only zones, not a <see cref="ZoneMoveReason"/>. The v1
///   condition fires on ANY Battlefield → Graveyard move involving a
///   permanent. This produces two real-world deviations:
///   <list type="bullet">
///     <item>OVER-fire: Mayhem Devil triggers on permanents being
///       destroyed or dying from SBA-driven death, which it should not
///       per the printed text.</item>
///     <item>UNDER-fire: <see cref="Fx.Sacrifice"/> currently routes
///       through <see cref="Majik.Core.CardData.OracleSpellBinder.MoveToGraveyard"/>
///       which mutates zones directly WITHOUT publishing a
///       <see cref="CardMovedEvent"/>. Sacrifice payments inside
///       activated-ability cost closures (Fanatical Firebrand,
///       Insolent Neonate, Goblin Bombardment) hit this path and the
///       trigger is silently missed.</item>
///   </list>
///   The clean fix is a dedicated <c>PermanentSacrificedEvent</c>
///   published by <see cref="Fx.Sacrifice"/> + every additional-cost
///   sacrifice closure; the trigger condition would then move to
///   <c>EventTriggerCondition&lt;PermanentSacrificedEvent&gt;</c> with
///   no behavioural change to this factory beyond swapping the
///   condition type. Same shape as the planned card-cast-event
///   refactor.
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
        // v1 condition: any permanent moving Battlefield → Graveyard
        // (see class xmldoc gap note for the over/under-fire footprint).
        // The card must still be a permanent at LKI to filter out raw
        // spell resolutions (Instant / Sorcery hitting the graveyard via
        // a different zone path).
        // ----------------------------------------------------------------
        TriggeredAbility? sacTrigger = null;
        var sacCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.FromZone != ZoneType.Battlefield) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            // Only permanent types — battlefield-zone restriction (CR 110.1)
            // already covers this in production but the explicit filter
            // is cheap insurance against synthetic test moves.
            return e.Card.HasType(CardType.Creature)
                || e.Card.HasType(CardType.Artifact)
                || e.Card.HasType(CardType.Enchantment)
                || e.Card.HasType(CardType.Land)
                || e.Card.HasType(CardType.Planeswalker);
        });

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
