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
/// Named-card factory for Endurance (Modern Horizons 2, {1}{G}{G}).
///
/// Creature — Elemental Incarnation 3/4. Oracle text:
///   "Flash
///    Reach
///    When this creature enters, target player shuffles their graveyard into
///    their library.
///    Evoke—Exile a green card from your hand."
///
/// ## Implemented (v1)
/// - 3/4 Elemental Incarnation with Flash + Reach + Evoke keyword markers
///   (<see cref="KeywordAbility"/> entries attached inline for the
///   <see cref="NamedCardFactory"/> path; the data-driven load route gets
///   them via <see cref="Majik.Core.CardData.Parsing.KeywordBinder"/>).
/// - Evoke alt-cost (<see cref="Majik.Core.Costs.EvokeAlternativeCost"/>):
///   exile a green card from hand replaces the {1}{G}{G} mana cost
///   (CR 702.74 + CR 117.11).
/// - Evoke sacrifice trigger (<see cref="EvokeFactory"/>): "When this creature
///   enters, if its evoke cost was paid, sacrifice it" (CR 702.74b).
/// - ETB graveyard-to-library trigger: when Endurance enters the battlefield,
///   prompts for a target player (any player, including the controller) and
///   shuffles that player's graveyard into their library (CR 701.19c).
///
/// ## Deferred (v1 gaps)
/// - <b>True random shuffle hook</b>: <see cref="IZone"/> doesn't yet expose a
///   <c>Shuffle</c> entry point, so we mirror the Surgical Extraction /
///   GameDriver pattern (remove all → Fisher-Yates with a fresh
///   <see cref="Random"/> → re-add). Deterministic-shuffle tests can inject
///   their own RNG at engine boot. The observable contract — graveyard ends
///   empty, those cards end in the library — is preserved.
/// </summary>
[CardName("Endurance")]
public static class EnduranceFactory
{
    /// <summary>Construct Endurance owned and controlled by <paramref name="owner"/>.</summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Endurance",
            manaCost: "{1}{G}{G}",
            power: 3,
            toughness: 4,
            subtypes: new[] { CardSubtype.Elemental, CardSubtype.Incarnation });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Keyword markers — CR 702.8 (Flash), CR 702.9 (Reach), CR 702.74
        // (Evoke). When this factory is used directly (test /
        // NamedCardFactory path) the markers aren't supplied by
        // KeywordBinder, so attach them inline for consistency with
        // SolitudeFactory / GriefFactory.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flash", card, owner));
        card.AddAbility(new KeywordAbility("Reach", card, owner));
        card.AddAbility(new KeywordAbility("Evoke", card, owner));

        // ----------------------------------------------------------------
        // Printed evoke sacrifice trigger (CR 702.74b).
        //   "When this creature enters, if its evoke cost was paid,
        //    sacrifice it."
        // Reuses the shared EvokeFactory helper — same wiring Solitude /
        // Grief / Fury / Subtlety use for the MH2 incarnation cycle.
        // ----------------------------------------------------------------
        card.AddAbility(EvokeFactory.Build(card));

        // ----------------------------------------------------------------
        // ETB graveyard-to-library trigger (CR 603.6a / CR 701.19c).
        // Declares a "target player" TargetRequest (any player — note this
        // is NOT "opponent" like Grief/Solitude; Endurance can be aimed at
        // its own controller to recycle their graveyard). The effect moves
        // every card in the target's graveyard into their library and
        // shuffles.
        // ----------------------------------------------------------------
        TriggeredAbility? shuffleTrigger = null;
        var shuffleCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var shuffleEffect = new Effect(
            "Endurance — target player shuffles their graveyard into their library",
            () =>
            {
                if (shuffleTrigger == null) return;
                var chosen = shuffleTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Player targetPlayer) return;

                // CR 701.19c — move all graveyard cards to library, then
                // shuffle. We snapshot the list first because mutating the
                // zone while iterating its underlying collection would
                // invalidate the enumerator.
                var graveyardCards = targetPlayer.Zones.Graveyard.GetCards().ToList();
                foreach (var c in graveyardCards)
                {
                    targetPlayer.Zones.Graveyard.RemoveCard(c);
                    targetPlayer.Zones.Library.AddCard(c);
                    c.SetZone(ZoneType.Library);
                }

                ShuffleLibrary(targetPlayer);
            });

        shuffleTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: shuffleCondition,
            effects: new[] { shuffleEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(shuffleTrigger);

        return card;
    }

    /// <summary>
    /// CR 701.19c — shuffle. <see cref="IZone"/> doesn't expose a shuffle
    /// entry point yet (tracked alongside Surgical Extraction's identical
    /// helper), so mirror GameDriver's "remove all → Fisher-Yates → re-add"
    /// pattern with a freshly seeded <see cref="System.Random"/>. The
    /// engine's observable contract here is just "library was shuffled";
    /// deterministic ordering can be re-introduced once the central
    /// shuffle hook lands.
    /// </summary>
    private static void ShuffleLibrary(Player player)
    {
        var lib = player.Zones.Library.GetCards().ToList();
        foreach (var c in lib) player.Zones.Library.RemoveCard(c);

        var rng = new System.Random();
        for (var i = lib.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (lib[i], lib[j]) = (lib[j], lib[i]);
        }

        foreach (var c in lib) player.Zones.Library.AddCard(c);
    }
}
