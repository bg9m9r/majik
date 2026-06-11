using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Rules;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Necrodominance (Modern Horizons 3 — black
/// enchantment, Necropotence variant).
///
/// Printed oracle text (per the embedded seed):
///
///   Legendary Enchantment — {B}{B}{B}
///   "Skip your draw step.
///    At the beginning of your end step, you may pay any amount of life.
///    If you do, draw that many cards.
///    Your maximum hand size is five.
///    If a card or token would be put into your graveyard from anywhere,
///    exile it instead."
///
/// NOTE: the structural skip-additional-draw marker + the "Pay 1 life:
/// exile top + cast-from-exile" activated ability modelled below were
/// authored against an earlier (preview) wording of this card and do not
/// correspond to the printed oracle text above; they are retained as-is
/// (out of scope for the end-step trigger work) but are NOT part of the
/// real card. The live, printed clause this factory now implements is the
/// end-step pay-life-then-draw trigger.
///
/// ## Implemented (v1)
/// - Card identity: Enchantment, {B}{B}{B}, owner/controller.
/// - <b>Static "skip your draw step"</b>: wired against
///   <see cref="SkipDrawRegistry"/> when the registry-aware overload is
///   used. Same shape as <see cref="NecropotenceFactory"/> — the skip
///   predicate gates on Necrodominance being on the battlefield (CR
///   614.12) and is unregistered via the cleanup disposable when the
///   enchantment leaves the battlefield.
/// - <b>Static "skip additional draws"</b>: surfaced as a
///   <see cref="StaticAbility"/> marker only. The engine has no
///   CardDrawIntent on the ReplacementBus in v1 — every replacement
///   effect that intercepts a draw (Spirit of the Labyrinth, Alms
///   Collector, the additional-draw clause of this card) ships as a
///   structural marker until that intent shape lands. Same v1 gap as
///   the discard→exile marker on Necropotence: shape-correct, not
///   live.
/// - <b>Activated ability — Pay 1 life: exile top + cast-from-exile
///   permission until EOT</b>: cost is
///   <see cref="AdditionalCost.PayLife"/>(1); effect moves the top of
///   the controller's library into exile, builds a
///   <see cref="CastFromExileAlternativeCost"/> at <see cref="ManaCost.Zero"/>
///   for that card, and registers an EOT cleanup hook on the supplied
///   <see cref="IEventBus"/> that revokes the cast permission at the
///   next Cleanup step (CR 514.2 — "until end of turn" expires when
///   the next Cleanup begins). Each activation records the exiled card
///   on the wiring so callers (and tests) can locate it for casting.
/// - <b>Triggered ability — "At the beginning of your end step, you may
///   pay any amount of life. If you do, draw that many cards."</b>: a
///   <see cref="TriggeredAbility"/> gated on the controller's End step
///   (<see cref="Triggers.OnStepBegin"/>). On resolution the controller's
///   agent supplies the amount X via its variable-amount prompt
///   (<see cref="IPlayerAgent.ChooseXAsync"/>), clamped to
///   [0, LifeTotal] (CR 119.4); the controller then loses that much life
///   (CR 119.1c) and draws that many cards via <see cref="Fx.DrawCards"/>
///   (CR 120). The trigger lives on <c>card.Abilities</c>, so the live
///   engine auto-registers it on ETB (<c>TriggerManager</c> scans
///   battlefield permanents for <see cref="ITriggeredAbility"/>) — no
///   explicit TriggerManager wiring is needed by callers.
///
/// ## Deferred (v1 gaps)
/// - <b>Face-down exile</b>: the engine has no per-card face-down flag
///   yet. The exiled card is exile-face-up here, which is observable
///   only by other "look at exile" effects (none in v1) — game-state
///   visible behaviour is correct. Same gap as Necropotence + Dauthi
///   Voidwalker.
/// - <b>Live additional-draw skip</b>: no CardDrawIntent on the
///   ReplacementBus, so the "skip every draw except the first per draw
///   step" clause is structural-only. The static marker exposes the
///   declarative effect; when the intent shape lands, swap the marker
///   for a real <see cref="IReplacementEffect{T}"/> the same way
///   Necropotence's discard→exile clause is wired today.
/// - <b>"Look at it any time"</b>: no exile-visibility primitive in
///   the engine. Cards in exile are inspectable to all observers in
///   v1, so this clause is a no-op gain — same gap as Dauthi
///   Voidwalker.
/// - <b>Sorcery-speed cast restriction</b>: the activated ability is
///   a regular activated ability (CR 605 — not a mana ability) and
///   can be activated at instant speed in v1. Necrodominance's
///   activated ability has no printed sorcery-speed gate, so this is
///   correct. The "cast from exile" alt-cost itself inherits whatever
///   timing the underlying card has — instants stay instant-speed,
///   sorceries stay sorcery-speed.
/// - <b>Lifecycle auto-cleanup</b>: when Necrodominance leaves the
///   battlefield, callers must call the returned cleanup
///   <see cref="IDisposable"/> (registry-aware overload) to unregister
///   the skip-draw predicate. Same gap as Necropotence /
///   Dauthi Voidwalker.
/// </summary>
[CardName("Necrodominance")]
public static class NecrodominanceFactory
{
    public const string CardName = "Necrodominance";

    /// <summary>
    /// Construct Necrodominance with no registry/bus wiring — the card
    /// shape (Enchantment, mana cost, owner, three abilities) is fully
    /// populated but the skip-draw predicate and the EOT cleanup hook
    /// do not actually fire. Suitable for shape tests / the dispatcher
    /// path.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, eventBus: null).Card;

    /// <summary>
    /// Construct Necrodominance fully wired against the supplied
    /// <see cref="IEventBus"/>. The returned
    /// <see cref="NecrodominanceWiring.Cleanup"/> disposable unregisters
    /// the skip-draw predicate — call it when Necrodominance leaves the
    /// battlefield. The <see cref="NecrodominanceWiring.ActiveCasts"/>
    /// snapshot exposes the per-activation cast-from-exile permissions
    /// the EOT cleanup is tracking; production callers wire each entry
    /// into <c>SpellCastFlow</c> as an alternative-cost candidate.
    /// </summary>
    public static NecrodominanceWiring Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            "{B}{B}{B}",
            supertypes: new[] { CardSupertype.Legendary });
        card.SetOwner(owner);
        card.SetController(owner);

        // -----------------------------------------------------------------
        // Static — "Skip your draw step."
        //
        // Registered via SkipDrawRegistry; consulted by TurnDriver before
        // performing the normal draw-step draw. The predicate gates on
        // Necrodominance's zone so a bounced enchantment stops skipping
        // immediately (CR 614.12 — replacement effects function only while
        // their source is on the battlefield). Same shape as Necropotence.
        // -----------------------------------------------------------------
        var skipToken = new object();
        SkipDrawRegistry.AddSkip(skipToken, p =>
            ReferenceEquals(p, card.Controller) && card.Zone == ZoneType.Battlefield);

        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description: "Skip your draw step.",
            isActiveCheck: () => card.Zone == ZoneType.Battlefield,
            applyEffect: null));

        // -----------------------------------------------------------------
        // Static marker — "If you would draw a card except for the first
        // card you draw in each of your draw steps, skip that draw."
        //
        // Structural-only in v1. The engine has no CardDrawIntent on the
        // ReplacementBus, so the additional-draw skip ships as a
        // declarative StaticAbility marker. When CardDrawIntent lands,
        // swap this for a real IReplacementEffect that skips every draw
        // after the first per draw step (Necropotence's
        // ZoneMoveIntent-based discard→exile is the template).
        // -----------------------------------------------------------------
        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description:
                "If you would draw a card except for the first card you draw in each "
                + "of your draw steps, skip that draw.",
            isActiveCheck: () => card.Zone == ZoneType.Battlefield,
            applyEffect: null));

        // -----------------------------------------------------------------
        // Activated — "Pay 1 life: Exile the top card of your library face
        //              down. Look at it any time. You may cast that card
        //              from exile until end of turn."
        //
        // CR 605 — not a mana ability. The effect exiles the top of the
        // controller's library and builds a CastFromExileAlternativeCost
        // at ManaCost.Zero so callers can play the exiled card without
        // paying its mana cost (CR 118.9). Each activation registers an
        // independent EOT cleanup on the event bus that revokes the
        // permission at the next Cleanup step (CR 514.2). The wiring's
        // ActiveCasts list snapshots the live permissions so production
        // callers / tests can locate them for SpellCastFlow integration.
        //
        // Face-down exile is deferred — engine has no face-down flag.
        // "Look at it any time" is a no-op in v1 (exile is fully visible).
        // -----------------------------------------------------------------
        var activeCasts = new List<NecrodominanceCastPermission>();

        var activated = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.PayLife(1),
            },
            effects: new IEffect[]
            {
                new Effect(
                    "Necrodominance: exile top of library + grant cast-from-exile until EOT",
                    () =>
                    {
                        // Necrodominance only functions while on the battlefield
                        // (CR 113.6). Guard the effect body too — the cost
                        // payment has already happened by the time we reach
                        // here, but a destroyed Necrodominance shouldn't be
                        // able to keep dredging cards in response.
                        if (card.Zone != ZoneType.Battlefield) return;

                        var top = owner.Zones.Library.GetCards().FirstOrDefault();
                        if (top == null)
                        {
                            // Empty library — SBA loss handled elsewhere (CR
                            // 704.5b). No card to exile.
                            return;
                        }

                        owner.Zones.Library.RemoveCard(top);
                        owner.Zones.Exile.AddCard(top);
                        top.SetZone(ZoneType.Exile);

                        var altCost = new CastFromExileAlternativeCost(
                            description: $"Necrodominance — cast {top.Name} from exile without paying its mana cost",
                            cost: ManaCost.Zero);
                        var permission = new NecrodominanceCastPermission(top, altCost);
                        activeCasts.Add(permission);

                        // CR 514.2 — schedule revocation. Subscribe to
                        // StepStartedEvent and revoke the permission at the
                        // next Cleanup step. No bus → no auto-revocation
                        // (factory was built shape-only; callers manage EOT
                        // manually).
                        if (eventBus == null) return;

                        Action<StepStartedEvent>? handler = null;
                        handler = (e) =>
                        {
                            if (e.StepType != StepStateType.Cleanup) return;
                            permission.Revoke();
                            activeCasts.Remove(permission);
                            if (handler != null) eventBus.Unsubscribe(handler);
                        };
                        eventBus.Subscribe(handler);
                    }),
            });

        card.AddAbility(activated);

        // -----------------------------------------------------------------
        // Triggered — "At the beginning of your end step, you may pay any
        //              amount of life. If you do, draw that many cards."
        //
        // CR 603.1 — a "you may" end-step trigger. On resolution the
        // controller's agent chooses how much life to pay (the variable
        // amount X). Paying 0 (or declining) draws nothing; paying N pays
        // (loses) N life and draws N cards.
        //
        // The amount is sourced from the agent's variable-amount prompt
        // (IPlayerAgent.ChooseXAsync) — the same primitive SpellCastFlow
        // uses for {X} costs — then clamped to [0, controller.LifeTotal]
        // (CR 119.4 — a player can't pay more life than they have; this
        // seed has no "you don't lose the game for 0 or less life" clause,
        // so paying down to 0 is the floor). Each drawn card is routed
        // through Fx.DrawCards so the controller's ReplacementBus sees a
        // DrawCardIntent (CR 614) — relevant if a future draw-replacement
        // is attached; this seed's graveyard→exile replacement does not
        // intercept draws, so the cards land in hand normally.
        //
        // The effect reads the live agent + game off the ResolutionContext
        // (CR 608) — populated on the routed trigger-resolution path
        // (TriggeredAbility.ResolveAsync). With no agent (shape path /
        // legacy sync Execute) it falls back to the AgentRegistry, then to
        // "pay nothing" so a context-free fire is a safe no-op.
        // -----------------------------------------------------------------
        var endStepEffect = new Effect(
            "Necrodominance: you may pay any amount of life, then draw that many cards",
            async (ResolutionContext ctx) =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                var controller = card.Controller;
                if (controller == null) return;

                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                if (agent == null) return; // No decider → pay nothing (CR 119.1 — optional).

                var requested = await agent
                    .ChooseXAsync(ctx.Game!, card, ctx.Ct)
                    .ConfigureAwait(false);

                // CR 119.4 — clamp to the life the controller can actually
                // pay; a negative request (mis-behaving agent) pays nothing.
                var amount = Math.Clamp(requested, 0, controller.LifeTotal);
                if (amount <= 0) return;

                controller.LoseLife(amount);            // CR 119.1c — paying life is losing it.
                Fx.DrawCards(controller, amount);       // CR 120 — draw that many.
            });

        var endStepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, StepStateType.End),
            effects: new IEffect[] { endStepEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(endStepTrigger);

        var cleanup = new NecrodominanceCleanup(skipToken);
        return new NecrodominanceWiring(card, activeCasts, cleanup);
    }
}

