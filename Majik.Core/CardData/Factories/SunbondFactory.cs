using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sunbond (Magic 2015, {3}{W}).
///
/// Enchantment — Aura. Oracle text (verified against Scryfall 2026-06-02):
///   "Enchant creature
///    Enchanted creature has \"Whenever you gain life, put that many
///    +1/+1 counters on this creature.\""
///
/// ## Shape source
/// Card identity (name, {3}{W}, Enchantment — Aura, white) is loaded from
/// <c>Majik.Core/CardData/Cards/sunbond.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The granted triggered ability is
/// attached in code below — the JSON ability schema does not express a
/// life-gain trigger that scales by "that many", so it is hand-rolled here
/// (same Aura-grants-a-trigger posture as <see cref="CuriosityFactory"/>).
///
/// ## Implemented (v1)
/// - Enchantment — Aura at {3}{W}; ETB-attach plumbing via the shared
///   <see cref="AuraSpellDefinitionBuilder"/> path
///   (<see cref="BuildSpellDefinition"/>; "Enchant creature" — CR 702.5b /
///   303.4c). On resolution the aura enters already attached to the chosen
///   creature (CR 303.4f).
/// - <b>Granted lifegain triggered ability (CR 603.1)</b>: the aura grants the
///   enchanted creature the ability "Whenever you gain life, put that many
///   +1/+1 counters on this creature." Per CR 603.3c the granted ability is
///   controlled by the enchanted creature's controller — so "you" reads the
///   enchanted creature's controller dynamically (via
///   <see cref="Permanent.AttachedTo"/>), and a control-change of the creature
///   redirects whose life gains matter without re-registration.
///   - <b>Trigger condition</b> — fires on a <see cref="LifeChangedEvent"/>
///     whose player is the enchanted creature's controller AND whose delta is
///     strictly positive (a life *gain*, not a loss — CR 119.3). Gated on the
///     aura being attached: while unattached the condition can never match.
///   - <b>"that many" (CR 603.7 — snapshot at trigger queueing)</b>: the gained
///     amount (<c>NewLife - PreviousLife</c>) is captured by an
///     <see cref="IEventBus"/> subscription into a closure-shared mutable slot
///     the moment the event fires — before the trigger queues / resolves. The
///     trigger Effect reads + clears the slot on resolution and places that
///     many <see cref="CounterType.PlusOnePlusOne"/> counters.
///   - <b>"this creature"</b> — the counters go on the enchanted creature
///     (<see cref="Permanent.AttachedTo"/>), rechecked at resolution
///     (CR 608.2 — still attached, still on the battlefield) before placement
///     via <see cref="CounterCollection.Add"/> (same counter-placement shape
///     as Heliod, Sun-Crowned).
///
/// ## Lifecycle
/// - The single-arg <see cref="Create(Player)"/> dispatcher overload attaches
///   the trigger for shape / condition inspection but wires no
///   <see cref="IEventBus"/>; without a bus the amount slot is never stamped,
///   so the placement clause no-ops on hand-executed Effects (same shape-only
///   posture as Vito). The <c>(owner, eventBus)</c> overload wires the
///   amount-snapshot subscription so a bus-fired life gain stamps the slot
///   before the trigger resolves.
///
/// ## Deferred (v1 gaps)
/// - <b>Shape-only path</b>: without an <see cref="IEventBus"/>, the slot is
///   never stamped so the counter clause no-ops — identical to Vito's
///   shape-only posture. The engine wire-up site should always pass a bus.
/// </summary>
[CardName("Sunbond")]
public static class SunbondFactory
{
    public const string CardName = "Sunbond";
    public const string Slug = "sunbond";
    public const string Cost = "{3}{W}";

