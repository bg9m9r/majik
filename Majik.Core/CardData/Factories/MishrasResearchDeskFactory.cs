using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mishra's Research Desk (Modern Horizons 3, {1}).
///
/// Artifact. Oracle text (verified against Scryfall):
///   "{1}, {T}, Sacrifice this artifact: Exile the top two cards of your
///    library. Choose one of them. Until the end of your next turn, you may
///    play that card.
///    Unearth {1}{R} ({1}{R}: Return this card from your graveyard to the
///    battlefield. Exile it at the beginning of the next end step or if it
///    would leave the battlefield. Unearth only as a sorcery.)"
///
/// The base shape (name, Artifact, {1}) is materialised from the embedded
/// JSON definition (<c>mishras-research-desk.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same JSON-backed posture as
/// <see cref="ExperimentalSynthesizerFactory"/> / <see cref="ScrapworkMuttFactory"/>.
/// The two printed behaviours (the impulse-draw activated ability and the
/// Unearth activated ability) are layered on here, since the JSON ability
/// schema doesn't express the runtime exile-cast grant or unearth.
///
/// ## Implemented (v1)
/// - <b>{1}, {T}, Sacrifice this artifact: exile top two, choose one, may play
///   it until end of your next turn.</b> One <see cref="ActivatedAbility"/>
///   (CR 602.1) with three costs: a <see cref="ManaCostCost"/> ({1}), an
///   <see cref="AdditionalCost.Tap"/> (CR 602.5e) and a
///   <see cref="SacrificeSelfCost"/> (CR 701.16). On resolution it exiles the
///   top two cards of the controller's library (CR 701.20), prompts the
///   controller to choose ONE of the two exiled cards (agent-driven;
///   deterministic first-card fallback when no agent registered — same posture
///   as the reveal-and-choose family), and stamps a runtime exile-cast grant
///   (<see cref="Card.GrantRuntimeExileCast"/>) on ONLY the chosen card so the
///   controller may play it for its printed mana cost (CR 118.9). The
///   unchosen card stays in exile with no grant. The grant clears on the
///   controller's NEXT turn's Cleanup step — "until the end of your next turn"
///   (CR 514.2). Same two-Cleanup-count duration as
///   <see cref="RecklessImpulseFactory"/>; the activated ability resolves on
///   the controller's own turn (sorcery-speed sacrifice activations are most
///   common, but this ability has no sorcery-speed rider — see the duration
///   note below).
/// - <b>Unearth {1}{R} (CR 702.85)</b>: a graveyard-activated, sorcery-speed
///   <see cref="ActivatedAbility"/> with a {1}{R} <see cref="ManaCostCost"/>.
///   On resolution it returns this artifact from its owner's graveyard to the
///   battlefield (routing through <see cref="ZoneService.MoveCard"/> when
///   supplied so ETB triggers fire — CR 603.6a) and registers a one-shot
///   <see cref="DelayedTriggeredAbility"/> (CR 603.7) that <b>exiles</b> the
///   artifact at the beginning of the next end step (CR 702.85c). No "gains
///   haste" rider — this is a noncreature artifact (haste is meaningless), so
///   unlike <see cref="ScrapworkMuttFactory"/> the unearth grants nothing
///   beyond the return + delayed exile.
///
/// ## Duration note (CR 514.2)
/// "Until the end of your next turn" ends at the SECOND Cleanup step belonging
/// to the controller after the grant is stamped: the first is the current
/// turn's cleanup (grant survives), the second is the controller's next turn's
/// cleanup (grant clears). This activated ability can be activated at instant
/// speed (no sorcery rider), so it may resolve on an opponent's turn. In that
/// case the controller's CURRENT turn has not happened yet at stamp time, and
/// counting Cleanups owned by the controller still lands the clear on the
/// controller's next-turn cleanup. Same Cleanup-counting model as
/// <see cref="RecklessImpulseFactory"/>.
///
/// ## Deferred (v1 gaps — mirror the existing impulse + unearth factories)
/// - <b>"May play that card" includes lands</b>: the runtime exile-cast grant
///   authorises casting; an exiled land would need a parallel "play this land
///   from exile" grant. v1 ships the spell-only authorisation, matching the
///   Experimental Synthesizer / Light Up the Stage posture.
/// - <b>Empty / single-card library</b>: "top two" exiles whatever is there
///   (CR 121.2). If the library is empty the exile is a clean no-op (CR 701.20
///   imposes no SBA flag); if it holds one card, one card is exiled and is the
///   sole choice.
/// - <b>Zone-scoped unearth activation</b>: the engine does not yet gate
///   activated abilities on source zone (CR 113.6). The unearth ability is
///   enumerable from any zone; the resolution body guards
///   <c>card.Zone == Graveyard</c> so spurious activations are no-op-shaped
///   (same caveat as <see cref="ScrapworkMuttFactory"/> / Hellspark Elemental).
/// - <b>"…or if it would leave the battlefield" exile rider</b>: the
///   "exile at the next end step" half of unearth (CR 702.85c) is wired via
///   the delayed end-step trigger. The "or if it would leave the battlefield"
///   half is a replacement-style effect (CR 614) the engine does not yet
///   expose for graveyard-origin permanents — recorded as a deferral, same as
///   <see cref="ScrapworkMuttFactory"/>.
/// </summary>
[CardName("Mishra's Research Desk")]
public static class MishrasResearchDeskFactory
{
    public const string CardName = "Mishra's Research Desk";
    public const string Slug = "mishras-research-desk";

