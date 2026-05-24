using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Solitude (Modern Horizons 2, {3}{W}{W}).
///
/// Creature — Elemental Incarnation 3/2. Oracle text:
///   "Flash
///    Lifelink
///    When this creature enters, exile up to one other target creature. That
///    creature's controller gains life equal to its power.
///    Evoke—Exile a white card from your hand."
///
/// ## Implemented (v1)
/// - 3/2 Elemental Incarnation with Flash + Lifelink + Evoke keyword markers
///   (<see cref="KeywordAbility"/> entries via <see cref="KeywordBinder"/>
///   when loaded from the DB, or attached inline by this factory for the
///   <see cref="NamedCardFactory"/> code path).
/// - Evoke alt-cost (<see cref="Majik.Core.Costs.EvokeAlternativeCost"/>):
///   exile a white card from hand replaces the {3}{W}{W} mana cost (CR 702.74
///   + CR 117.11).
/// - Evoke sacrifice trigger (<see cref="EvokeFactory"/>): "When this creature
///   enters, if its evoke cost was paid, sacrifice it" (CR 702.74b).
/// - ETB exile trigger: when Solitude enters the battlefield, fires a
///   triggered ability that exiles one target creature controlled by an
///   opponent (chosen via the standard <see cref="TargetRequest"/> prompt) and
///   makes that creature's controller gain life equal to the exiled creature's
///   power. "Up to one" is honoured (zero-target resolution is a no-op).
///
/// ## Deferred (v1 gaps)
/// - <b>Opponent pitch-back</b>: the printed text on the actual card has a
///   nested "that player may exile a non-Elemental, non-Incarnation white card
///   from their hand to return the exiled creature" clause. That requires a
///   synchronous opponent-prompt during another player's spell resolution and
///   the "return-from-exile-to-battlefield" path. Tracked separately — the
///   v1 implementation always keeps the exiled creature in exile.
/// - <b>Lifelink P/T sourcing</b>: lifegain currently uses the exiled
///   creature's <see cref="Creature.Power"/> at resolution time. That matches
///   oracle wording (the exiled creature is the last-known-information source
///   per CR 112.7a after it leaves the battlefield).
/// </summary>
[CardName("Solitude")]
public static class SolitudeFactory
{
    /// <summary>Construct Solitude owned and controlled by <paramref name="owner"/>.</summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Solitude",
            manaCost: "{3}{W}{W}",
            power: 3,
            toughness: 2,
            subtypes: new[] { CardSubtype.Elemental, CardSubtype.Incarnation });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Keyword markers — CR 702.8 (Flash), CR 702.15 (Lifelink),
        // CR 702.74 (Evoke). When this factory is used directly (test /
        // NamedCardFactory path) the markers aren't supplied by
        // KeywordBinder, so attach them here for consistency.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flash", card, owner));
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));
        card.AddAbility(new KeywordAbility("Evoke", card, owner));

        // ----------------------------------------------------------------
        // Printed evoke sacrifice trigger (CR 702.74b).
        //   "When this creature enters, if its evoke cost was paid,
        //    sacrifice it."
        // ----------------------------------------------------------------
        card.AddAbility(EvokeFactory.Build(card));

        // ----------------------------------------------------------------
        // ETB exile-target-creature trigger (CR 603.6a / CR 701.21).
        // Declares a "target creature an opponent controls" TargetRequest
        // (0..1 targets — "up to one"). The effect reads the trigger's
        // ChosenTargets, exiles the picked creature, and gives that
        // creature's controller life equal to its power.
        // ----------------------------------------------------------------
        TriggeredAbility? exileTrigger = null;
        var exileCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var exileEffect = new Effect(
            "Solitude — exile up to one other target creature; that creature's controller gains life equal to its power",
            () =>
            {
                if (exileTrigger == null) return;
                var chosen = exileTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return; // "up to one" — zero is legal

                var raw = chosen[0][0];
                if (raw is not Creature target) return;
                // "other" — cannot target Solitude itself even if somehow legal.
                if (ReferenceEquals(target, card)) return;
                if (target.Zone != ZoneType.Battlefield) return; // illegal at resolution

                // Snapshot power BEFORE moving the creature (CR 112.7a — last
                // known information once it leaves the battlefield).
                var snapshotPower = target.Power;
                var targetController = target.Controller ?? target.Owner;

                // Exile (CR 701.21).
                var fromOwner = target.Owner;
                if (fromOwner != null)
                {
                    fromOwner.Zones.Battlefield.RemoveCard(target);
                    fromOwner.Zones.Exile.AddCard(target);
                }
                target.SetZone(ZoneType.Exile);

                // Lifegain to the exiled creature's controller (CR 119.3).
                if (targetController != null && snapshotPower > 0)
                {
                    targetController.GainLife(snapshotPower);
                }
            });

        exileTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: exileCondition,
            effects: new[] { exileEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "up to one other target creature",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(exileTrigger);

        return card;
    }
}
