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
/// Named-card factory for Restoration Angel (Avacyn Restored, {3}{W}).
///
/// Creature — Angel 3/4. Oracle text:
///   "Flash
///    Flying
///    When Restoration Angel enters, you may exile another target
///    non-Angel creature you control, then return that card to the
///    battlefield under your control."
///
/// CR 701.21 (Exile) + CR 614 (return to the battlefield) — Restoration
/// Angel is the canonical "flash + flicker" engine: Modern's Kiki-Jiki
/// combo half, and the value piece that re-triggers blink-friendly ETBs
/// (Snapcaster Mage / Thragtusk / Wall of Omens). The flicker half is
/// shape-identical to <see cref="CloudshiftFactory"/>'s body but is
/// gated by:
///   (a) "another" — cannot target Restoration Angel itself (CR 109.5);
///   (b) "non-Angel" — target's <see cref="CardSubtype"/> set must not
///       include <see cref="CardSubtype.Angel"/> (CR 205.3m); and
///   (c) "you control" — controller-scoped candidate gather, with the
///       resolution-time legality re-check.
///
/// "may" — the trigger's controller chooses on resolve whether to
/// exile/return at all. v1 ships this as a 0..1 <see cref="TargetRequest"/>
/// — selecting zero targets is the printed "you may not" branch, matching
/// the "up to one" idiom used by <see cref="SolitudeFactory"/>'s ETB exile
/// trigger.
///
/// ## Implemented (v1)
/// - 3/4 Creature — Angel at {3}{W}; owner + controller seated.
/// - Flash + Flying keyword markers (CR 702.8 / CR 702.9).
/// - <b>ETB triggered ability</b> (CR 603.6a): single 0..1
///   <see cref="TargetRequest"/> "another target non-Angel creature you
///   control" with a live <c>CandidateGatherer</c> that walks the
///   controller's battlefield for Creature permanents other than
///   Restoration Angel itself, filtering out anything with the Angel
///   subtype. <see cref="BotIntent.Protection"/> — the dominant use of
///   Restoration Angel is dodging removal or re-triggering an ETB.
///   On resolve: re-checks the target is still a controller-side
///   battlefield non-Angel Creature (CR 608.2b — illegal target → no
///   effect). Exile via owner-routed zone moves (CR 701.21), then
///   immediately return under the original caster's control (CR 614).
///   The return is "to the battlefield under your control" — distinct
///   from Cloudshift's "owner's control"; "you" here is Restoration
///   Angel's controller, which equals the target's owner under normal
///   play (the target was "you control"). When a <see cref="ZoneService"/>
///   is supplied, both halves route through it so
///   <see cref="CardMovedEvent"/> publishes and downstream ETB listeners
///   fire on the re-entry (matches <see cref="TouchTheSpiritRealmFactory"/>
///   / <see cref="YorionSkyNomadFactory"/> two-mode posture).
///
/// ## Deferred (v1 gaps)
/// - <b>Kiki-Jiki / token copies</b> (CR 701.21 + CR 111.8): a token
///   Restoration Angel exiled by another Resto / Cloudshift evaporates
///   in exile. The flicker body defensively guards on
///   <c>Zone == ZoneType.Exile</c> before the return so vanished tokens
///   are skipped. Same posture as Cloudshift / Ephemerate.
/// - <b>True new-object semantics</b> (CR 400.7): v1 returns the same
///   <see cref="Permanent"/> instance — the engine doesn't yet mint a
///   fresh object on flicker, so "until end of turn" pumps on the
///   exiled creature persist through the return. Tracked alongside
///   Cloudshift / Ephemerate / Ocelot Pride as a shared "flicker
///   new-object" primitive deferred.
/// - <b>Synchronous "may" prompt</b>: agent surface treats 0..1 as the
///   may rider — bot rankers can return zero candidates to decline.
///   No separate Yes/No prompt is built (mirrors Solitude's "up to one"
///   pattern).
/// </summary>
[CardName("Restoration Angel")]
public static class RestorationAngelFactory
{
    public const string CardName = "Restoration Angel";
    public const string PrintedManaCost = "{3}{W}";
    public const int Power = 3;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Restoration Angel with no live runtime services. Flash +
    /// Flying keyword markers and the ETB trigger are attached to the
    /// card shape for structural / dispatch tests; the trigger is not
    /// registered against a <see cref="TriggerManager"/>.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zones: null);

