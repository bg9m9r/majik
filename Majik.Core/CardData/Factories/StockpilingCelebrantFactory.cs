using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stockpiling Celebrant (Outlaws of Thunder Junction,
/// Creature — Dwarf Knight {2}{W} 3/2).
///
/// Oracle text (verified against Scryfall):
///   "When this creature enters, you may return another target nonland
///    permanent you control to its owner's hand. If you do, scry 2."
///
/// ## Card identity comes from JSON
/// Name / Creature / Dwarf + Knight subtypes, {2}{W}, 3/2 are materialised
/// from the embedded JSON definition (<c>stockpiling-celebrant.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (same posture as
/// <see cref="KorSkyfisherFactory"/>). The ETB trigger is layered on here —
/// the JSON <c>AbilityDefinition</c> schema doesn't express this
/// optional-bounce-then-conditional-scry shape.
///
/// ## Implemented
/// - 3/2 Creature — Dwarf Knight, mana cost {2}{W}, owner/controller wired.
/// - <b>ETB triggered ability</b> (CR 603.6a) fired when this creature enters.
///   - "you may return ..." — OPTIONAL (CR 603.5). The target request is
///     0..1 (MinTargets=0) so the controller may decline.
///   - Target: "another target nonland permanent you control".
///     - "you control" — only the controller's own battlefield permanents
///       (CR 109.5). Same self-bounce posture as
///       <see cref="KorSkyfisherFactory"/>, but here it is a true TARGET.
///     - "another" — excludes the Celebrant itself (CR 115.5b).
///     - "nonland" — lands are not legal targets (CR 305).
///   - On resolution: if a (still-legal) permanent was chosen, it is returned
///     to its owner's hand (CR 701.10), then — and only then — the controller
///     scrys 2 (CR 701.20). "If you do" gates the scry on the bounce actually
///     happening (CR 603.2c — intervening "if you do" clause).
///   - CR 608.2b: if the chosen target is no longer a legal "nonland permanent
///     you control" at resolution, the bounce does nothing and (per "if you
///     do") the scry does not happen.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape + ETB trigger attached for
///   shape inspection; no ZoneService wiring (raw zone-move fallback),
///   scry consults <see cref="AgentRegistry"/> off the resolution context.
///   Suitable for shape tests and the <see cref="NamedCardFactory"/> dispatcher.
/// - <see cref="Create(Player, ZoneService, TriggerManager)"/> — full wiring:
///   ZoneService routes the bounce (replacement bus fires, CardMovedEvent
///   published for downstream triggers) and the TriggerManager evaluates the
///   ETB trigger so it fires automatically when the card enters.
/// </summary>
[CardName("Stockpiling Celebrant")]
public static class StockpilingCelebrantFactory
{
    public const string CardName = "Stockpiling Celebrant";
    public const string Slug = "stockpiling-celebrant";
    public const int Power = 3;
    public const int Toughness = 2;
    private const int ScryAmount = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Stockpiling Celebrant with the ETB trigger attached for shape
    /// inspection. No ZoneService wiring — bounce uses a raw zone move.
    /// Suitable for shape tests and the <see cref="NamedCardFactory"/> dispatcher.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, zoneService: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Stockpiling Celebrant.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Zone service for replacement-bus-aware moves.
    /// May be null — raw zone move is used as fallback.</param>
    /// <param name="triggers">TriggerManager to register the ETB trigger
    /// against. May be null — trigger is attached to the card shape only.</param>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Dwarf +
        // Knight subtypes, {2}{W}, 3/2). The JSON carries no abilities — the
        // ETB bounce-then-scry trigger is layered on below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // --------------------------------------------------------------------
        // ETB triggered ability (CR 603.6a).
        //   "When this creature enters, you may return another target nonland
        //    permanent you control to its owner's hand. If you do, scry 2."
        // --------------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: may return another nonland permanent you control; if you do, scry 2",
            async ctx =>
            {
                if (etbTrigger == null) return;

                var controller = card.Controller ?? owner;

                // CR 603.5 — "you may". With no chosen target the optional
                // bounce was declined; the scry ("if you do") does not happen.
                var bounced = TryBounceChosenTarget(etbTrigger, card, zoneService);
                if (!bounced) return;

                // CR 603.2c — "If you do, scry 2." gated on the bounce.
                await ScryTwoAsync(controller, ctx).ConfigureAwait(false);
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
                // "you may return another target nonland permanent you control"
                // MinTargets=0 — optional (CR 603.5): controller may decline.
                new TargetRequest(
                    Description: "another target nonland permanent you control",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Bounce,
                    // "nonland permanent you control", excluding the Celebrant
                    // itself ("another", CR 115.5b).
                    CandidateGatherer: ctx => (ctx.AllPlayers
                            .FirstOrDefault(p => ReferenceEquals(p, card.Controller ?? owner))
                            ?.Zones.Battlefield.GetCards() ?? Enumerable.Empty<Card>())
                        .OfType<Permanent>()
                        .Where(p => !p.HasType(CardType.Land))
                        .Where(p => !ReferenceEquals(p, card))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// Return the chosen target ("another nonland permanent you control") to
    /// its owner's hand (CR 701.10). Returns <c>true</c> iff a permanent was
    /// actually bounced (drives the "If you do" scry gate).
    ///
    /// CR 608.2b — resolution-time legality re-checks ("another", "nonland",
    /// still on the battlefield); an illegal/absent target is a no-op and
    /// returns <c>false</c> (so the scry does not happen).
    /// </summary>
    private static bool TryBounceChosenTarget(
        TriggeredAbility trigger,
        Creature source,
        ZoneService? zoneService)
    {
        var chosen = trigger.ChosenTargets;
        // CR 603.5 — optional: no target chosen (declined) → no bounce.
        if (chosen.Count == 0 || chosen[0].Count == 0) return false;

        if (chosen[0][0] is not Permanent target) return false;

        // CR 115.5b / CR 305 — "another" + "nonland" re-checked at resolution.
        if (ReferenceEquals(target, source)) return false;
        if (target.HasType(CardType.Land)) return false;

        // CR 608.2b — target must still be on the battlefield.
        if (target.Zone != ZoneType.Battlefield) return false;

        var targetOwner = target.Owner;
        if (targetOwner == null) return false;

        // CR 701.10 — return to owner's hand.
        if (zoneService != null)
        {
            // Full path: replacement bus fires, CardMovedEvent published.
            zoneService.MoveCard(target, ZoneType.Battlefield, ZoneType.Hand);
        }
        else
        {
            // Raw fallback: direct zone manipulation (shape tests / dispatcher
            // path with no ZoneService).
            var fromController = target.Controller ?? targetOwner;
            fromController.Zones.Battlefield.RemoveCard(target);
            targetOwner.Zones.Hand.AddCard(target);
            target.SetZone(ZoneType.Hand);
            target.SetController(targetOwner);
        }

        return true;
    }

    /// <summary>
    /// Scry 2 (CR 701.20). Consults the controller's agent for the bottom/top
    /// partition; falls back to all-to-bottom when no agent is registered
    /// (identical posture to <see cref="CharmingPrinceFactory"/>'s scry mode).
    /// </summary>
    private static async ValueTask ScryTwoAsync(Player controller, ResolutionContext ctx)
    {
        var peeked = ScryAction.Peek(controller, ScryAmount);
        if (peeked.Count == 0) return;

        var agent = ctx.Agent ?? AgentRegistry.Get(controller);
        ScryAction.ScryDecision decision;
        if (agent != null)
        {
            decision = await agent.ChooseScryDecisionAsync(ctx.Game, peeked)
                .ConfigureAwait(false);
        }
        else
        {
            // Pre-agent default: all peeked cards to bottom.
            decision = new ScryAction.ScryDecision(
                ToBottom: peeked.ToList(),
                TopOrder: Array.Empty<ICard>());
        }

        ScryAction.Apply(controller, peeked.Count, decision);
    }
}