    /// <summary>Mana portion of the impulse activated ability cost. CR 602.1.</summary>
    public const string ImpulseManaCost = "{1}";

    /// <summary>Number of cards exiled by the impulse ability. CR 701.20.</summary>
    public const int CardsExiled = 2;

    /// <summary>Unearth activation cost. CR 702.85.</summary>
    public const string UnearthCost = "{1}{R}";

    /// <summary>
    /// Construct Mishra's Research Desk with no live runtime services. Both
    /// activated abilities are attached for shape inspection; the impulse
    /// grant's "until end of your next turn" cleanup is not scheduled (no
    /// event bus) and the unearth's delayed exile is not registered (no
    /// trigger manager). Suitable for shape / dispatcher tests. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, zoneService: null, agent: null);

    /// <summary>
    /// Construct a fully-wired Mishra's Research Desk.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">When supplied, the impulse resolve effect
    /// schedules its "until the end of your next turn" exile-cast cleanup on
    /// the controller's next-turn Cleanup step (CR 514.2).</param>
    /// <param name="triggers">When supplied, each unearth activation registers
    /// its own one-shot delayed end-step exile trigger (CR 603.7 /
    /// 702.85c).</param>
    /// <param name="zoneService">When supplied the unearth return routes
    /// through <see cref="ZoneService.MoveCard"/> so ETB triggers fire
    /// (CR 603.6a).</param>
    /// <param name="agent">When supplied it drives the "choose one of them"
    /// pick; agent-less callers deterministically grant the first exiled
    /// card.</param>
    public static Artifact Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService,
        IPlayerAgent? agent)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Artifact, {1}).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Artifact)CardDefinitionFactory.Build(definition, owner);

        AddImpulseAbility(card, owner, eventBus, agent);
        AddUnearthAbility(card, owner, zoneService, triggers);

        return card;
    }

    /// <summary>
    /// {1}, {T}, Sacrifice this artifact: Exile the top two cards of your
    /// library. Choose one of them. Until the end of your next turn, you may
    /// play that card. CR 602.1 activated ability; CR 602.5e tap; CR 701.16
    /// sacrifice; CR 701.20 exile; CR 118.9 play-from-exile permission;
    /// CR 514.2 duration.
    /// </summary>
    private static void AddImpulseAbility(
        Artifact card, Player owner, IEventBus? eventBus, IPlayerAgent? agent)
    {
        var impulseEffect = new Effect(
            $"{CardName}: exile top two, choose one, may play it until end of your next turn",
            ctx => ResolveImpulseAsync(card, owner, eventBus, agent, ctx));

        var impulseAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ImpulseManaCost),
                AdditionalCost.Tap(card),
                new SacrificeSelfCost(card),
            },
            effects: new IEffect[] { impulseEffect });

        card.AddAbility(impulseAbility);
    }

    /// <summary>
    /// CR 701.20 — exile the top two cards of the controller's library, then
    /// CR 118.9 — grant the controller permission to play exactly ONE of them
    /// for its printed mana cost. The unchosen card stays exiled with no
    /// grant. The grant clears at the controller's next-turn Cleanup
    /// (CR 514.2 — second controller-owned Cleanup after the stamp).
    /// </summary>
    private static async ValueTask ResolveImpulseAsync(
        Artifact card, Player owner, IEventBus? eventBus, IPlayerAgent? agent, ResolutionContext ctx)
    {
        var controller = card.Controller ?? owner;
        agent = ctx.Agent ?? agent ?? AgentRegistry.Get(controller);

        // CR 701.20 / CR 121.2 — exile the top two cards (or whatever's there
        // if the library is shorter). Snapshot the exiled cards in reveal
        // order so the choice is over a stable pile.
        var exiled = new List<Card>(CardsExiled);
        for (var i = 0; i < CardsExiled; i++)
        {
            var top = controller.Zones.Library.GetCards().FirstOrDefault();
            if (top == null) break; // library underflow — exile finds nothing

            controller.Zones.Library.RemoveCard(top);
            controller.Zones.Exile.AddCard(top);
            top.SetZone(ZoneType.Exile);

            if (top is Card concrete) exiled.Add(concrete);
        }

        if (exiled.Count == 0) return; // empty library — nothing to choose

        // "Choose one of them." CR 116.x — the controller picks which of the
        // exiled cards becomes playable. Agent-driven; deterministic
        // first-card fallback when no agent is registered (same posture as the
        // reveal-and-choose family). Both exiled cards are eligible.
        Card chosen = exiled[0];
        if (agent != null && exiled.Count > 1)
        {
            var pick = await agent.ChooseFromRevealedAsync(
                    ctx: ctx.Game,
                    revealed: exiled,
                    eligible: exiled,
                    optional: false,
                    label: $"{CardName}: choose one card to play until end of your next turn",
                    ct: ctx.Ct)
                .ConfigureAwait(false);

            if (pick is Card pc && exiled.Contains(pc)) chosen = pc;
        }

        // CR 118.9 — "you may play that card" with no alternate-cost rider:
        // the grant authorises casting for the printed mana cost. Same
        // impulse-draw primitive as Experimental Synthesizer / Reckless
        // Impulse. Stamp ONLY the chosen card.
        chosen.GrantRuntimeExileCast(controller, chosen.ManaCostValue);

        if (eventBus == null) return;

        // CR 514.2 — "until the end of your next turn": clear the grant on the
        // SECOND Cleanup step belonging to the controller after the stamp. The
        // first such Cleanup is the controller's current turn's cleanup (grant
        // survives); the second is the controller's next turn's cleanup
        // (clear). Same two-Cleanup model as Reckless Impulse.
        var cleanupsSeen = 0;
        Action<StepStartedEvent>? handler = null;
        handler = (e) =>
        {
            if (e.StepType != PhaseStateType.Cleanup) return;
            if (!ReferenceEquals(e.Player, controller)) return;
            cleanupsSeen++;
            if (cleanupsSeen < 2) return;

            chosen.ClearRuntimeExileCast();
            if (handler != null) eventBus.Unsubscribe(handler);
        };
        eventBus.Subscribe(handler);
    }

    /// <summary>
    /// Unearth {1}{R} — CR 702.85. Graveyard-activated, sorcery-speed
    /// activated ability. Returns this artifact from graveyard → battlefield
    /// and registers a delayed end-step EXILE (CR 702.85c). No "gains haste"
    /// rider (noncreature artifact). Same unearth shape as Scrapwork Mutt,
    /// minus the haste grant + summoning-sickness clear.
    /// </summary>
    private static void AddUnearthAbility(
        Artifact card, Player owner, ZoneService? zoneService, TriggerManager? triggers)
    {
        var unearthEffect = new Effect(
            $"{CardName}: unearth — return from graveyard, exile next end step",
            () => ResolveUnearth(card, owner, zoneService, triggers));

        var unearthAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(UnearthCost) },
            effects: new IEffect[] { unearthEffect },
            // CR 702.85a — "Unearth only as a sorcery." ActionValidator gates
            // activation on the controller's main phase + empty stack.
            sorcerySpeed: true);

        card.AddAbility(unearthAbility);
    }

    /// <summary>
    /// CR 702.85 — resolve the Unearth activation. Returns the artifact from
    /// its owner's graveyard to the battlefield under the owner's control and
    /// (when <paramref name="triggers"/> is supplied) registers a one-shot
    /// delayed end-step trigger that EXILES the artifact (CR 702.85c). No-ops
    /// cleanly when the card is not in its owner's graveyard (zone-scoping
    /// deferred). Mirrors <see cref="ScrapworkMuttFactory"/> without the haste
    /// grant.
    /// </summary>
    private static void ResolveUnearth(
        Artifact card, Player owner, ZoneService? zoneService, TriggerManager? triggers)
    {
        // Zone guard — unearth only returns the card from the graveyard.
        if (card.Zone != ZoneType.Graveyard) return;
        if (card.Owner == null || !ReferenceEquals(card.Owner, owner)) return;
        if (!owner.Zones.Graveyard.GetCards().Contains(card)) return;

        // Graveyard → battlefield (CR 702.85a). ZoneService routes the publish
        // so ETB triggers fire (CR 603.6a).
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

        // "Exile it at the beginning of the next end step." CR 702.85c /
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
