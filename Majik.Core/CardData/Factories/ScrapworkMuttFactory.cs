using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Scrapwork Mutt (The Brothers' War,
/// Artifact Creature — Dog {2}).
///
/// Oracle text (Scryfall verified):
///   "When this creature enters, you may discard a card. If you do, draw a card.
///    Unearth {1}{R} ({1}{R}: Return this card from your graveyard to the
///    battlefield. It gains haste. Exile it at the beginning of the next end
///    step or if it would leave the battlefield. Unearth only as a sorcery.)"
///
/// ## Base shape
/// Name / Creature+Artifact / Dog / {2} / 2/1 are materialised from the
/// embedded JSON definition (<c>scrapwork-mutt.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same JSON-backed posture as
/// <see cref="KroxaTitanFactory"/>. The two printed behaviours (ETB loot +
/// Unearth) are layered on here because the JSON ability schema doesn't yet
/// express them.
///
/// ## Implemented (v1)
/// - <b>2/1 Artifact Creature — Dog</b>, mana cost {2}, owner / controller
///   wired. (CR 301.1 / CR 302.1 — Artifact + Creature card type, expressed
///   directly in the JSON <c>types</c> array, same as Adaptive Automaton.)
/// - <b>ETB loot (CR 603.1 / CR 117.x "may" / CR 701.16 discard /
///   CR 121.1 draw)</b>: "When this creature enters, you may discard a card.
///   If you do, draw a card." A <see cref="TriggeredAbility"/> keyed on
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>. The "you may" is gated
///   on the controller's <see cref="IPlayerAgent.ChooseYesNoAsync"/>
///   (<see cref="BotIntent.Discard"/> | <see cref="BotIntent.Draw"/>);
///   agent-less callers default to YES when the hand is non-empty (looting
///   away one card for a fresh draw is strictly card-neutral and usually an
///   upside). Discard pick uses the same agent-or-fallback policy as
///   <see cref="FaithlessLootingFactory"/> / <see cref="TormentingVoiceFactory"/>:
///   <see cref="IPlayerAgent.ChooseFromHandAsync"/> with
///   <see cref="BotIntent.Discard"/>, last-card-in-hand fallback. The draw
///   happens IFF a card was actually discarded ("If you do" — CR 117.x); an
///   empty hand → no discard → no draw. Empty library mid-draw flags the SBA
///   loss (CR 704.5b).
/// - <b>Unearth {1}{R} (CR 702.84)</b>: a graveyard-activated, sorcery-speed
///   <see cref="ActivatedAbility"/> with a {1}{R} <see cref="ManaCostCost"/>.
///   On resolution it returns this card from its owner's graveyard to the
///   battlefield (routing through <see cref="ZoneService.MoveCard"/> when
///   supplied so ETB triggers fire — CR 603.6a), grants Haste, clears
///   summoning sickness, and registers a one-shot
///   <see cref="DelayedTriggeredAbility"/> (CR 603.7) that <b>exiles</b> the
///   creature at the beginning of the next end step. Same unearth shape as
///   <see cref="HellsparkElementalFactory"/>.
///
/// ## Deferred (v1 gaps — mirror the existing unearth-style factories)
/// - <b>Zone-scoped activation</b>: the engine does not yet gate activated
///   abilities on source zone (CR 113.6). The unearth ability is enumerable
///   from any zone; the resolution body guards <c>card.Zone == Graveyard</c>
///   so spurious activations are no-op-shaped (same caveat as
///   <see cref="HellsparkElementalFactory"/> / Priest of Fell Rites).
/// - <b>"…or if it would leave the battlefield" exile rider</b>: the
///   "exile at the next end step" half of unearth (CR 702.84c) is wired via
///   the delayed end-step trigger. The "or if it would leave the
///   battlefield" half is a replacement-style effect (CR 614) the engine
///   does not yet expose for graveyard-origin permanents — recorded as a
///   deferral, same as <see cref="HellsparkElementalFactory"/>.
/// - <b>Discard-pick prompt UI</b>: v1 is agent-driven when supplied, else
///   last-card-in-hand — same gap as Faithless Looting / Tormenting Voice.
/// </summary>
[CardName("Scrapwork Mutt")]
public static class ScrapworkMuttFactory
{
    public const string CardName = "Scrapwork Mutt";
    public const string Slug = "scrapwork-mutt";

    /// <summary>Unearth activation cost. CR 702.84.</summary>
    public const string UnearthCost = "{1}{R}";

    /// <summary>Keyword granted on unearth. CR 702.10.</summary>
    public const string Haste = "Haste";

    /// <summary>
    /// Construct Scrapwork Mutt with no live runtime services. The ETB loot
    /// trigger and the Unearth activated ability are attached for shape
    /// inspection (not registered with a <see cref="TriggerManager"/>); the
    /// ETB loot uses raw zone moves and no delayed exile trigger is
    /// registered. Suitable for shape / dispatcher tests. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null, agent: null);

    /// <summary>
    /// Construct a fully-wired Scrapwork Mutt. When <paramref name="triggers"/>
    /// is supplied the ETB loot trigger is registered, and each unearth
    /// activation registers its own one-shot delayed end-step <b>exile</b>
    /// trigger (CR 603.7 / 702.84c). When <paramref name="zoneService"/> is
    /// supplied the unearth return routes through
    /// <see cref="ZoneService.MoveCard"/> so ETB triggers fire (CR 603.6a).
    /// When <paramref name="agent"/> is supplied it drives the "you may"
    /// confirm + the discard pick.
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers,
        IPlayerAgent? agent)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature +
        // Artifact, Dog, {2}, 2/1). No abilities in the JSON — the two
        // printed behaviours are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // "When this creature enters, you may discard a card. If you do,
        // draw a card." CR 603.1 self-ETB trigger. CR 117.x — the "may"
        // is the controller's choice; the draw is gated on the discard
        // actually happening ("If you do").
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: you may discard a card; if you do, draw a card",
            ctx => ResolveLootAsync(card, owner, agent, ctx));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Unearth {1}{R} — CR 702.84. Graveyard-activated, sorcery-speed
        // activated ability. Returns this card from graveyard → battlefield,
        // grants Haste, and registers a delayed end-step EXILE (CR 702.84c).
        // Same shape as Hellspark Elemental.
        // ----------------------------------------------------------------
        var unearthEffect = new Effect(
            $"{CardName}: unearth — return from graveyard, gain haste, exile next end step",
            () => ResolveUnearth(card, owner, zoneService, triggers));

        var unearthAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(UnearthCost) },
            effects: new IEffect[] { unearthEffect },
            // CR 702.84a — "Unearth only as a sorcery." ActionValidator gates
            // activation on the controller's main phase + empty stack.
            sorcerySpeed: true);

        card.AddAbility(unearthAbility);

        return card;
    }

    /// <summary>
    /// CR 603.1 ETB loot — "you may discard a card. If you do, draw a card."
    /// The controller chooses whether to loot (agent-driven; agent-less
    /// defaults to YES while the hand is non-empty — card-neutral upside).
    /// Discard pick mirrors Faithless Looting / Tormenting Voice (agent
    /// ChooseFromHandAsync, last-card fallback). The draw is gated on the
    /// discard actually occurring; an empty hand → no loot.
    /// </summary>
    private static async ValueTask ResolveLootAsync(Creature card, Player owner, IPlayerAgent? agent, ResolutionContext ctx)
    {
        var controller = card.Controller ?? owner;
        agent = ctx.Agent ?? agent ?? AgentRegistry.Get(controller);

        var hand = controller.Zones.Hand.GetCards().ToList();
        if (hand.Count == 0) return; // "you may discard" — nothing to discard.

        // "You may" — CR 117.x. Agent-less defaults to YES: discard-then-draw
        // is card-neutral (net 0 hand size) and digs one card deep, so the
        // upside branch is the deterministic v1 default.
        bool wantsToLoot = agent == null
            || await agent.ChooseYesNoAsync(
                    $"{CardName}: discard a card to draw a card?",
                    BotIntent.Discard | BotIntent.Draw)
                .ConfigureAwait(false);

        if (!wantsToLoot) return;

        // CR 701.16 — discard a card. Agent path: ChooseFromHandAsync with
        // BotIntent.Discard; null / off-hand pick falls back to last card.
        ICard? pick;
        if (agent != null)
        {
            pick = await agent.ChooseFromHandAsync(controller, hand, BotIntent.Discard)
                .ConfigureAwait(false);
            if (pick == null || pick.Zone != ZoneType.Hand)
                pick = hand[^1];
        }
        else
        {
            pick = hand[^1];
        }

        controller.Zones.Hand.RemoveCard(pick);
        controller.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);

        // "If you do, draw a card." CR 121.1 — the draw is conditioned on the
        // discard above having happened (it did). Empty library mid-draw
        // flags the SBA loss (CR 704.5b) and short-circuits.
        var top = controller.Zones.Library.GetCards().FirstOrDefault();
        if (top == null)
        {
            controller.MarkTriedToDrawFromEmptyLibrary();
            return;
        }
        controller.Zones.Library.RemoveCard(top);
        controller.Zones.Hand.AddCard(top);
        top.SetZone(ZoneType.Hand);
    }

    /// <summary>
    /// CR 702.84 — resolve the Unearth activation. Returns the card from its
    /// owner's graveyard to the battlefield under the controller's control,
    /// grants Haste (CR 702.10), clears summoning sickness, and (when
    /// <paramref name="triggers"/> is supplied) registers a one-shot delayed
    /// end-step trigger that EXILES the creature (CR 702.84c). No-ops cleanly
    /// when the card is not in its owner's graveyard (zone-scoping deferred).
    /// Mirrors <see cref="HellsparkElementalFactory"/>.
    /// </summary>
    private static void ResolveUnearth(
        Creature card, Player owner, ZoneService? zoneService, TriggerManager? triggers)
    {
        // Zone guard — unearth only returns the card from the graveyard.
        if (card.Zone != ZoneType.Graveyard) return;
        if (card.Owner == null || !ReferenceEquals(card.Owner, owner)) return;
        if (!owner.Zones.Graveyard.GetCards().Contains(card)) return;

        // Graveyard → battlefield (CR 702.84a). ZoneService routes the
        // publish so ETB triggers fire (CR 603.6a).
        if (zoneService != null)
        {
            zoneService.MoveCard(card, ZoneType.Graveyard, ZoneType.Battlefield, owner);
        }
        else
        {
            owner.Zones.Graveyard.RemoveCard(card);
            owner.Zones.Battlefield.AddCard(card);
            card.SetZone(ZoneType.Battlefield);
            card.SetController(owner);
        }

        // "It gains haste." CR 702.84a / CR 613.1c (Layer 6). EOT-scoped grant
        // is observationally equivalent to the printed no-duration wording —
        // the card is exiled at the same end-step boundary at which the grant
        // would expire. No-op silently when no ActiveEffects service is wired
        // (shape mode); the summoning-sickness clear below still applies so
        // attack-declaration sees haste behaviour (CR 702.10b).
        if (card.ActiveEffects != null)
        {
            card.ActiveEffects.Register(new GrantKeywordUntilEndOfTurnEffect(card, Haste));
        }
        card.HasSummoningSickness = false;

        // "Exile it at the beginning of the next end step." CR 702.84c /
        // CR 603.7 — one-shot delayed triggered ability fenced strictly after
        // this resolution so the current end step (if any) doesn't trip it.
        if (triggers == null) return;

        var resolvedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();
        var exileEffect = new Effect(
            $"{CardName}: unearth — exile at next end step",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                var bfPlayer = card.Controller ?? owner;
                if (!bfPlayer.Zones.Battlefield.GetCards().Contains(card)) return;
                var exileOwner = card.Owner ?? owner;

                if (zoneService != null)
                {
                    zoneService.MoveCard(card, ZoneType.Battlefield, ZoneType.Exile, bfPlayer);
                }
                else
                {
                    bfPlayer.Zones.Battlefield.RemoveCard(card);
                    exileOwner.Zones.Exile.AddCard(card);
                    card.SetZone(ZoneType.Exile);
                }
            });

        var delayedExile = new DelayedTriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == PhaseStateType.End
                          && e.Timestamp > resolvedAt),
            effects: new IEffect[] { exileEffect });

        triggers.RegisterDelayed(delayedExile);
    }
}
