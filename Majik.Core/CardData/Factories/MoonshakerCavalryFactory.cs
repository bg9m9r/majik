using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Moonshaker Cavalry (Wilds of Eldraine,
/// {5}{W}{W}{W}).
///
/// Creature — Spirit Knight 6/6. Oracle text (verified against Scryfall):
///   "Flying
///    When this creature enters, creatures you control gain flying and get
///    +X/+X until end of turn, where X is the number of creatures you
///    control."
///
/// ## Implementation
///
/// Base shape (name, Creature, Spirit + Knight subtypes, {5}{W}{W}{W}, 6/6) is
/// materialised from the embedded JSON definition
/// (<c>moonshaker-cavalry.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The intrinsic Flying marker and
/// the ETB triggered ability are layered on top here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express keyword markers or a
/// dynamic-X ETB pump (same posture as
/// <see cref="CraterhoofBehemothFactory"/>, the structural analogue: ETB
/// team-wide pump where X = creature count + a granted keyword — Craterhoof
/// grants Trample + intrinsic Haste, Moonshaker grants Flying + intrinsic
/// Flying).
///
/// ## Implemented (v1)
/// - 6/6 Creature — Spirit Knight at printed cost {5}{W}{W}{W}.
/// - <b>Flying</b> (CR 702.9) as a <see cref="KeywordAbility"/> marker.
/// - <b>ETB triggered ability (CR 603.6a)</b>: fires on Stack → Battlefield
///   (<see cref="CardMovedEvent"/>). On resolution (CR 608.2 — effects
///   resolve against current game state) the body:
///   1. Snapshots the number of creatures the controller controls into X.
///      Moonshaker itself is on the battlefield when its own ETB resolves, so
///      it is included in the count (e.g. with two other creatures, X = 3). X
///      is a fixed value once computed (CR 608.2 — "where X is the number of
///      creatures you control" is locked at resolution, not a continuous
///      recount).
///   2. For each creature the controller controls, registers a
///      <see cref="GrantKeywordUntilEndOfTurnEffect"/>("Flying", CR 702.9,
///      Layer 6 per CR 613.1c) and a
///      <see cref="PumpUntilEndOfTurnEffect"/>(+X/+X, Layer 7c). Both expire
///      at cleanup (CR 514.2). Creatures without a wired
///      <see cref="ContinuousEffectsService"/> no-op cleanly (shape-only
///      guard, mirrors <see cref="CraterhoofBehemothFactory.ApplyTrampleAndPump"/>).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Keyword marker + ETB trigger
///   attached structurally; the trigger is not registered with any
///   <see cref="TriggerManager"/>. This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?)"/> — fully wired; the ETB
///   trigger registers with <paramref name="triggers"/>.
/// </summary>
[CardName("Moonshaker Cavalry")]
public static class MoonshakerCavalryFactory
{
    public const string CardName = "Moonshaker Cavalry";
    public const string Slug = "moonshaker-cavalry";

    /// <summary>Intrinsic + granted keyword — CR 702.9 Flying.</summary>
    public const string FlyingKeyword = "Flying";

    /// <summary>
    /// Single-arg dispatcher path. Attaches the Flying marker + ETB trigger
    /// structurally so card shape is correct; no <see cref="TriggerManager"/>
    /// wiring.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Fully-wired construction. <paramref name="triggers"/> registers the ETB
    /// triggered ability so a <see cref="CardMovedEvent"/> (Stack →
    /// Battlefield) on this card fires the flying + dynamic +X/+X rider
    /// automatically.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Spirit
        // + Knight subtypes, {5}{W}{W}{W}, 6/6). The JSON carries no abilities
        // — the Flying marker + ETB trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.9 — Flying.
        card.AddAbility(new KeywordAbility(FlyingKeyword, card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, creatures you control gain flying and
        //    get +X/+X until end of turn, where X is the number of creatures
        //    you control."
        // Fires on Stack → Battlefield for this card.
        // ----------------------------------------------------------------
        var condition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var effect = new Effect(
            $"{CardName}: creatures you control gain flying and get +X/+X until end of turn (X = creatures you control)",
            () => ApplyFlyingAndPump(card.Controller ?? owner));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// Apply Moonshaker Cavalry's ETB rider to every creature
    /// <paramref name="controller"/> controls at the moment this effect runs.
    /// CR 608.2 — X is the number of creatures the controller controls,
    /// snapshotted at resolution time (including Moonshaker itself, which is
    /// already on the battlefield when its own ETB resolves). The same fixed X
    /// is applied to each creature: Flying grant (CR 702.9, Layer 6) + +X/+X
    /// pump (Layer 7c). Both until end of turn (CR 514.2). Creatures without a
    /// wired <see cref="ContinuousEffectsService"/> no-op cleanly.
    /// </summary>
    public static void ApplyFlyingAndPump(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        // Snapshot to a list so any same-step side effects don't disturb the
        // enumeration (mirrors CraterhoofBehemothFactory.ApplyTrampleAndPump).
        var creatures = controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .ToList();

        // CR 608.2 — X is locked at resolution: the creature count at the
        // moment the ETB resolves. Moonshaker is included (it's already on the
        // battlefield), so X >= 1.
        var x = creatures.Count;

        foreach (var creature in creatures)
        {
            // Shape-only safety — without a live ContinuousEffectsService the
            // grant/pump silently no-ops rather than NRE'ing.
            if (creature.ActiveEffects == null) continue;

            // CR 613.1c Layer 6 — Flying grant (CR 702.9).
            creature.ActiveEffects.Register(
                new GrantKeywordUntilEndOfTurnEffect(creature, FlyingKeyword));

            // CR 613.1c Layer 7c — +X/+X pump.
            creature.ActiveEffects.Register(
                new PumpUntilEndOfTurnEffect(creature, x, x));
        }
    }
}
