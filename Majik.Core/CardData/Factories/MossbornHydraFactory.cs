using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mossborn Hydra (Zendikar Rising Commander, {2}{G}).
///
/// Creature — Elemental Hydra 0/0. Oracle text (verified against Scryfall):
///   "Trample (This creature can deal excess combat damage to the player or
///    planeswalker it's attacking.)
///    This creature enters with a +1/+1 counter on it.
///    Landfall — Whenever a land you control enters, double the number of
///    +1/+1 counters on this creature."
///
/// ## Shape source
/// Card identity (name, {2}{G}, 0/0, Creature — Elemental Hydra, green) is
/// loaded from <c>Majik.Core/CardData/Cards/mossborn-hydra.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> — same data-driven identity pattern as
/// <see cref="ScuteSwarmFactory"/> / <see cref="BristlyBillSpineSowerFactory"/>.
/// The Trample keyword marker and the landfall counter-doubling trigger are
/// wired in code below; the JSON ability schema does not yet express keyword
/// markers or landfall counter-doubling.
///
/// ## Implemented (v1)
/// - 0/0 Creature — Elemental Hydra at {2}{G}, green (colour from the {G} pip
///   per CR 202.2c), owner / controller stamped. Printed 0/0; with its
///   mandatory ETB +1/+1 counter it is a 1/1 on the battlefield.
/// - <b>Trample (CR 702.19)</b> — attached as a <see cref="KeywordAbility"/>
///   marker, same shape as <see cref="AvatarOfTheResoluteFactory"/>. CombatDamage
///   consumes the marker for excess combat-damage assignment.
/// - <b>Enters with a +1/+1 counter (CR 614.1d / CR 122.1g)</b> — NOT wired by
///   this factory. Same posture as <see cref="GoldveinHydraFactory"/>: the
///   generic <see cref="EntersWithCountersBinder"/> matches the unconditional
///   "enters with a +1/+1 counter on it" clause and registers the
///   <see cref="EntersWithCountersReplacement"/> on the production
///   <see cref="DeckCardBuilder"/> route (Approach B → OverlayAdditiveBinders),
///   so the Hydra enters WITH one +1/+1 counter (Hardened Scales / Doubling
///   Season compose on that channel, CR 614). The factory deliberately does NOT
///   <c>MarkSelfManagesEntersWithCounters()</c> — setting that flag suppresses
///   the binder, the one mechanism the prod route runs, yielding ZERO counters
///   in real play (the bug Hangarback / Walking Ballista document). The
///   single-arg <see cref="Create(Player)"/> overload that
///   <see cref="NamedCardFactory"/> dispatches to therefore registers no ETB
///   replacement of its own — it would double-stack with the binder.
/// - <b>Landfall — double the +1/+1 counters on this creature (CR 603.1 /
///   603.6a / CR 702.142 / CR 121.4)</b>: "Whenever a land you control enters,
///   double the number of +1/+1 counters on this creature." Fires on a
///   <see cref="Majik.Core.Events.CardMovedEvent"/> filtered to "a land
///   entering the battlefield under the controller's control" via the shared
///   <see cref="Triggers.OnLandEntersUnderControl"/> predicate (same plumbing as
///   <see cref="ScuteSwarmFactory"/> / <see cref="BristlyBillSpineSowerFactory"/>).
///   No <see cref="TargetRequest"/> — the doubling names no target. On resolution
///   the current <see cref="CounterType.PlusOnePlusOne"/> count N on THIS
///   creature is read and N more are added — CR 121.4: "double" means add a
///   number of counters equal to the number already there, so N → 2N. A Hydra
///   with zero +1/+1 counters is unaffected (double of 0 is 0). The count is
///   read at RESOLUTION (CR 603.4) off the live card so any counters added /
///   removed between trigger and resolution are reflected.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Trample marker + landfall
///   trigger attached structurally; the trigger is not registered with any
///   <see cref="TriggerManager"/>, and no ETB-counter replacement is wired (the
///   binder owns it in prod). This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?)"/> — fully wired; the landfall
///   trigger registers with <paramref name="triggers"/> so a land ETB under the
///   controller's control automatically queues the doubling (CR 603.2).
/// </summary>
[CardName("Mossborn Hydra")]
public static class MossbornHydraFactory
{
    public const string CardName = "Mossborn Hydra";
    public const string Slug = "mossborn-hydra";

    /// <summary>Intrinsic keyword — CR 702.19 Trample.</summary>
    public const string TrampleKeyword = "Trample";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Mossborn Hydra with no live <see cref="TriggerManager"/>
    /// wiring. The Trample marker + the landfall counter-doubling trigger are
    /// attached for shape inspection; the trigger is not registered with a bus,
    /// and no ETB-counter replacement is wired (the
    /// <see cref="EntersWithCountersBinder"/> owns that on the prod route).
    /// Suitable for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Mossborn Hydra. When <paramref name="triggers"/> is supplied
    /// the landfall trigger is registered so a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> for a land entering under
    /// the controller's control automatically queues the doubling ability.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Elemental + Hydra subtypes, {2}{G}, 0/0, green). The JSON carries no
        // abilities — Trample + the landfall doubling are layered on below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.19 — Trample. CombatDamage reads this marker to assign excess
        // combat damage to the defending player / planeswalker.
        card.AddAbility(new KeywordAbility(TrampleKeyword, card, owner));

        // ----------------------------------------------------------------
        // Landfall — CR 603.1 / 603.6a / CR 702.142 / CR 121.4.
        //   "Whenever a land you control enters, double the number of +1/+1
        //    counters on this creature."
        // Predicate shared with Scute Swarm / Bristly Bill / Steppe Lynx.
        // No target: the doubling names no target. CR 121.4 — "double the
        // number of counters" adds a number of +1/+1 counters equal to the
        // count already present (N → 2N). The count is read at RESOLUTION
        // (CR 603.4) off this creature's live counter bag; a Hydra with zero
        // +1/+1 counters is unaffected.
        // ----------------------------------------------------------------
        var doublingEffect = new Effect(
            $"{CardName}: landfall — double the number of +1/+1 counters on this creature",
            () =>
            {
                var current = card.Counters.Count(CounterType.PlusOnePlusOne);
                if (current > 0)
                    card.Counters.Add(CounterType.PlusOnePlusOne, current);
            });

        var landfallTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLandEntersUnderControl(owner),
            effects: new IEffect[] { doublingEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(landfallTrigger);
        triggers?.RegisterTriggeredAbility(landfallTrigger);

        return card;
    }
}
