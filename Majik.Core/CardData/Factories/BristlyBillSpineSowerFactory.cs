using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bristly Bill, Spine Sower (Bloomburrow, {1}{G}).
///
/// Legendary Creature — Plant Druid 2/2. Oracle text (verified against
/// Scryfall):
///   "Landfall — Whenever a land you control enters, put a +1/+1 counter on
///    target creature.
///    {3}{G}{G}: Double the number of +1/+1 counters on each creature you
///    control."
///
/// The green landfall sibling of <see cref="TirelessTrackerFactory"/> (the
/// landfall trigger) crossed with <see cref="DoublingSeasonFactory"/>'s
/// counter math (the activated "double the counters" ability). Base shape
/// (name, Legendary Creature, Plant + Druid subtypes, {1}{G}, 2/2) is
/// materialised from the embedded JSON definition
/// (<c>bristly-bill-spine-sower.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the two abilities are layered
/// on top (the JSON <c>AbilityDefinition</c> schema does not yet express
/// landfall triggers or counter-doubling, same posture as the other
/// JSON-backed cards whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
/// - 2/2 Legendary Creature — Plant Druid, mana cost {1}{G}, owner /
///   controller wired.
/// - <b>Landfall triggered ability</b> (CR 603.1 / 603.6a / CR 702.142):
///   "Whenever a land you control enters, put a +1/+1 counter on target
///   creature." Fires on a <see cref="Majik.Core.Events.CardMovedEvent"/>
///   filtered to "a land entering the battlefield under the controller's
///   control" via the shared
///   <see cref="Triggers.OnLandEntersUnderControl"/> predicate. Carries a
///   1..1 "target creature" <see cref="TargetRequest"/> (any creature — the
///   oracle does not restrict to creatures you control). On resolution the
///   chosen target is rechecked (CR 608.2b: still on the battlefield, still
///   a creature) and one <see cref="CounterType.PlusOnePlusOne"/> counter is
///   placed via <see cref="CounterCollection.Add"/>. Same shape as Heliod,
///   Sun-Crowned's lifegain-counter trigger.
/// - <b>Activated ability {3}{G}{G}: double the +1/+1 counters on each
///   creature you control</b> (CR 602 / CR 121). On resolution, for every
///   creature the controller controls, the current
///   <see cref="CounterType.PlusOnePlusOne"/> count is read and that many
///   more are added — net effect is doubling (CR 121.4 — "double" means add
///   a number of counters equal to the number already there). A creature
///   with N +1/+1 counters ends with 2N. Creatures with zero +1/+1 counters
///   are unaffected. Reads the live battlefield at resolve so creatures that
///   left/entered between activation and resolution are handled correctly.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven target prompt</b>: the landfall trigger honours a
///   pre-set <see cref="ITriggeredAbility.ChosenTargets"/>; the factory does
///   NOT wire an <see cref="Majik.Core.Players.Agents.IPlayerAgent"/>
///   prompt. Tests call
///   <see cref="TriggeredAbility.SetChosenTargets"/> directly (same posture
///   as Heliod, Sun-Crowned).
/// - <b>Trigger registration</b>: the shape-only <see cref="Create(Player)"/>
///   path attaches the trigger for inspection but does not register it with
///   a bus. Use the <see cref="Create(Player, TriggerManager)"/> overload
///   for live firing.
/// - <b>Doubling-replacement interaction</b>: Bristly Bill's doubling reads
///   counts and calls <see cref="CounterCollection.Add"/> directly (it is a
///   one-shot resolution effect, not a CR 614 replacement), so it does not
///   route through <c>CountersService.Add</c>. Doubling Season / Hardened
///   Scales would normally apply to the "put N more counters" event; that
///   replacement plumbing for this card is deferred — same posture as the
///   direct <see cref="CounterCollection.Add"/> calls in Heliod /
///   Tireless Tracker.
/// </summary>
[CardName("Bristly Bill, Spine Sower")]
public static class BristlyBillSpineSowerFactory
{
    public const string CardName = "Bristly Bill, Spine Sower";
    public const string Slug = "bristly-bill-spine-sower";
    public const string DoubleCountersCost = "{3}{G}{G}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Bristly Bill with no live <see cref="TriggerManager"/>
    /// wiring. The landfall trigger + the counter-doubling activated ability
    /// are attached for shape inspection; the landfall trigger is not
    /// registered with a bus. Suitable for shape / dispatcher tests. This is
    /// the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Bristly Bill, Spine Sower. When <paramref name="triggers"/>
    /// is supplied the landfall trigger is registered so a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> for a land entering
    /// under the controller's control automatically queues the ability.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // Creature, Plant + Druid subtypes, {1}{G}, 2/2). The JSON carries no
        // abilities — landfall + counter-doubling are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        AttachLandfallCounterTrigger(card, owner, triggers);
        AttachDoubleCountersAbility(card, owner);

        return card;
    }

    /// <summary>
    /// Landfall — CR 603.1 / 603.6a / CR 702.142.
    ///   "Whenever a land you control enters, put a +1/+1 counter on target
    ///    creature."
    /// The predicate is the shared
    /// <see cref="Triggers.OnLandEntersUnderControl"/>. The target is ANY
    /// creature (the oracle does not say "you control"). On resolve the
    /// chosen creature is rechecked (CR 608.2b) then gains one +1/+1 counter.
    /// </summary>
    private static void AttachLandfallCounterTrigger(
        Creature card,
        Player owner,
        TriggerManager? triggers)
    {
        TriggeredAbility? landfallTrigger = null;

        var counterEffect = new Effect(
            $"{CardName}: landfall — put a +1/+1 counter on target creature",
            () =>
            {
                if (landfallTrigger == null) return;
                var chosen = landfallTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];
                if (raw is not Creature target) return;
                if (target.Zone != ZoneType.Battlefield) return; // CR 608.2b

                target.Counters.Add(CounterType.PlusOnePlusOne, 1);
            });

        landfallTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLandEntersUnderControl(owner),
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff),
            });

        card.AddAbility(landfallTrigger);
        triggers?.RegisterTriggeredAbility(landfallTrigger);
    }

    /// <summary>
    /// "{3}{G}{G}: Double the number of +1/+1 counters on each creature you
    /// control." (CR 602 activated ability; CR 121.4 — "double" adds a number
    /// of counters equal to the number already present.)
    ///
    /// On resolve, snapshot the controller's creatures and for each one read
    /// its current +1/+1 count N and add N more (so the total becomes 2N).
    /// Reading the count up-front and adding to a snapshot keeps the doubling
    /// deterministic regardless of enumeration order. Creatures with no
    /// +1/+1 counters are unaffected.
    /// </summary>
    private static void AttachDoubleCountersAbility(Creature card, Player owner)
    {
        var doubleEffect = new Effect(
            $"{CardName}: double the +1/+1 counters on each creature you control",
            () =>
            {
                var controller = card.Controller ?? owner;

                // Snapshot first (CR 608.2 — resolve against the live
                // battlefield, but read counts before mutating so additions
                // don't compound on themselves).
                var creatures = controller.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .Select(c => (creature: c, count: c.Counters.Count(CounterType.PlusOnePlusOne)))
                    .ToList();

                foreach (var (creature, count) in creatures)
                {
                    if (count > 0)
                        creature.Counters.Add(CounterType.PlusOnePlusOne, count);
                }
            });

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(DoubleCountersCost) },
            effects: new IEffect[] { doubleEffect });

        card.AddAbility(ability);
    }
}