/// <summary>
/// One per Pay-1-life activation: pairs the exile-resident card with the
/// <see cref="CastFromExileAlternativeCost"/> that lets the controller
/// cast it for free until end of turn. <see cref="Revoke"/> marks the
/// permission expired so callers can drop it from their alt-cost
/// candidate set on the next SpellCastFlow probe.
/// </summary>
public sealed class NecrodominanceCastPermission
{
    /// <summary>The card exiled by the activation.</summary>
    public ICard ExiledCard { get; }

    /// <summary>The alternative cost that casts <see cref="ExiledCard"/>
    /// for free while the permission is active.</summary>
    public CastFromExileAlternativeCost AlternativeCost { get; }

    /// <summary>True until the EOT cleanup hook fires.</summary>
    public bool IsActive { get; private set; } = true;

    public NecrodominanceCastPermission(ICard exiledCard, CastFromExileAlternativeCost alternativeCost)
    {
        ExiledCard = exiledCard ?? throw new ArgumentNullException(nameof(exiledCard));
        AlternativeCost = alternativeCost ?? throw new ArgumentNullException(nameof(alternativeCost));
    }

    internal void Revoke() => IsActive = false;
}

/// <summary>
/// Bundle of the artifact handles returned by the bus-aware
/// <see cref="NecrodominanceFactory.Create(Player, IEventBus?)"/>
/// overload. <see cref="Cleanup"/> unregisters the skip-draw predicate
/// on dispose — call it when Necrodominance leaves the battlefield.
/// <see cref="ActiveCasts"/> exposes the live cast-from-exile
/// permissions stamped by each Pay-1-life activation.
/// </summary>
public sealed record NecrodominanceWiring(
    Enchantment Card,
    IReadOnlyList<NecrodominanceCastPermission> ActiveCasts,
    IDisposable Cleanup);

/// <summary>
/// Disposable cleanup handle: unregisters the skip-draw predicate from
/// <see cref="SkipDrawRegistry"/>. Idempotent — multiple
/// <see cref="Dispose"/> calls are safe.
/// </summary>
internal sealed class NecrodominanceCleanup : IDisposable
{
    private readonly object _skipToken;
    private bool _disposed;

    public NecrodominanceCleanup(object skipToken)
    {
        _skipToken = skipToken ?? throw new ArgumentNullException(nameof(skipToken));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SkipDrawRegistry.RemoveSkip(_skipToken);
    }
}
