using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Brago, King Eternal (Conspiracy, {2}{W}{U}).
///
/// Legendary Creature — Spirit Advisor 2/4. Oracle text (Scryfall, verified):
///   "Flying
///    Whenever Brago, King Eternal deals combat damage to a player, exile
///    any number of nonland permanents you control, then return those
///    cards to the battlefield under your control."
///
/// ## Implemented (v1)
/// - 2/4 Legendary Creature — Spirit Advisor at {2}{W}{U}.
/// - <b>Flying (CR 702.9)</b> as a <see cref="KeywordAbility"/> marker.
///   Evasion check is enforced by the combat damage step.
/// - <b>Combat-damage-to-player trigger (CR 603.6 / CR 510.1)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="CombatDamageDealtEvent"/>
///   filtered to <c>Source == Brago AND TargetPlayer != null</c>. Mirrors
///   Ragavan, Nimble Pilferer's combat-damage trigger shape. The trigger
///   declares a 1..1 "target nonland permanent you control" request
///   (v1 single-target collapse — see deferred gaps).
/// - <b>Exile-then-return blink (CR 701.21 + CR 614)</b>: on resolve,
///   exile the chosen permanent and immediately return it to the
///   battlefield under its owner's control. Same exile-then-return
///   shape as <see cref="CloudshiftFactory"/>'s resolve closure — when
///   a <see cref="ZoneService"/> is supplied both halves route through
///   it so dependent ETB / LTB triggers fire; otherwise the moves are
///   raw zone manipulation (shape-only path).
/// - The re-entry creates a new game object (CR 400.7) — Brago's
///   classic line is blinking ETB engines (Solemn Simulacrum, Mulldrifter)
///   on every connect.
///
/// ## Deferred (v1 gaps)
/// - <b>"Exile any number of nonland permanents you control"</b>: the
///   printed text is a variable-N target list; v1 collapses to a single
///   1..1 target nonland permanent (deterministic). Real agent-driven
///   N-target enumeration awaits the modal / multi-target prompt MVP —
///   same posture as Slogurk's "up to three land cards" return, Quirion
///   Beastcaller's "any number of target creatures" distribution.
/// - <b>"Then return those cards"</b>: anaphoric on the exile half — v1
///   wires it as the same closure (no separate resolution step), which
///   matches Cloudshift's same-resolution exile-then-return.
/// </summary>
[CardName("Brago, King Eternal")]
public static class BragoKingEternalFactory
{
    public const string CardName = "Brago, King Eternal";
    public const string PrintedManaCost = "{2}{W}{U}";
    public const int Power = 2;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Brago with no live wiring. The combat-damage trigger is
    /// attached structurally for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null);

    /// <summary>
    /// Construct Brago with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, the exile-then-return zone
    /// moves route through <see cref="ZoneService.MoveCard"/> so
    /// downstream ETB triggers (Solemn Simulacrum / Mulldrifter /
    /// Sun Titan) fire on the re-entry.</param>
    /// <param name="triggers">When supplied, the combat-damage trigger
    /// registers so a qualifying <see cref="CombatDamageDealtEvent"/>
    /// automatically queues the ability (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Spirit, CardSubtype.Advisor });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying keyword marker.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Combat-damage-to-player trigger (CR 603.6 / CR 510.1):
        //   "Whenever Brago, King Eternal deals combat damage to a player,
        //    exile any number of nonland permanents you control, then
        //    return those cards to the battlefield under your control."
        //
        // v1 single-target collapse (see class xmldoc) — declares a 1..1
        // "target nonland permanent you control" request.
        // ----------------------------------------------------------------
        TriggeredAbility? trigger = null;

        var effect = new Effect(
            $"{CardName}: blink target nonland permanent you control (single-target collapse)",
            () => ResolveBlinkTrigger(trigger, card, owner, zones));

        trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
                ReferenceEquals(e.Source, card) && e.TargetPlayer != null),
            effects: new IEffect[] { effect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target nonland permanent you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff,
                    CandidateGatherer: ctx =>
                    {
                        var controller = card.Controller ?? owner;
                        return controller.Zones.Battlefield.GetCards()
                            .OfType<Permanent>()
                            .Where(p => !p.HasType(CardType.Land))
                            .Where(p => ReferenceEquals(p.Controller, controller))
                            .Cast<object>()
                            .ToList();
                    }),
            });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    // --- Blink resolve (CR 701.21 + CR 614) -------------------------------

    private static void ResolveBlinkTrigger(
        TriggeredAbility? trigger,
        Creature card,
        Player owner,
        ZoneService? zones)
    {
        var target = ResolveLegalBlinkTarget(trigger, card, owner);
        if (target == null) return;

        var targetOwner = target.Owner ?? card.Controller ?? owner;

        // CR 701.21 — exile then CR 614 — return to battlefield under
        // owner's control in the same resolution. Mirrors Cloudshift.
        ExileTarget(target, targetOwner, zones);
        if (target.Zone != ZoneType.Exile) return;
        ReturnTargetFromExile(target, targetOwner, zones);
    }

    private static Permanent? ResolveLegalBlinkTarget(
        TriggeredAbility? trigger,
        Creature card,
        Player owner)
    {
        if (trigger == null) return null;
        if (trigger.ChosenTargets.Count == 0 || trigger.ChosenTargets[0].Count == 0) return null;
        if (trigger.ChosenTargets[0][0] is not Permanent target) return null;

        // CR 608.2b — resolution-time legality.
        if (target.Zone != ZoneType.Battlefield) return null;
        if (target.HasType(CardType.Land)) return null;

        var myController = card.Controller ?? owner;
        if (!ReferenceEquals(target.Controller, myController)) return null;
        return target;
    }

    private static void ExileTarget(Permanent target, Player targetOwner, ZoneService? zones)
    {
        if (zones != null)
        {
            zones.MoveCard(target, ZoneType.Battlefield, ZoneType.Exile);
            return;
        }
        targetOwner.Zones.Battlefield.RemoveCard(target);
        targetOwner.Zones.Exile.AddCard(target);
        target.SetZone(ZoneType.Exile);
    }

    private static void ReturnTargetFromExile(Permanent target, Player targetOwner, ZoneService? zones)
    {
        if (zones != null)
        {
            zones.MoveCard(target, ZoneType.Exile, ZoneType.Battlefield, targetOwner);
            return;
        }
        targetOwner.Zones.Exile.RemoveCard(target);
        targetOwner.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);
        target.SetController(targetOwner);
    }
}
