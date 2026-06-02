using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Craterhoof Behemoth (Avacyn Restored,
/// {5}{G}{G}{G}).
///
/// Creature — Beast 5/5. Oracle text (verified against Scryfall):
///   "Haste
///    When this creature enters, creatures you control gain trample and get
///    +X/+X until end of turn, where X is the number of creatures you
///    control."
///
/// ## Implementation
///
/// Base shape (name, Creature, Beast subtype, {5}{G}{G}{G}, 5/5) is
/// materialised from the embedded JSON definition
/// (<c>craterhoof-behemoth.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The intrinsic Haste marker and
/// the ETB triggered ability are layered on top here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express keyword markers or a
/// dynamic-X ETB pump (same posture as <see cref="BallLightningFactory"/>,
/// which layers Trample/Haste markers + a step-begin trigger over its JSON
/// shape).
///
/// ## Implemented (v1)
/// - 5/5 Creature — Beast at printed cost {5}{G}{G}{G}.
/// - <b>Haste</b> (CR 702.10) as a <see cref="KeywordAbility"/> marker.
/// - <b>ETB triggered ability (CR 603.6a)</b>: fires on Stack → Battlefield
///   (<see cref="CardMovedEvent"/>). On resolution (CR 608.2 — effects
///   resolve against current game state) the body:
///   1. Snapshots the number of creatures the controller controls into X.
///      Craterhoof itself is on the battlefield when its own ETB resolves,
///      so it is included in the count (e.g. with two other creatures, X =
///      3). X is a fixed value once computed (CR 608.2 — "where X is the
///      number of creatures you control" is locked at resolution, not a
///      continuous recount).
///   2. For each creature the controller controls, registers a
///      <see cref="GrantKeywordUntilEndOfTurnEffect"/>("Trample", CR 702.19,
///      Layer 6 per CR 613.1c) and a
///      <see cref="PumpUntilEndOfTurnEffect"/>(+X/+X, Layer 7c). Both expire
///      at cleanup (CR 514.2). Creatures without a wired
///      <see cref="ContinuousEffectsService"/> no-op cleanly (shape-only
///      guard, mirrors <see cref="ViolentOutburstFactory.ApplyPumpAndHaste"/>).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Keyword marker + ETB trigger
///   attached structurally; the trigger is not registered with any
///   <see cref="TriggerManager"/>. This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?)"/> — fully wired; the ETB
///   trigger registers with <paramref name="triggers"/>.
/// </summary>
[CardName("Craterhoof Behemoth")]
public static class CraterhoofBehemothFactory
{
    public const string CardName = "Craterhoof Behemoth";
    public const string Slug = "craterhoof-behemoth";

    /// <summary>Intrinsic keyword — CR 702.10 Haste.</summary>
    public const string HasteKeyword = "Haste";

    /// <summary>Granted keyword — CR 702.19 Trample.</summary>
    public const string GrantedTrample = "Trample";

    /// <summary>
    /// Single-arg dispatcher path. Attaches the Haste marker + ETB trigger
    /// structurally so card shape is correct; no <see cref="TriggerManager"/>
    /// wiring.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Fully-wired construction. <paramref name="triggers"/> registers the ETB
    /// triggered ability so a <see cref="CardMovedEvent"/> (Stack →
    /// Battlefield) on this card fires the trample + dynamic +X/+X rider
    /// automatically.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Beast
        // subtype, {5}{G}{G}{G}, 5/5). The JSON carries no abilities — the
        // Haste marker + ETB trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.10 — Haste.
        card.AddAbility(new KeywordAbility(HasteKeyword, card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, creatures you control gain trample
        //    and get +X/+X until end of turn, where X is the number of
        //    creatures you control."
        // Fires on Stack → Battlefield for this card.
        // ----------------------------------------------------------------
        var condition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var effect = new Effect(
            $"{CardName}: creatures you control gain trample and get +X/+X until end of turn (X = creatures you control)",
            () => ApplyTrampleAndPump(card.Controller ?? owner));

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
    /// Apply Craterhoof Behemoth's ETB rider to every creature
    /// <paramref name="controller"/> controls at the moment this effect runs.
    /// CR 608.2 — X is the number of creatures the controller controls,
    /// snapshotted at resolution time (including Craterhoof itself, which is
    /// already on the battlefield when its own ETB resolves). The same fixed X
    /// is applied to each creature: Trample grant (CR 702.19, Layer 6) +
    /// +X/+X pump (Layer 7c). Both until end of turn (CR 514.2). Creatures
    /// without a wired <see cref="ContinuousEffectsService"/> no-op cleanly.
    /// </summary>
    public static void ApplyTrampleAndPump(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        // Snapshot to a list so any same-step side effects don't disturb the
        // enumeration (mirrors ViolentOutburstFactory.ApplyPumpAndHaste).
        var creatures = controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .ToList();

        // CR 608.2 — X is locked at resolution: the creature count at the
        // moment the ETB resolves. Craterhoof is included (it's already on the
        // battlefield), so X >= 1.
        var x = creatures.Count;

        foreach (var creature in creatures)
        {
            // Shape-only safety — without a live ContinuousEffectsService the
            // grant/pump silently no-ops rather than NRE'ing.
            if (creature.ActiveEffects == null) continue;

            // CR 613.1c Layer 6 — Trample grant (CR 702.19).
            creature.ActiveEffects.Register(
                new GrantKeywordUntilEndOfTurnEffect(creature, GrantedTrample));

            // CR 613.1c Layer 7c — +X/+X pump.
            creature.ActiveEffects.Register(
                new PumpUntilEndOfTurnEffect(creature, x, x));
        }
    }
}
