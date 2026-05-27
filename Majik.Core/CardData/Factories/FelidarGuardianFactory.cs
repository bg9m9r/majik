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
/// Named-card factory for Felidar Guardian (Aether Revolt, {2}{W}).
///
/// Creature — Cat Beast 1/4. Oracle text:
///   "Flash
///    When Felidar Guardian enters, you may exile another target
///    permanent you control, then return that card to the battlefield
///    under your control."
///
/// CR 701.21 (Exile) + CR 614 (return) — Felidar Guardian is the
/// "permanent" widening of <see cref="RestorationAngelFactory"/>: same
/// flicker body, but the target is "another target permanent you
/// control" instead of "another non-Angel creature you control". The
/// infamous Saheeli Rai combo card — Standard-banned for that loop in
/// 2017. The flicker half is shape-identical to Restoration Angel and
/// <see cref="CloudshiftFactory"/> aside from the wider target filter.
///
/// "you may" — the controller chooses on resolve whether to exile/return
/// at all. v1 ships this as a 0..1 <see cref="TargetRequest"/> — selecting
/// zero is the printed "you may not" branch (same idiom as Restoration
/// Angel and Solitude's ETB exile trigger).
///
/// ## Implemented (v1)
/// - 1/4 Creature — Cat Beast at {2}{W}; owner + controller seated.
/// - Flash keyword marker (CR 702.8).
/// - <b>ETB triggered ability</b> (CR 603.6a): single 0..1
///   <see cref="TargetRequest"/> "another target permanent you control"
///   with a live <c>CandidateGatherer</c> that walks the controller's
///   battlefield for any <see cref="Permanent"/> other than Felidar
///   Guardian itself. <see cref="BotIntent.Protection"/>.
///   On resolve: re-checks the target is still a controller-side
///   battlefield permanent (CR 608.2b — illegal target → no effect).
///   Exile via <see cref="ZoneService"/> when supplied (so
///   <see cref="CardMovedEvent"/> fires); falls back to owner-routed
///   zone mutation. Then return under the original caster's control
///   (CR 614 — "under your control"). Defensive
///   <c>Zone == ZoneType.Exile</c> guard before the return handles
///   token vanish (CR 111.8) the same as Cloudshift / Restoration Angel.
///
/// ## Deferred (v1 gaps)
/// - <b>Saheeli Rai loop / token blink</b>: the printed combo is
///   Saheeli's "create a token copy" + Felidar's ETB blinking the token
///   to re-trigger Saheeli's "another creature ETB" payoff. v1 ships
///   the flicker half end-to-end; the Saheeli payoff lands when her
///   factory is wired (separate PR). The token-vanish guard mirrors
///   Cloudshift's defensive posture.
/// - <b>True new-object semantics</b> (CR 400.7): v1 returns the same
///   <see cref="Permanent"/> instance — tracked alongside Cloudshift /
///   Ephemerate / Restoration Angel as a shared "flicker new-object"
///   primitive deferred.
/// - <b>Cat Beast subtype set</b>: ships both
///   <see cref="CardSubtype.Cat"/> and <see cref="CardSubtype.Beast"/>
///   per CR 205.3m. Both subtypes are enumerated in the engine's
///   <see cref="CardSubtype"/>.
/// </summary>
[CardName("Felidar Guardian")]
public static class FelidarGuardianFactory
{
    public const string CardName = "Felidar Guardian";
    public const string PrintedManaCost = "{2}{W}";
    public const int Power = 1;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Felidar Guardian with no live runtime services. Flash
    /// keyword marker and the ETB trigger are attached to the card shape
    /// for structural / dispatch tests; the trigger is not registered
    /// against a <see cref="TriggerManager"/>.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zones: null);

    /// <summary>
    /// Construct Felidar Guardian with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / controller.</param>
    /// <param name="triggers">Manager that registers the ETB trigger
    /// so a qualifying <see cref="CardMovedEvent"/> queues the ability
    /// end-to-end.</param>
    /// <param name="zones">Used to route the exile + return zone moves
    /// so <see cref="CardMovedEvent"/> fires for downstream listeners.
    /// Null → falls back to direct owner-routed zone mutation.</param>
    public static Creature Create(Player owner, TriggerManager? triggers, ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Cat, CardSubtype.Beast });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash keyword marker.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.21 / CR 614.
        //   "When Felidar Guardian enters, you may exile another target
        //    permanent you control, then return that card to the
        //    battlefield under your control."
        //
        // 0..1 TargetRequest models the "may" rider — selecting zero is
        // the "you may not" branch.
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var targetRequest = new TargetRequest(
            Description: "another target permanent you control",
            MinTargets: 0,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.Protection,
            // CR 109.5 / CR 608.2b — controller-scoped gather, "another"
            // rider drops Felidar Guardian itself. Any permanent type
            // qualifies (artifact / creature / enchantment / land /
            // planeswalker / battle) — the "permanent" filter passes
            // anything under Permanent.
            CandidateGatherer: ctx => owner.Zones.Battlefield.GetCards()
                .OfType<Permanent>()
                .Where(p => !ReferenceEquals(p, card))
                .Where(p => ReferenceEquals(p.Controller, owner))
                .Cast<object>()
                .ToList());

        var etbEffect = new Effect(
            $"{CardName} — exile another target permanent you control, then return it",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return; // "may" → declined

                if (chosen[0][0] is not Permanent target) return;
                // "another" — cannot target Felidar Guardian itself.
                if (ReferenceEquals(target, card)) return;
                // CR 608.2b — resolution-time legality re-check.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!ReferenceEquals(target.Controller, owner)) return;

                // CR 701.21 — Exile. Prefer ZoneService when supplied.
                if (zones != null)
                {
                    zones.MoveCard(target, ZoneType.Battlefield, ZoneType.Exile);
                }
                else
                {
                    var fromOwner = target.Owner ?? owner;
                    fromOwner.Zones.Battlefield.RemoveCard(target);
                    fromOwner.Zones.Exile.AddCard(target);
                    target.SetZone(ZoneType.Exile);
                }

                // CR 614 — "return that card to the battlefield under
                // your control". CR 111.8 — token guard.
                if (target.Zone != ZoneType.Exile) return;

                if (zones != null)
                {
                    zones.MoveCard(target, ZoneType.Exile, ZoneType.Battlefield, owner);
                }
                else
                {
                    var returnOwner = target.Owner ?? owner;
                    returnOwner.Zones.Exile.RemoveCard(target);
                    owner.Zones.Battlefield.AddCard(target);
                    target.SetZone(ZoneType.Battlefield);
                    target.SetController(owner);
                }
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[] { targetRequest });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
