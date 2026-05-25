using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pestermite (Lorwyn, {2}{U}).
///
/// Creature — Faerie Rogue 2/1. Oracle text:
///   "Flash
///    Flying
///    When this creature enters, you may tap or untap target permanent."
///
/// ## Implemented (v1)
/// - 2/1 Faerie Rogue with Flash + Flying keyword markers
///   (<see cref="KeywordAbility"/> — CR 702.8 / CR 702.9). Combat helpers
///   read Flying directly; Flash gates instant-speed casting.
/// - ETB <see cref="TriggeredAbility"/> (CR 603.6a) declaring a 1..1
///   "target permanent" <see cref="TargetRequest"/> (Intent:
///   <see cref="BotIntent.Untap"/>). The "you may" rider (CR 605.1 / 117.x)
///   is honoured at resolution — when a target was chosen the controller
///   either taps or untaps the chosen permanent based on its current
///   tapped state (the v1 deterministic "useful" pick: untap a tapped
///   target, tap an untapped one), so the chain always does something
///   observable when paired with Splinter Twin.
/// - Resolution-time legality: the chosen permanent must still be on
///   the battlefield (CR 608.2b — illegal-on-resolution → clean no-op).
///
/// ## Splinter Twin pairing (the famous Twin combo)
/// When Splinter Twin's granted "{T}: create token copy" ability is
/// activated on Pestermite, the spawned token is a copy of Pestermite
/// per CR 706.2 — so it inherits Pestermite's printed ETB trigger AND
/// the bearer's Twin-granted activated ability. The token's own ETB
/// fires (CR 603.6a) with a free choice of target; pointing the untap
/// at the still-tapped original Pestermite recharges the Twin engine
/// for a second activation. Repeat ad infinitum (the classic UR Twin
/// loop). See
/// <see cref="Majik.Core.Tests.Effects.SplinterTwinTokenChainTests"/>
/// for the token-inheritance / chain assertions.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven "may" / tap-or-untap mode prompt</b>: v1 collapses
///   the printed choice into a deterministic "useful flip" (untap if
///   currently tapped, else tap). Real agent prompts for the binary
///   mode + "may" decline land alongside the broader modal-trigger
///   queue (same gap noted on Snapcaster Mage / Splinter Twin tests).
/// - <b>Target legality at choose-time</b>: <c>LegalCandidates</c> is
///   left empty (same posture as Solitude / Snapcaster Mage / Subtlety
///   — production agent enumerates the live battlefield itself).
/// </summary>
[CardName("Pestermite")]
public static class PestermiteFactory
{
    public const string CardName = "Pestermite";
    public const string PrintedManaCost = "{2}{U}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>Construct Pestermite owned and controlled by
    /// <paramref name="owner"/>. The ETB trigger is attached structurally;
    /// callers that want bus-driven firing register the returned
    /// <see cref="TriggeredAbility"/> with their <see cref="TriggerManager"/>
    /// (same shape as SnapcasterMage / Subtlety).</summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Faerie, CardSubtype.Rogue });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 / CR 702.9 — Flash + Flying keyword markers.
        card.AddAbility(new KeywordAbility("Flash", card, owner));
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // CR 603.6a — ETB trigger: "When this creature enters, you may
        // tap or untap target permanent." Declares a 1..1 "target
        // permanent" TargetRequest. The "may" + "tap or untap" choice is
        // collapsed to a deterministic "useful flip" at resolution in v1
        // — untap a tapped target, tap an untapped one, no-op when no
        // legal target was chosen (printed "may" + CR 608.2b).
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var condition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName} — tap or untap target permanent",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return; // printed "may" declined

                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — illegal-on-resolution. Target must still
                // be on the battlefield.
                if (target.Zone != ZoneType.Battlefield) return;

                // Deterministic "useful flip" — tap-or-untap is the
                // printed mode, v1 picks whichever changes state. Twin
                // pairing relies on this: the spawned token's ETB
                // un-taps the original Pestermite so the next Twin
                // activation can re-tap it.
                if (target.IsTapped) target.Untap();
                else target.Tap();
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target permanent",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(etbTrigger);

        return card;
    }
}
