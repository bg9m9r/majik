using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Big Game Hunter (Time Spiral, {1}{B}{B}).
///
/// Creature — Human Rebel Assassin 1/1. Oracle text (verified against Scryfall):
///   "When this creature enters, destroy target creature with power 4 or
///    greater. It can't be regenerated.
///    Madness {B}"
///
/// The base shape (name, Human Rebel Assassin subtypes, {1}{B}{B}, 1/1) is
/// materialised from the embedded JSON definition (<c>big-game-hunter.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The ETB destroy trigger is
/// layered on here — the JSON <c>AbilityDefinition</c> schema doesn't express
/// targeted destroy triggers (same posture as <see cref="NekrataalFactory"/>).
///
/// <b>Madness {B} (CR 702.35)</b> is intrinsic: the central discard funnel
/// (<c>Fx.DiscardCard</c>) consults <c>MadnessCatalog</c> for any catalogued
/// card and routes a discarded Big Game Hunter to exile + offers it for its
/// madness cost automatically. NO factory / JSON code is needed for it.
///
/// ## Implemented (v1)
/// - <b>ETB destroy trigger (CR 603.6a)</b>: "When this creature enters,
///   destroy target creature with power 4 or greater. It can't be
///   regenerated." Declares a mandatory 1..1 <see cref="TargetRequest"/>.
///   The candidate gatherer enumerates every Creature on the battlefield with
///   current power >= 4 (CR 208.3 — power is the left value of the P/T box,
///   as modified by continuous effects), tagged <see cref="BotIntent.Removal"/>.
///   On resolution the effect re-checks legality (CR 608.2b): still a Creature
///   on the battlefield with power >= 4, then destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///   <see cref="ZoneMoveReason.DestroyNoRegeneration"/>. The "can't be
///   regenerated" rider (CR 701.15) is honoured by that move reason:
///   indestructible (CR 702.12) still cancels the destroy, but any active
///   regeneration shield is BYPASSED rather than consumed. Not restricted to
///   opponents — the printed text lets you target any qualifying creature,
///   including your own.
///
/// ## Deferred (v1 gaps)
/// - <b>Target legality in ActionValidator</b>: the validator does not yet
///   enforce "power 4 or greater" at trigger-target declaration; the
///   resolution-time guard handles illegal targets (CR 608.2b). Same posture
///   as <see cref="NekrataalFactory"/>.
/// - <b>Mandatory pick when no legal target</b>: if no creature with power >= 4
///   exists, the ETB still triggers (no "if able" clause) and falls through to
///   a clean no-op at resolution.
/// </summary>
[CardName("Big Game Hunter")]
public static class BigGameHunterFactory
{
    public const string CardName = "Big Game Hunter";
    public const string Slug = "big-game-hunter";

    /// <summary>
    /// Construct Big Game Hunter owned and controlled by <paramref name="owner"/>.
    /// The ETB destroy trigger is attached structurally; no
    /// <see cref="TriggerManager"/> wiring. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Fully-wired construction. <paramref name="triggers"/> registers the
    /// ETB destroy trigger so a <see cref="CardMovedEvent"/>
    /// (Stack → Battlefield) on this card fires it automatically.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human Rebel Assassin subtypes, {1}{B}{B}, 1/1). The JSON carries no
        // abilities — the ETB destroy trigger is layered on below. Madness {B}
        // is intrinsic (MadnessCatalog + Fx.DiscardCard).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB destroy trigger — CR 603.6a.
        //   "When this creature enters, destroy target creature with power 4
        //    or greater. It can't be regenerated."
        // Mandatory 1..1 target. Candidate gatherer enumerates every
        // battlefield Creature whose current power (CR 208.3) is >= 4. Not
        // restricted to opponents — any qualifying creature is legal.
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: destroy target creature with power 4 or greater (no regeneration)",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Creature target) return;

                // CR 608.2b — resolution-time legality re-check: the chosen
                // target must still be a creature on the battlefield with
                // power >= 4. If its power dropped below 4 or it left the
                // battlefield, the ability doesn't affect it (clean no-op).
                if (target.Zone != ZoneType.Battlefield) return;
                if (target.Power < 4) return;

                // CR 701.7 — destroy. "It can't be regenerated" (CR 701.15)
                // honoured via DestroyNoRegeneration: indestructible
                // (CR 702.12) still cancels the destroy, but any active
                // regeneration shield is BYPASSED rather than consumed.
                OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.DestroyNoRegeneration);
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature with power 4 or greater",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Live candidate gatherer: every battlefield Creature whose
                    // current power (CR 208.3) is >= 4. Engine resolves this at
                    // prompt time against the live board.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => c.Power >= 4)
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
