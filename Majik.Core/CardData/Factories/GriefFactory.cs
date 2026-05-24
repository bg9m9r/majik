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
/// Named-card factory for Grief (Modern Horizons 2, {2}{B}).
///
/// Creature — Elemental Incarnation 3/2. Oracle text:
///   "Menace
///    When this creature enters, target opponent reveals their hand. You
///    choose a nonland card from it. That player discards that card.
///    Evoke—Exile a black card from your hand."
///
/// ## Implemented (v1)
/// - 3/2 Elemental Incarnation with Menace + Evoke keyword markers.
/// - Evoke alt-cost (<see cref="Majik.Core.Costs.EvokeAlternativeCost"/>):
///   exile a black card from hand replaces the {2}{B} mana cost
///   (CR 702.74 + CR 117.11).
/// - Evoke sacrifice trigger (<see cref="EvokeFactory"/>): "When this creature
///   enters, if its evoke cost was paid, sacrifice it" (CR 702.74b).
/// - ETB triggered ability: target opponent reveals their hand; controller
///   deterministically picks the first nonland card; that player discards
///   it (goes to graveyard). Mirrors the Thoughtseize-style helper.
///
/// ## Deferred (v1 gaps)
/// - <b>Opponent pitch-back</b>: the printed text on the actual card has a
///   nested "that player may exile a non-Elemental, non-Incarnation black
///   card from their hand to counter this triggered ability" clause. That
///   requires a synchronous opponent-prompt during another player's spell
///   resolution. Deferred — v1 always resolves the discard fully.
/// - <b>Caster's choice</b>: v1 picks the first nonland card deterministically
///   (mirrors <see cref="ThoughtseizePatternTemplate"/>). Real Grief lets the
///   caster choose any nonland card from the revealed hand.
/// </summary>
[CardName("Grief")]
public static class GriefFactory
{
    /// <summary>Construct Grief owned and controlled by <paramref name="owner"/>.</summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Grief",
            manaCost: "{2}{B}",
            power: 3,
            toughness: 2,
            subtypes: new[] { CardSubtype.Elemental, CardSubtype.Incarnation });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Keyword markers — CR 702.110 (Menace) + CR 702.74 (Evoke). When
        // this factory is used directly (test / NamedCardFactory path) the
        // markers aren't supplied by KeywordBinder, so attach them inline
        // for consistency with SolitudeFactory.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Menace", card, owner));
        card.AddAbility(new KeywordAbility("Evoke", card, owner));

        // ----------------------------------------------------------------
        // Printed evoke sacrifice trigger (CR 702.74b).
        //   "When this creature enters, if its evoke cost was paid,
        //    sacrifice it."
        // Reuses the shared EvokeFactory helper — same wiring Solitude /
        // Endurance / Fury / Subtlety use for the MH2 incarnation cycle.
        // ----------------------------------------------------------------
        card.AddAbility(EvokeFactory.Build(card));

        // ----------------------------------------------------------------
        // ETB reveal-and-discard trigger (CR 603.6a / CR 701.16).
        // Declares a "target opponent" TargetRequest. The effect reveals
        // the target's hand, picks the first nonland card deterministically,
        // and moves it to the target's graveyard (discard, CR 701.8).
        // ----------------------------------------------------------------
        TriggeredAbility? discardTrigger = null;
        var discardCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var discardEffect = new Effect(
            "Grief — target opponent reveals their hand; you choose a nonland card; that player discards it",
            () =>
            {
                if (discardTrigger == null) return;
                var chosen = discardTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Player targetPlayer) return;

                // CR 701.16 — "Target opponent reveals their hand" is a public
                // state transition; the engine's hand state is already
                // observable to all agents via the trigger's resolution. UI
                // wire-up (CardRevealedEvent fan-out) is handled by the
                // outer SpellCastFlow / TriggerManager bus path when a live
                // EventBus is attached at the game level. The factory shell
                // path here does not synthesise a separate reveal event.

                // v1: deterministic pick — first non-land card in target's hand.
                var pick = targetPlayer.Zones.Hand.GetCards()
                    .FirstOrDefault(c => !c.HasType(CardType.Land));

                if (pick == null) return; // hand is empty or contains only lands → no discard

                // CR 701.8 — discard: move from hand to owner's graveyard.
                targetPlayer.Zones.Hand.RemoveCard(pick);
                targetPlayer.Zones.Graveyard.AddCard(pick);
                pick.SetZone(ZoneType.Graveyard);
            });

        discardTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: discardCondition,
            effects: new[] { discardEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target opponent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(discardTrigger);

        return card;
    }
}