    /// <summary>
    /// Construct Restoration Angel with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / controller.</param>
    /// <param name="triggers">Manager that registers the ETB trigger
    /// so a qualifying <see cref="CardMovedEvent"/> queues the ability
    /// end-to-end.</param>
    /// <param name="zones">Used to route the exile + return zone moves
    /// so <see cref="CardMovedEvent"/> fires for downstream listeners.
    /// Null → falls back to direct owner-routed zone mutation
    /// (matches the Cloudshift posture for shape tests).</param>
    public static Creature Create(Player owner, TriggerManager? triggers, ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Angel });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 / CR 702.9 — Flash + Flying keyword markers.
        card.AddAbility(new KeywordAbility("Flash", card, owner));
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.21 / CR 614.
        //   "When Restoration Angel enters, you may exile another target
        //    non-Angel creature you control, then return that card to
        //    the battlefield under your control."
        //
        // 0..1 TargetRequest models the "may" rider — selecting zero is
        // the "you may not" branch (mirrors SolitudeFactory's ETB shape).
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var targetRequest = new TargetRequest(
            Description: "another target non-Angel creature you control",
            MinTargets: 0,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.Protection,
            // CR 109.5 / CR 608.2b — controller-scoped gather, "another"
            // rider drops Restoration Angel itself, non-Angel filter
            // drops every other Angel on Alice's side too.
            CandidateGatherer: ctx => owner.Zones.Battlefield.GetCards()
                .OfType<Creature>()
                .Where(c => !ReferenceEquals(c, card))
                .Where(c => !c.HasSubtype(CardSubtype.Angel))
                .Where(c => ReferenceEquals(c.Controller, owner))
                .Cast<object>()
                .ToList());

        var etbEffect = new Effect(
            $"{CardName} — exile another target non-Angel creature you control, then return it",
            () => ResolveFlickerTrigger(etbTrigger, card, owner, zones));

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

    // --- Flicker resolve (CR 608.2b / 701.21 / 614 / 111.8) ---------------

    private static void ResolveFlickerTrigger(
        TriggeredAbility? etbTrigger,
        Creature card,
        Player owner,
        ZoneService? zones)
    {
        var target = ResolveLegalFlickerTarget(etbTrigger, card, owner);
        if (target == null) return;

        // CR 701.21 — Exile. Prefer ZoneService when supplied so
        // CardMovedEvent fires (TouchTheSpiritRealm / Yorion posture).
        ExileTarget(target, owner, zones);

        // CR 614 — "return that card to the battlefield under your
        // control". CR 111.8 token guard: a token in exile has already
        // been removed by SBAs in production; Zone == Exile check skips
        // it cleanly.
        if (target.Zone != ZoneType.Exile) return;
        ReturnTarget(target, owner, zones);
    }

    private static Creature? ResolveLegalFlickerTarget(
        TriggeredAbility? etbTrigger,
        Creature card,
        Player owner)
    {
        if (etbTrigger == null) return null;
        var chosen = etbTrigger.ChosenTargets;
        if (chosen.Count == 0 || chosen[0].Count == 0) return null; // "may" → declined
        if (chosen[0][0] is not Creature target) return null;

        // "another" — cannot target Restoration Angel itself.
        if (ReferenceEquals(target, card)) return null;
        // CR 608.2b — resolution-time legality re-check.
        if (target.Zone != ZoneType.Battlefield) return null;
        if (!ReferenceEquals(target.Controller, owner)) return null;
        if (target.HasSubtype(CardSubtype.Angel)) return null;
        return target;
    }

    private static void ExileTarget(Creature target, Player owner, ZoneService? zones)
    {
        if (zones != null)
        {
            zones.MoveCard(target, ZoneType.Battlefield, ZoneType.Exile);
            return;
        }
        var fromOwner = target.Owner ?? owner;
        fromOwner.Zones.Battlefield.RemoveCard(target);
        fromOwner.Zones.Exile.AddCard(target);
        target.SetZone(ZoneType.Exile);
    }

    private static void ReturnTarget(Creature target, Player owner, ZoneService? zones)
    {
        if (zones != null)
        {
            zones.MoveCard(target, ZoneType.Exile, ZoneType.Battlefield, owner);
            return;
        }
        var returnOwner = target.Owner ?? owner;
        returnOwner.Zones.Exile.RemoveCard(target);
        owner.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);
        target.SetController(owner);
    }
}
