using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fury (Modern Horizons 2, {3}{R}).
///
/// Creature — Elemental Incarnation 3/3. Oracle text (verified against Scryfall
/// 2026-06-16 — Fury was ERRATA'd from the original "X = cards in hand" wording
/// to a FIXED 4 damage):
///   "Double strike
///    When this creature enters, it deals 4 damage divided as you choose among
///    any number of target creatures and/or planeswalkers.
///    Evoke—Exile a red card from your hand."
///
/// Pattern mirrors <see cref="SolitudeFactory"/> — Evoke alt-cost wired via
/// <see cref="Majik.Core.Costs.EvokeAlternativeCost"/>, evoke sacrifice trigger
/// wired via <see cref="EvokeFactory"/>, and the printed ETB triggered ability
/// is attached inline here.
///
/// ## Implemented (v1)
/// - 3/3 Elemental Incarnation with Double strike + Evoke keyword markers.
/// - Evoke alt-cost = "exile a red card from hand" (Solitude analogue).
/// - Evoke sacrifice trigger (CR 702.74b).
/// - <b>ETB divide-damage trigger (CR 601.2d / CR 119.4)</b>: declares a
///   0..many "target creature and/or planeswalker" request AND a
///   <see cref="DamageDivisionSpec"/>(4). The engine prompts the controller's
///   agent for the per-target split at stack entry (Rule 603.3, in
///   <see cref="Services.TriggerManager.PutPendingTriggersOnStackAsync"/>) —
///   the triggered-ability analogue of the cast-time divide-damage seam — and
///   resolution deals the announced amounts via
///   <see cref="Fx.DealDividedDamageAny"/>, routing each allocation through the
///   canonical Player / Creature / Planeswalker seam (CR 119 / CR 306.7). When
///   no agent answered (no-agent dispatcher path) the 4 is even-split over the
///   chosen targets. The optional <c>distribute</c> Func overrides the agent
///   prompt for direct/test callers.
/// </summary>
[CardName("Fury")]
public static class FuryFactory
{
    public const int DividedDamage = 4;

    /// <summary>Construct Fury owned and controlled by <paramref name="owner"/>.
    /// When <paramref name="distribute"/> is supplied it overrides the agent
    /// divide-damage prompt (deal-off-ChosenTargets, used by tests); when null
    /// the engine's agent-driven division prompt (or its even-split fallback)
    /// drives the allocation.</summary>
    public static Creature Create(
        Player owner,
        Func<IReadOnlyList<Permanent>, int, IReadOnlyDictionary<Permanent, int>>? distribute = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Fury",
            manaCost: "{3}{R}",
            power: 3,
            toughness: 3,
            subtypes: new[] { CardSubtype.Elemental, CardSubtype.Incarnation });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Keyword markers — CR 702.4 (Double strike), CR 702.74 (Evoke).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Double strike", card, owner));
        card.AddAbility(new KeywordAbility("Evoke", card, owner));

        // ----------------------------------------------------------------
        // Printed evoke sacrifice trigger (CR 702.74b).
        // ----------------------------------------------------------------
        card.AddAbility(EvokeFactory.Build(card));

        // ----------------------------------------------------------------
        // ETB divide-damage trigger (CR 603.6a / CR 601.2d / CR 119.4):
        // "deals 4 damage divided as you choose among any number of target
        // creatures and/or planeswalkers."
        // ----------------------------------------------------------------
        var effect = new Effect(
            $"Fury — deal {DividedDamage} damage divided among target creatures/planeswalkers",
            rc =>
            {
                if (distribute is null)
                {
                    // Engine path — deal the agent-announced split (or even
                    // split) off the live ResolutionContext. Illegal-at-
                    // resolution targets (CR 608.2b) are skipped inside
                    // Fx.DealDividedDamageAny by the per-position mapping.
                    Fx.DealDividedDamageAny(rc, DividedDamage, source: card);
                    return ValueTask.CompletedTask;
                }

                // Direct/test override: honour the supplied distribution over
                // the chosen legal targets.
                var slots = rc.ChosenTargets;
                var targets = new List<Permanent>();
                if (slots.Count > 0)
                {
                    foreach (var raw in slots[0])
                    {
                        if (raw is Permanent p
                            && p.Zone == ZoneType.Battlefield
                            && (p is Creature || p.IsEffectivePlaneswalker() || p is Planeswalker))
                        {
                            targets.Add(p);
                        }
                    }
                }
                if (targets.Count == 0) return ValueTask.CompletedTask;

                var allocation = distribute(targets, DividedDamage)
                    ?? new Dictionary<Permanent, int>();
                foreach (var (perm, amount) in allocation)
                {
                    if (amount <= 0) continue;
                    if (perm.Zone != ZoneType.Battlefield) continue;
                    Fx.DealDamageAny(perm, amount);
                }
                return ValueTask.CompletedTask;
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>(
                (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield),
            effects: new[] { effect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any number of target creatures and/or planeswalkers",
                    MinTargets: 0,
                    MaxTargets: int.MaxValue,
                    LegalCandidates: Array.Empty<object>()),
            },
            damageDivision: new DamageDivisionSpec(DividedDamage, TargetSlotIndex: 0));

        card.AddAbility(etbTrigger);

        return card;
    }
}
