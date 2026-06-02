using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Keldon Marauders (Time Spiral + Modern Horizons
/// reprint, {1}{R}). Creature — Human Warrior 3/1. Oracle text (verified
/// against the printed card):
///   "Vanishing 2 (This creature enters with two time counters on it. At
///    the beginning of your upkeep, remove a time counter from it. When the
///    last is removed, sacrifice it.)
///    When this creature enters or leaves the battlefield, it deals 1
///    damage to target player or planeswalker."
///
/// The card's base shape (name, Creature, Human / Warrior subtypes, {1}{R},
/// 3/1) is materialised from the embedded JSON definition
/// (<c>keldon-marauders.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The Vanishing time-counter
/// loop and the enters/leaves damage triggers are layered on here — the
/// JSON <c>AbilityDefinition</c> schema doesn't yet express Vanishing or
/// targeted damage triggers (same posture as
/// <see cref="ViashinoPyromancerFactory"/> / <see cref="PiaAndKiranNalaarFactory"/>).
///
/// ## Implemented (v1)
///
/// - 3/1 Human Warrior at printed cost {1}{R}, owner / controller wired.
/// - <b>Vanishing 2 (CR 702.63)</b>:
///   <list type="bullet">
///     <item><b>Enters with two time counters (CR 702.63b / CR 122.1)</b>:
///       <see cref="CounterType.Time"/> x2 added to the card on
///       construction. (The "enters with" is a replacement effect in the
///       comp rules; the established factory posture — Aether Vial /
///       Chalice ETB — adds the on-enter counters directly, which is
///       observationally identical for a permanent that always enters
///       from the stack with these intrinsic counters.)</item>
///     <item><b>Upkeep tick (CR 702.63c)</b>: "At the beginning of your
///       upkeep, remove a time counter from it." Wired via
///       <see cref="Triggers.OnStepBegin"/> filtered to
///       <see cref="Majik.Core.StateMachine.PhaseStateType.Upkeep"/> + the
///       controller. Removes one <see cref="CounterType.Time"/> counter.</item>
///     <item><b>Sacrifice on last removed (CR 702.63d / CR 701.16)</b>:
///       "When the last is removed, sacrifice it." After the upkeep tick,
///       when no time counters remain the permanent is moved
///       battlefield → owner's graveyard (same inline-sacrifice posture as
///       <see cref="GemstoneMineFactory"/> — the engine's generic
///       sacrifice path is a no-op stub). That graveyard move fires the
///       leaves-the-battlefield damage trigger below (CR 603.10a — the
///       leaves trigger looks back in time at the permanent's
///       last-known battlefield state).</item>
///   </list>
/// - <b>Enters-the-battlefield damage trigger (CR 603.6a)</b>: "When this
///   creature enters ..., it deals 1 damage to target player or
///   planeswalker." Wired via <see cref="Triggers.OnEnterBattlefieldSelf"/>
///   with a single 1..1 "target player or planeswalker"
///   <see cref="TargetRequest"/>. Same shape as
///   <see cref="ViashinoPyromancerFactory"/>'s ETB damage.
/// - <b>Leaves-the-battlefield damage trigger (CR 603.6d / CR 603.10a)</b>:
///   "When this creature ... leaves the battlefield, it deals 1 damage to
///   target player or planeswalker." Wired as an
///   <see cref="EventTriggerCondition{CardMovedEvent}"/> matching any
///   <see cref="CardMovedEvent"/> whose <see cref="CardMovedEvent.FromZone"/>
///   is the battlefield (every exit path — death, sacrifice, bounce,
///   exile — counts as "leaves the battlefield", CR 603.6d). Active zones
///   include the graveyard / exile / hand so the trigger still matches
///   after <see cref="ZoneService"/> has stamped the card's new zone before
///   publishing the event (same look-back posture as Matter Reshaper's
///   dies trigger).
/// - Both damage halves surface every live <see cref="Player"/> plus every
///   <see cref="Planeswalker"/> on any battlefield as candidates
///   (CR 115.1 — creatures excluded) and route 1 damage through
///   <see cref="Fx.DealDamageAny(object, int)"/> so a planeswalker target
///   converts to loyalty removal (CR 306.8). A resolve-time
///   Player/Planeswalker gate (CR 608.2b) further validates.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Live TriggerManager wiring</b>: this dispatcher-path factory
///   attaches all triggers structurally; the upkeep / enters / leaves
///   triggers are not registered with a <see cref="TriggerManager"/>. Tests
///   fire them by invoking <see cref="TriggeredAbility.Resolve"/> /
///   <see cref="PerformUpkeepTick"/> directly. Same posture as
///   <see cref="ViashinoPyromancerFactory"/> / <see cref="TheOneRingFactory.Create(Player)"/>.
/// - <b>Damage source threading</b>: <see cref="Fx.DealDamageAny"/> doesn't
///   thread the Marauders through as the damage source, so a future
///   lifelink / "whenever a source you control deals damage" grant won't
///   observe it. Same primitive-level posture as Viashino Pyromancer.
/// </summary>
[CardName("Keldon Marauders")]
public static class KeldonMaraudersFactory
{
    public const string CardName = "Keldon Marauders";
    public const string Slug = "keldon-marauders";
    public const int Power = 3;
    public const int Toughness = 1;

    /// <summary>CR 702.63a — Vanishing N: enters with N time counters.</summary>
    public const int VanishingCount = 2;

    /// <summary>CR 119 — fixed 1 damage on each enters / leaves trigger.</summary>
    public const int DamageAmount = 1;

    /// <summary>
    /// Construct Keldon Marauders owned and controlled by
    /// <paramref name="owner"/>. The base shape comes from the embedded JSON
    /// definition; the Vanishing 2 time-counter loop and the enters / leaves
    /// "deal 1 to target player or planeswalker" triggers are attached here.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Vanishing 2 — enters with two time counters (CR 702.63b /
        // CR 122.1). The on-enter counters are intrinsic to the permanent;
        // adding them on construction matches the Aether Vial / Chalice ETB
        // posture and is observationally identical for a card that always
        // enters from the stack with these counters.
        // ----------------------------------------------------------------
        card.Counters.Add(CounterType.Time, VanishingCount);

        // ----------------------------------------------------------------
        // Vanishing upkeep tick (CR 702.63c): "At the beginning of your
        // upkeep, remove a time counter from it." When the last counter is
        // removed, sacrifice it (CR 702.63d / CR 701.16).
        // ----------------------------------------------------------------
        var upkeepEffect = new Effect(
            $"{CardName}: Vanishing — remove a time counter; sacrifice when last removed",
            () => PerformUpkeepTick(card, owner, zones: null));

        var upkeepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(
                owner, Majik.Core.StateMachine.PhaseStateType.Upkeep),
            effects: new IEffect[] { upkeepEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(upkeepTrigger);

        // ----------------------------------------------------------------
        // Enters-the-battlefield damage trigger (CR 603.6a).
        //   "When this creature enters ..., it deals 1 damage to target
        //    player or planeswalker."
        // ----------------------------------------------------------------
        var etbTrigger = BuildDamageTrigger(
            card, owner, Triggers.OnEnterBattlefieldSelf(card),
            // ETB trigger only matters while the card is on the
            // battlefield (CR 603.6c — it must still be there at the time
            // the trigger would be put on the stack).
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Leaves-the-battlefield damage trigger (CR 603.6d / CR 603.10a).
        //   "When this creature ... leaves the battlefield, it deals 1
        //    damage to target player or planeswalker."
        // Matches every exit from the battlefield (death, sacrifice,
        // bounce, exile). Active zones include the destination zones so the
        // trigger still matches after ZoneService stamps the card's new
        // zone before publishing the CardMovedEvent (Matter Reshaper
        // look-back posture).
        // ----------------------------------------------------------------
        var leavesTrigger = BuildDamageTrigger(
            card, owner,
            new EventTriggerCondition<CardMovedEvent>(
                (e, _) => ReferenceEquals(e.Card, card)
                          && e.FromZone == ZoneType.Battlefield),
            activeZones: new[]
            {
                ZoneType.Battlefield,
                ZoneType.Graveyard,
                ZoneType.Exile,
                ZoneType.Hand,
            });

        card.AddAbility(leavesTrigger);

        return card;
    }

    /// <summary>
    /// Build a "deals 1 damage to target player or planeswalker" triggered
    /// ability driven by <paramref name="condition"/>. Shared by the enters
    /// + leaves halves (identical resolve body, different trigger
    /// condition). The 1..1 <see cref="TargetRequest"/> surfaces every live
    /// player plus every planeswalker on any battlefield (CR 115.1 —
    /// creatures excluded); resolution gates to Player / Planeswalker
    /// (CR 608.2b) and routes through <see cref="Fx.DealDamageAny"/>
    /// (planeswalker → loyalty removal, CR 306.8).
    /// </summary>
    private static TriggeredAbility BuildDamageTrigger(
        Creature card,
        Player owner,
        ITriggerCondition condition,
        ZoneType[] activeZones)
    {
        TriggeredAbility? trigger = null;
        var effect = new Effect(
            $"{CardName}: deal {DamageAmount} damage to target player or planeswalker",
            () =>
            {
                if (trigger == null) return;
                var chosen = trigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var target = chosen[0][0];

                // CR 608.2b — only Player and Planeswalker are legal; no-op
                // for any other resolved type. Fx.DealDamageAny routes
                // planeswalker damage as loyalty removal (CR 306.8).
                if (target is Player || target is Planeswalker)
                {
                    Fx.DealDamageAny(target, DamageAmount);
                }
            });

        trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { effect },
            interveningIf: null,
            activeZones: activeZones,
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player or planeswalker",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Burn,
                    // CR 115.1 — players and planeswalkers only.
                    CandidateGatherer: ctx =>
                    {
                        var candidates = new List<object>(ctx.AllPlayers);
                        candidates.AddRange(ctx.AllPlayers
                            .SelectMany(p => p.Zones.Battlefield.GetCards())
                            .OfType<Planeswalker>());
                        return candidates;
                    }),
            });

        return trigger;
    }

    /// <summary>
    /// Perform one Vanishing upkeep tick (CR 702.63c): remove a single time
    /// counter. When the last counter is removed (none remain), sacrifice
    /// the permanent (CR 702.63d / CR 701.16) — move it from its
    /// controller's battlefield to its owner's graveyard. The sacrifice move
    /// is the inline zone-shuffle posture used by
    /// <see cref="GemstoneMineFactory"/>; when <paramref name="zones"/> is
    /// supplied the move routes through <see cref="ZoneService.MoveCard"/>
    /// so the resulting <see cref="CardMovedEvent"/> fires the
    /// leaves-the-battlefield damage trigger downstream.
    /// </summary>
    public static void PerformUpkeepTick(Creature card, Player owner, ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(owner);

        if (card.Zone != ZoneType.Battlefield) return;
        if (card.Counters.Count(CounterType.Time) == 0) return;

        // CR 702.63c — remove one time counter.
        card.Counters.Remove(CounterType.Time, 1);

        // CR 702.63d — when the last is removed, sacrifice it.
        if (card.Counters.Count(CounterType.Time) > 0) return;

        var controller = card.Controller ?? owner;
        var graveyardOwner = card.Owner ?? owner;

        if (zones != null)
        {
            // Full path: replacement bus fires, CardMovedEvent published
            // (drives the leaves-the-battlefield damage trigger).
            zones.MoveCard(card, ZoneType.Battlefield, ZoneType.Graveyard);
        }
        else
        {
            // Inline sacrifice (Gemstone Mine posture — generic sacrifice
            // path is a no-op stub).
            controller.Zones.Battlefield.RemoveCard(card);
            graveyardOwner.Zones.Graveyard.AddCard(card);
            card.SetZone(ZoneType.Graveyard);
        }
    }
}
