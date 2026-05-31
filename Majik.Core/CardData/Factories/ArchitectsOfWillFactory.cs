using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Architects of Will (Alara Reborn, {2}{U}{B}).
///
/// Artifact Creature — Human Wizard 3/3. Oracle text (Scryfall):
///   "When this creature enters, look at the top three cards of target
///    player's library, then put them back in any order.
///    Cycling {U/B} ({U/B}, Discard this card: Draw a card.)"
///
/// ## Implemented (v1)
///
/// - <b>Artifact Creature — Human Wizard {2}{U}{B} 3/3</b>. Base
///   <see cref="Creature"/> constructor registers
///   <see cref="CardType.Creature"/>; <see cref="CardType.Artifact"/> is
///   additively flagged via <see cref="Card.AddCardType"/> for the
///   printed "Artifact Creature" type-line (CR 205.2a — mirrors
///   <see cref="ShardlessAgentFactory"/> / <see cref="ArcboundRavagerFactory"/>).
///
/// - <b>ETB trigger</b> (CR 603.6a): "When this creature enters, look at
///   the top three cards of target player's library, then put them back
///   in any order." Wired via <see cref="Triggers.OnEnterBattlefieldSelf"/>
///   with one 1..1 "target player" <see cref="Targeting.TargetRequest"/>.
///   On resolve the controller's <see cref="IPlayerAgent"/> partitions
///   the peeked top three via the standard <see cref="ScryAction"/>
///   pipeline — Architects of Will is "reorder-only" (no put-to-bottom
///   option in the current oracle), so any agent-supplied
///   <see cref="ScryAction.ScryDecision.ToBottom"/> entries are collapsed
///   into the top-order tail (preserves relative ordering) before the
///   ScryAction.Apply call. With no agent registered, the peeked cards
///   are returned in their original order (pre-agent legacy fallback —
///   same shape as <see cref="PonderFactory"/>'s default-keep posture).
///
/// - <b>Cycling {U/B}</b> (CR 702.32) — routed through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>("{U/B}"). The primitive attaches the
///   <see cref="ActivatedAbility"/> + a <see cref="KeywordAbility"/>
///   "Cycling" marker, layers the <see cref="DiscardSelfCost"/>
///   hand-zone gate (CR 702.32a) onto the cost stack, and on resolve
///   publishes <see cref="CardCycledEvent"/> for any "Whenever a player
///   cycles" triggers (CR 702.32d — Lightning Rift, Living End cascade
///   shells, Curator of Mysteries scry-1).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. ETB trigger
///   attached for shape inspection; cycling activated ability attached
///   with no event bus (no CardCycledEvent publication).
/// - <see cref="Create(Player, TriggerManager?, IEventBus?)"/> — fully
///   wired. ETB trigger registered for bus-driven firing; cycling
///   resolve publishes <see cref="CardCycledEvent"/> against the bus.
///
/// CR rule references: 205.2a (Artifact + Creature multi-type),
/// 205.3m (Human / Wizard subtypes), 603.6a (ETB), 701.20 (Scry
/// reorder pipeline reused for the look-and-put-back), 702.32 (Cycling).
/// </summary>
[CardName("Architects of Will")]
public static class ArchitectsOfWillFactory
{
    public const string CardName = "Architects of Will";
    public const string PrintedManaCost = "{2}{U}{B}";
    public const int Power = 3;
    public const int Toughness = 3;
    public const int LookAtCount = 3;
    public const string CyclingCost = "{U/B}";

    /// <summary>
    /// Construct Architects of Will with no live ZoneService / TriggerManager
    /// wiring. ETB trigger is attached for shape inspection; cycling
    /// ability attached without an event bus (shape-only — no
    /// <see cref="CardCycledEvent"/> publication).
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null);

    /// <summary>
    /// Construct Architects of Will. When <paramref name="triggers"/> is
    /// supplied the ETB trigger is registered so a self-enter
    /// <see cref="CardMovedEvent"/> queues the look-and-reorder body.
    /// When <paramref name="eventBus"/> is supplied the cycling resolve
    /// body publishes <see cref="CardCycledEvent"/> so CR 702.32d
    /// "Whenever a player cycles" triggers fire.
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 205.2a — Artifact Creature multi-type. Base Creature ctor
        // only registers CardType.Creature; flag Artifact additively.
        card.AddCardType(CardType.Artifact);

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.6a.
        //   "When this creature enters, look at the top three cards of
        //    target player's library, then put them back in any order."
        // Single 1..1 "target player" TargetRequest; on resolve the
        // controller's agent partitions the peeked top three via the
        // standard ScryAction pipeline (reorder-only — any ToBottom
        // entries are collapsed into the top-order tail to keep
        // everything on top).
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var etbEffect = new Effect(
            $"{CardName}: look at top {LookAtCount}, put back in any order",
            async ctx =>
            {
                if (etbTrigger is null) return;
                if (etbTrigger.ChosenTargets.Count == 0
                    || etbTrigger.ChosenTargets[0].Count == 0)
                {
                    // CR 608.2b — no legal target, do nothing.
                    return;
                }

                if (etbTrigger.ChosenTargets[0][0] is not Player target)
                {
                    return;
                }

                var peeked = ScryAction.Peek(target, LookAtCount);
                if (peeked.Count == 0) return;

                var controller = card.Controller ?? owner;
                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                ScryAction.ScryDecision decision;
                if (agent != null)
                {
                    // TODO: drop sync-over-async once IEffect.Execute becomes async.
                    var agentDecision = (await agent.ChooseScryDecisionAsync( ctx.Game, peeked).ConfigureAwait(false));
                    // Architects of Will is reorder-only — current oracle
                    // does not include "you may put them on the bottom".
                    // Collapse any ToBottom entries into the TopOrder
                    // tail so everything stays on top, preserving the
                    // agent's relative ordering. Same defensive collapse
                    // PonderFactory uses.
                    if (agentDecision.ToBottom.Count > 0)
                    {
                        var collapsed = agentDecision.TopOrder
                            .Concat(agentDecision.ToBottom)
                            .ToList();
                        decision = new ScryAction.ScryDecision(
                            ToBottom: Array.Empty<ICard>(),
                            TopOrder: collapsed);
                    }
                    else
                    {
                        decision = agentDecision;
                    }
                }
                else
                {
                    // No agent: keep original order on top (pre-agent
                    // legacy posture — matches PonderFactory).
                    decision = new ScryAction.ScryDecision(
                        ToBottom: Array.Empty<ICard>(),
                        TopOrder: peeked.ToList());
                }

                ScryAction.Apply(target, peeked.Count, decision);
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.LibraryReorder),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Cycling {U/B} — CR 702.32. Routed through the shared
        // CyclingFactory primitive; the primitive appends the
        // DiscardSelfCost hand-zone gate (CR 702.32a) and the
        // CardCycledEvent publish (CR 702.32d) automatically.
        // ----------------------------------------------------------------
        CyclingFactory.Build(card, new ManaCostCost(CyclingCost), eventBus);

        return card;
    }
}
