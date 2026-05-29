using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Dark-Dwellers (Oath of the Gatewatch,
/// {3}{R}{R}).
///
/// Creature — Goblin 4/4. Oracle text (verified against Scryfall 2026-05-29):
///   "Menace (This creature can't be blocked except by two or more creatures.)
///    When this creature enters, you may cast target instant or sorcery card
///    with mana value 3 or less from your graveyard without paying its mana
///    cost. If that spell would be put into your graveyard, exile it instead."
///
/// ## Analogue lineage
/// Pairs two shapes already in the engine:
/// - <b>Menace (CR 702.111)</b> — <see cref="KeywordAbility"/> marker consumed
///   by <see cref="Majik.Core.Combat.CombatAbilities.HasMenace"/> at
///   block-declaration time (same posture as
///   <see cref="InsolentNeonateFactory"/> / <see cref="SireOfSevenDeathsFactory"/>).
/// - <b>ETB graveyard free-cast</b> — the same ETB
///   <see cref="TriggeredAbility"/> + <see cref="TargetRequest"/> shape as
///   <see cref="SnapcasterMageFactory"/>, except the resolution stamps a
///   <em>zero-cost</em> runtime flashback grant
///   (<see cref="Card.GrantRuntimeFlashback"/> with <see cref="ManaCost.Zero"/>)
///   rather than a grant equal to the card's mana cost. A zero-cost
///   flashback-style grant is exactly "cast without paying its mana cost"
///   (CR 118.5) + "exile it instead [of going to the graveyard]" — the
///   <see cref="Majik.Core.Costs.FlashbackAlternativeCost.OnResolved"/> exile
///   (CR 702.34b) implements the printed replacement for the resolution trip.
///
/// ## Base shape
/// Name / Creature type / Goblin subtype / 4/4 / {3}{R}{R} are materialised
/// from the embedded JSON definition (<c>goblin-dark-dwellers.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Menace and the ETB trigger are
/// layered on here because the JSON <c>AbilityDefinition</c> schema expresses
/// neither yet (same posture as <see cref="ArdentPleaFactory"/>).
///
/// ## ETB trigger (CR 603.6a)
/// - Declares an <b>optional</b> single <see cref="TargetRequest"/>
///   (0..1 — "you may cast target … card") for an instant or sorcery card
///   with mana value 3 or less in the controller's graveyard.
/// - On resolution it re-checks legality (CR 608.2b — the chosen object must
///   still be a graveyard instant/sorcery the controller owns with MV ≤ 3)
///   then stamps a zero-cost runtime flashback grant on the target.
///
/// ## How the granted spell is cast
/// Identical to <see cref="SnapcasterMageFactory"/>: callers build a
/// <see cref="Majik.Core.Costs.FlashbackAlternativeCost"/> from the card's
/// <see cref="Card.RuntimeFlashbackCost"/> (here, <see cref="ManaCost.Zero"/>)
/// and pass it to <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>. The
/// existing flashback alt-cost path handles the graveyard zone-restriction,
/// the alternative-cost-replaces-printed-cost semantics, and the
/// exile-on-resolution side effect (CR 702.34b) that satisfies "exile it
/// instead". No new spell-cast plumbing is introduced.
///
/// ## Deferred (v1 gaps)
/// - <b>"Exile it instead" beyond the resolution trip</b>: the printed text is
///   a continuous one-shot replacement scoped to <em>that spell</em>. The
///   flashback-style exile-on-resolve covers the common case (the spell
///   resolves and would head to the graveyard). A spell that would be put
///   into the graveyard for a different reason (e.g. countered) is not
///   separately re-routed here — same limitation as the existing
///   flashback/Snapcaster grant path. Recorded, not half-built.
/// </summary>
[CardName("Goblin Dark-Dwellers")]
public static class GoblinDarkDwellersFactory
{
    public const string CardName = "Goblin Dark-Dwellers";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "goblin-dark-dwellers";

    /// <summary>"with mana value 3 or less" cast filter (CR 202.3).</summary>
    public const int MaxTargetManaValue = 3;

    /// <summary>
    /// Single-arg dispatcher path (used by <see cref="NamedCardFactory"/>).
    /// Attaches Menace + the ETB trigger structurally; no event bus, so the
    /// EOT-cleanup hook on the grant is inert. Suitable for shape /
    /// dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Goblin Dark-Dwellers with an optional event bus. When the bus
    /// is supplied, the ETB effect subscribes to <see cref="StepStartedEvent"/>
    /// and clears the runtime flashback grant on the next Cleanup step
    /// (CR 514.2) if the granted card was not cast.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name / Creature / Goblin / 4/4 / {3}{R}{R}) from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Creature card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Creature but got "
                + $"'{built.GetType().Name}'.");
        }

        // CR 702.111 — Menace keyword marker. Consumed by
        // CombatAbilities.HasMenace at block-declaration time.
        card.AddAbility(new KeywordAbility("Menace", card, owner));

        // CR 603.6a — ETB triggered ability. Declares an optional (0..1)
        // target request for an instant or sorcery card with MV ≤ 3 in the
        // controller's graveyard; on resolution stamps a zero-cost runtime
        // flashback grant (= "cast without paying its mana cost" + exile on
        // resolve) on the chosen card.
        TriggeredAbility? etb = null;
        var condition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            "Goblin Dark-Dwellers — you may cast target instant or sorcery with mana value 3 or less from your graveyard without paying its mana cost; exile it instead of putting it in the graveyard",
            () =>
            {
                if (etb == null) return;
                var chosen = etb.ChosenTargets;
                // "you may cast target … card" — optional. No target → no-op.
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];
                if (raw is not Card target) return;

                // CR 608.2b — legality re-check on resolution: still in the
                // controller's graveyard, owned by the controller, an
                // instant or sorcery card, MV ≤ 3.
                if (target.Zone != ZoneType.Graveyard) return;
                if (!ReferenceEquals(target.Owner, owner)) return;
                if (!target.HasType(CardType.Instant) && !target.HasType(CardType.Sorcery)) return;
                if (target.ManaCostValue.TotalValue > MaxTargetManaValue) return;

                // CR 118.5 — "without paying its mana cost": stamp a ZERO-cost
                // flashback-style grant. FlashbackAlternativeCost.OnResolved
                // exiles the card on resolution (CR 702.34b), implementing
                // "If that spell would be put into your graveyard, exile it
                // instead." for the resolution trip.
                target.GrantRuntimeFlashback(ManaCost.Zero);

                // CR 514.2 — schedule cleanup. If the granted card was never
                // cast, clear the grant on the first Cleanup step. No bus →
                // no auto-cleanup (shape-only path; callers manage EOT).
                if (eventBus == null) return;

                Action<StepStartedEvent>? handler = null;
                handler = e =>
                {
                    if (e.StepType != PhaseStateType.Cleanup) return;
                    // Only clear if it is still in the graveyard (uncast).
                    if (target.Zone == ZoneType.Graveyard) target.ClearRuntimeFlashback();
                    if (handler != null) eventBus.Unsubscribe(handler);
                };
                eventBus.Subscribe(handler);
            });

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target instant or sorcery card with mana value 3 or less in your graveyard",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: System.Array.Empty<object>()),
            });

        card.AddAbility(etb);

        return card;
    }
}
