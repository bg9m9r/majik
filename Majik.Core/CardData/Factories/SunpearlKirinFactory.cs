using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sunpearl Kirin (Bloomburrow, {1}{W}).
///
/// Creature — Kirin 2/1. Oracle text (verified against Scryfall):
///   "Flash
///    Flying
///    When this creature enters, return up to one other target nonland
///    permanent you control to its owner's hand. If it was a token, draw
///    a card."
///
/// ## Shape source
/// Card identity (name, {1}{W}, 2/1, Creature — Kirin) is loaded from
/// <c>Majik.Core/CardData/Cards/sunpearl-kirin.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> — same posture as
/// <see cref="FaerieSeerFactory"/>. Flash + Flying keyword markers and the ETB
/// triggered ability are attached in code below (the JSON ability schema does
/// not yet express keyword markers, a self-bounce target filter, or the
/// token-leaves draw rider).
///
/// ## Implemented (v1)
/// - 2/1 Creature — Kirin (CR 205.3m) at {1}{W}. Color identity white.
/// - <b>Flash</b> (CR 702.8) + <b>Flying</b> (CR 702.9): <see cref="KeywordAbility"/>
///   markers — same wire-up shape as <see cref="RestorationAngelFactory"/>.
/// - <b>ETB triggered ability</b> (CR 603.6a): single 0..1
///   <see cref="TargetRequest"/> "another target nonland permanent you
///   control" with a live <c>CandidateGatherer</c> that walks the controller's
///   battlefield for <see cref="Permanent"/>s that are NOT lands (CR 305) and
///   NOT Sunpearl Kirin itself ("other", CR 109.5). The 0..1 request models the
///   "up to one" rider — selecting zero targets is the printed "you may not"
///   branch (mirrors <see cref="RestorationAngelFactory"/>'s ETB shape).
///   <see cref="BotIntent.Bounce"/> — the dominant use is re-buying a token
///   (re-triggering an ETB on a real card or cashing a token for a card).
///   On resolve: re-checks the target is still a controller-side battlefield
///   nonland permanent other than this Kirin (CR 608.2b — illegal target → no
///   effect). The target is returned to its owner's hand via
///   <see cref="Fx.BounceToHand(ICard, ZoneService?)"/> (CR 701.20). The
///   token-ness is captured BEFORE the bounce (CR 603.10 last-known-information
///   — a token that left the battlefield ceases to exist via SBA 704.5d, so the
///   "was it a token" test reads its pre-bounce state); if it was a token the
///   Kirin's controller draws a card (CR 701.20 via <see cref="Fx.DrawCards"/>).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The ETB trigger is attached for
///   shape inspection; not registered with a <see cref="TriggerManager"/>.
/// - <see cref="Create(Player, TriggerManager?, ZoneService?)"/> — fully wired.
/// </summary>
[CardName("Sunpearl Kirin")]
public static class SunpearlKirinFactory
{
    public const string CardName = "Sunpearl Kirin";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("sunpearl-kirin");

    /// <summary>
    /// Construct Sunpearl Kirin with no live wiring. The ETB trigger is
    /// attached to the card for shape inspection; not registered with any
    /// <see cref="TriggerManager"/>.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null, zones: null);

    /// <summary>
    /// Construct Sunpearl Kirin with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / controller.</param>
    /// <param name="triggers">Manager that registers the ETB trigger so a
    /// qualifying <c>CardMovedEvent</c> queues the ability end-to-end.</param>
    /// <param name="zones">Routes the bounce zone move so <c>CardMovedEvent</c>
    /// fires for downstream listeners; null → raw-zone fallback.</param>
    public static Creature Create(Player owner, TriggerManager? triggers, ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 / CR 702.9 — Flash + Flying keyword markers.
        card.AddAbility(new KeywordAbility("Flash", card, owner));
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.20.
        //   "When this creature enters, return up to one other target
        //    nonland permanent you control to its owner's hand. If it was
        //    a token, draw a card."
        //
        // 0..1 TargetRequest models the "up to one" rider — selecting zero
        // is the "you may not" branch (mirrors RestorationAngel's ETB shape).
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var targetRequest = new TargetRequest(
            Description: "up to one other target nonland permanent you control",
            MinTargets: 0,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.Bounce,
            // CR 109.5 / CR 305 / CR 608.2b — controller-scoped gather,
            // "other" drops the Kirin itself, nonland filter drops lands.
            CandidateGatherer: ctx => owner.Zones.Battlefield.GetCards()
                .OfType<Permanent>()
                .Where(p => !ReferenceEquals(p, card))
                .Where(p => !p.HasType(CardType.Land))
                .Where(p => ReferenceEquals(p.Controller, owner))
                .Cast<object>()
                .ToList());

        var etbEffect = new Effect(
            $"{CardName} — return up to one other target nonland permanent you control to its owner's hand; if it was a token, draw a card",
            () => ResolveBounceTrigger(etbTrigger, card, owner, zones));

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

    // --- Bounce resolve (CR 608.2b / 701.20 / 603.10) --------------------

    private static void ResolveBounceTrigger(
        TriggeredAbility? etbTrigger,
        Creature card,
        Player owner,
        ZoneService? zones)
    {
        var target = ResolveLegalBounceTarget(etbTrigger, card, owner);
        if (target == null) return;

        // CR 603.10 — capture the token-ness BEFORE the bounce: a token that
        // leaves the battlefield ceases to exist (SBA 704.5d), so the rider
        // reads the permanent's last-known information from the battlefield.
        var wasToken = target.IsToken;

        // CR 701.20 — return to its owner's hand. ZoneService-routed when
        // supplied so CardMovedEvent fires (LTB listeners / replacements).
        Fx.BounceToHand(target, zones);

        // "If it was a token, draw a card." — the Kirin's controller.
        if (wasToken)
        {
            var controller = card.Controller ?? owner;
            Fx.DrawCards(controller, 1);
        }
    }

    private static Permanent? ResolveLegalBounceTarget(
        TriggeredAbility? etbTrigger,
        Creature card,
        Player owner)
    {
        if (etbTrigger == null) return null;
        var chosen = etbTrigger.ChosenTargets;
        if (chosen.Count == 0 || chosen[0].Count == 0) return null; // "up to one" → declined
        if (chosen[0][0] is not Permanent target) return null;

        // "other" — cannot target Sunpearl Kirin itself (CR 109.5).
        if (ReferenceEquals(target, card)) return null;
        // CR 608.2b — resolution-time legality re-check.
        if (target.Zone != ZoneType.Battlefield) return null;
        if (!ReferenceEquals(target.Controller, owner)) return null;
        if (target.HasType(CardType.Land)) return null; // "nonland" (CR 305)
        return target;
    }
}
