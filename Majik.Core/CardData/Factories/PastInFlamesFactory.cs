using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Past in Flames (Innistrad, {3}{R}).
///
/// Sorcery. Oracle text:
///   "Each instant and sorcery card in your graveyard gains flashback until
///    end of turn. The flashback costs are equal to their mana costs.
///    Flashback {4}{R}."
///
/// ## Implemented (v1)
/// - Sorcery {3}{R} (Red) shape, owner / controller wired.
/// - Resolve effect (<see cref="BuildResolveEffect"/>) snapshots the
///   controller's graveyard at resolution time and grants runtime
///   flashback (<see cref="Card.GrantRuntimeFlashback"/>) to every instant
///   or sorcery card found, stamped with each card's own printed mana cost
///   (CR 702.34 — "The flashback costs are equal to their mana costs").
///   Past in Flames itself is skipped — it just moved to the graveyard
///   when it resolved, but its own flashback grant comes from its printed
///   Flashback {4}{R} keyword, not from this self-referential grant.
/// - EOT cleanup: when an <see cref="IEventBus"/> is supplied, a one-shot
///   <see cref="StepStartedEvent"/> handler clears every grant stamped by
///   this resolution on the first <see cref="PhaseStateType.Cleanup"/>
///   step (CR 514.2). No bus → grants persist until callers clear them
///   (shape / test path). Mirrors the cleanup posture used by
///   <see cref="FlashbackFactory"/> + <see cref="SnapcasterMageFactory"/>.
/// - <b>Printed Flashback {4}{R}</b> alt-cost: produced via
///   <see cref="GetFlashbackAlternativeCost"/> so callers (bots /
///   integration tests) can cast Past in Flames itself from the graveyard
///   via <see cref="FlashbackAlternativeCost"/>. Same alt-cost wiring as
///   every other printed-flashback card.
///
/// ## How granted cards get cast
/// Each granted graveyard card keeps the stamped cost on
/// <see cref="Card.RuntimeFlashbackCost"/>. Callers cast via
/// <see cref="FlashbackAlternativeCost"/> built from that cost.
/// <see cref="Majik.Core.Players.Agents.RuntimeFlashbackAltCostProbe"/>
/// surfaces the grant to <see cref="HeuristicBotAgent"/>, so a bot wired
/// with that probe automatically considers replaying each granted spell.
///
/// ## Deferred (v1 gaps)
/// - <b>Storm count: not a storm card.</b> Past in Flames is a Storm-PILLAR
///   enabler (rebuy graveyard spells to chain storms), not a Storm-keyword
///   card; no <see cref="Majik.Core.Keywords.StormHelper"/> wiring here.
/// - <b>Per-grant alt-cost probe registration</b>: the
///   <see cref="RuntimeFlashbackAltCostProbe"/> on the bot side reads
///   <see cref="Card.RuntimeFlashbackCost"/> at decision time, so no extra
///   probe wiring per-grant is needed.
/// - <b>Stack-zone vs. graveyard-zone race</b>: Past in Flames moves to
///   the graveyard during resolution (CR 608.2f); the resolve effect
///   intentionally filters by current zone at the moment of execution.
///   When the body of the effect runs the card has typically been moved
///   to graveyard already — we skip self to avoid stamping a flashback
///   grant on Past in Flames itself (its printed Flashback {4}{R} is the
///   authoritative cost).
/// </summary>
[CardName("Past in Flames")]
public static class PastInFlamesFactory
{
    public const string CardName = "Past in Flames";
    public const string PrintedManaCost = "{3}{R}";
    public const string FlashbackManaCost = "{4}{R}";

    /// <summary>
    /// Construct the Past in Flames sorcery shape with no resolve effect
    /// bound. Use <see cref="BuildResolveEffect"/> to compose the
    /// graveyard-grant body into a
    /// <see cref="Majik.Core.Game.SpellDefinition"/> or
    /// <see cref="Majik.Core.Spells.Spell"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the resolve-time effect for Past in Flames: grant runtime
    /// flashback (cost = each card's printed mana cost) to every instant
    /// or sorcery card in <paramref name="controller"/>'s graveyard.
    /// </summary>
    /// <param name="controller">The resolving controller — only THEIR
    /// graveyard is scanned (CR 702.34 — "your graveyard"). Past in Flames
    /// itself is skipped to avoid overwriting its printed Flashback {4}{R}
    /// cost with the {3}{R} printed mana cost.</param>
    /// <param name="eventBus">Optional event bus; when supplied a one-shot
    /// <see cref="StepStartedEvent"/> subscription clears every grant
    /// stamped by this resolution on the first
    /// <see cref="PhaseStateType.Cleanup"/> step (CR 514.2). No bus → no
    /// auto-cleanup.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player controller, IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: each instant and sorcery in your graveyard gains flashback until end of turn (cost = its mana cost)",
                () =>
                {
                    // Snapshot the graveyard at resolution time. The
                    // controller's graveyard is the only legal source
                    // (CR 702.34 — "your graveyard"). Materialize to a
                    // list so any incidental mutations during iteration
                    // don't trip the enumerator.
                    var granted = new List<Card>();
                    foreach (var raw in controller.Zones.Graveyard.GetCards().ToList())
                    {
                        if (raw is not Card target) continue;
                        // Skip non-instant / non-sorcery cards — only
                        // instant/sorcery cards gain flashback per the
                        // printed effect.
                        if (!target.HasType(CardType.Instant) && !target.HasType(CardType.Sorcery)) continue;
                        // Skip Past in Flames itself. Past in Flames has
                        // printed Flashback {4}{R}; the resolve effect's
                        // {3}{R} mana cost is NOT the authoritative
                        // flashback cost. Self-stamp would overwrite the
                        // correct printed cost with the wrong one.
                        if (target.Name == CardName) continue;
                        target.GrantRuntimeFlashback(target.ManaCostValue);
                        granted.Add(target);
                    }

                    if (eventBus == null || granted.Count == 0) return;

                    // CR 514.2 — single one-shot Cleanup-step handler
                    // clears every grant stamped by this resolution.
                    // Same shape as FlashbackFactory / SnapcasterMage.
                    Action<StepStartedEvent>? handler = null;
                    handler = e =>
                    {
                        if (e.StepType != PhaseStateType.Cleanup) return;
                        foreach (var c in granted) c.ClearRuntimeFlashback();
                        if (handler != null) eventBus.Unsubscribe(handler);
                    };
                    eventBus.Subscribe(handler);
                }),
        };
    }

    /// <summary>
    /// Build the <see cref="FlashbackAlternativeCost"/> for Past in Flames
    /// itself — the printed Flashback {4}{R}. Callers cast Past in Flames
    /// from the graveyard by passing this alt-cost to
    /// <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>.
    /// </summary>
    public static FlashbackAlternativeCost GetFlashbackAlternativeCost() =>
        new(ManaCost.Parse(FlashbackManaCost));
}