    public static readonly IReadOnlyList<string> OracleText = new[]
    {
        "Enchant creature",
        "Enchanted creature has \"Whenever you gain life, put that many "
            + "+1/+1 counters on this creature.\"",
    };

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Sunbond with the granted lifegain trigger attached but no
    /// live <see cref="IEventBus"/> wiring. Suitable for shape / dispatcher /
    /// trigger-condition tests; the "that many" amount slot is never stamped
    /// so the counter clause no-ops on hand-executed Effects. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Enchantment Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Sunbond. When <paramref name="eventBus"/> is supplied, the
    /// aura subscribes to <see cref="LifeChangedEvent"/> so the "that many"
    /// amount slot is stamped (the controller's <c>NewLife - PreviousLife</c>
    /// delta) before the granted trigger resolves. The granted trigger is
    /// always attached to the aura's <see cref="Card.Abilities"/>.
    /// </summary>
    public static Enchantment Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // The "that many" amount snapshot (CR 603.7). Captured by the bus
        // subscription the moment a qualifying life gain fires; read + cleared
        // by the trigger Effect at resolution.
        var slot = new AmountSlot { Amount = 0 };

        // ----------------------------------------------------------------
        // "you" = the enchanted creature's controller (CR 603.3c). Read
        // dynamically through AttachedTo so a control-change of the creature
        // redirects whose life gains matter (and an unattached aura yields a
        // null controller → no life gain ever qualifies).
        // ----------------------------------------------------------------
        Player? EnchantedController() => card.AttachedTo?.Controller;

        // ----------------------------------------------------------------
        // Amount snapshot — stamp the "that many" slot BEFORE the trigger
        // queues / resolves. Filtered to the enchanted creature's controller
        // and to strictly-positive deltas (a life gain — CR 119.3).
        // ----------------------------------------------------------------
        if (eventBus != null)
        {
            eventBus.Subscribe<LifeChangedEvent>(e =>
            {
                var controller = EnchantedController();
                if (controller == null) return;
                if (!ReferenceEquals(e.Player, controller)) return;
                var delta = e.NewLife - e.PreviousLife;
                if (delta <= 0) return;
                slot.Amount = delta;
            });
        }

        // ----------------------------------------------------------------
        // Granted lifegain trigger — CR 603.1 / 119.3 / 603.7.
        //   "Whenever you gain life, put that many +1/+1 counters on this
        //    creature."
        // Condition: LifeChangedEvent.Player == enchanted creature's
        // controller AND NewLife > PreviousLife. Resolution: place slot.Amount
        // +1/+1 counters on the enchanted creature ("this creature"), rechecked
        // still-attached + still on the battlefield (CR 608.2). The slot is
        // reset to 0 after placement so a stale value can't replay.
        // ----------------------------------------------------------------
        var counterEffect = new Effect(
            $"{CardName}: put that many +1/+1 counters on the enchanted creature",
            () =>
            {
                var amount = slot.Amount;
                slot.Amount = 0;
                if (amount <= 0) return;

                // "this creature" — the enchanted creature. Recheck it is still
                // attached and on the battlefield (CR 608.2).
                if (card.AttachedTo is not Creature enchanted) return;
                if (enchanted.Zone != ZoneType.Battlefield) return;

                enchanted.Counters.Add(CounterType.PlusOnePlusOne, amount);
            });

        var lifegainTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<LifeChangedEvent>((e, _) =>
            {
                var controller = EnchantedController();
                if (controller == null) return false;
                return ReferenceEquals(e.Player, controller)
                    && e.NewLife > e.PreviousLife;
            }),
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(lifegainTrigger);

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Sunbond. The
    /// printed "Enchant creature" line (CR 702.5b / 303.4c) makes any creature
    /// a legal target. Filters the supplied battlefield to creatures; on
    /// resolve the aura enters already attached to the chosen target
    /// (CR 303.4f).
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Enchantment aura,
        IEnumerable<Permanent> battlefield)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(battlefield);

        return AuraSpellDefinitionBuilder.ForAura(
            aura,
            targetDescription: "target creature",
            battlefield: battlefield,
            predicate: p => p != null && p.HasType(CardType.Creature));
    }

    /// <summary>
    /// Closure-shared mutable cell for the "that many" amount snapshot
    /// (CR 603.7). Stamped by the bus subscription and consumed + cleared by
    /// the trigger Effect at resolution.
    /// </summary>
    private sealed class AmountSlot
    {
        public int Amount;
    }
}
