using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Magmatic Channeler (Modern Horizons 3, {1}{R}).
///
/// Creature — Human Wizard 1/3. Oracle text (verified against Scryfall
/// 2026-06-16):
///   "As long as there are four or more instant and/or sorcery cards in
///    your graveyard, this creature gets +3/+1.
///    {T}, Discard a card: Exile the top two cards of your library, then
///    choose one of them. You may play that card this turn."
///
/// ## Implemented (v1)
///
/// - 1/3 Creature — Human Wizard at {1}{R}; owner / controller wired.
///   Subtypes <see cref="CardSubtype.Human"/> + <see cref="CardSubtype.Wizard"/>
///   (CR 205.3m).
/// - <b>Dynamic conditional static (CR 613.1f / 613.4c)</b>: "gets +3/+1 as
///   long as there are four or more instant and/or sorcery cards in your
///   graveyard." A Layer-7c <see cref="GraveyardThresholdPumpEffect"/>
///   registered with the <see cref="ContinuousEffectsService"/> on the
///   runtime overload. <see cref="GraveyardThresholdPumpEffect.IsActive"/> is
///   sampled live on every <see cref="ContinuousEffectsService.Compute(Permanent)"/>
///   off the source's <em>controller</em>'s graveyard (CR 109.5 — "your" =
///   the permanent's controller), so the +3/+1 turns on/off as instants and
///   sorceries enter/leave the yard with no event subscriptions. Same
///   live-sampled conditional-pump shape as
///   <see cref="DragonsRageChannelerFactory"/>'s delirium static — the count
///   reads the BEARER's controller's graveyard, which is what makes this
///   correct when Agatha's Soul Cauldron grants the ability set away (the
///   static itself is not granted — Agatha grants only ACTIVATED abilities —
///   but the dynamic-count discipline is shared with the activated body
///   below).
/// - <b>Activated ability (CR 602.1)</b>: <see cref="ActivatedAbility"/> with
///   a <see cref="AdditionalCost.Tap"/> ({T}) + <see cref="DiscardACardCost"/>
///   (discard a card) cost pair and one effect.
///   <list type="bullet">
///     <item>{T} — the tap symbol (CR 602.1b). Auto-re-homed to the bearer
///       by <see cref="ActivatedAbility.RebindTo"/> Stage 1 under Agatha.</item>
///     <item>Discard a card — a player-resource cost with no captured source,
///       passed through unchanged by RebindTo.</item>
///   </list>
///   Resolve closure: exile the top <see cref="ExileCount"/> cards of the
///   controller's library (CR 701.20), let the controller choose one of them
///   via <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> (deterministic
///   first-card fallback pre-agent), and stamp a runtime exile-cast grant
///   (<see cref="Card.GrantRuntimeExileCast"/>) on the chosen card so the
///   controller may play it this turn for its printed mana cost (CR 118.9 —
///   the same impulse-play primitive as <see cref="AbbotOfKeralKeepFactory"/>
///   / <see cref="LightUpTheStageFactory"/>). The grant clears on the next
///   Cleanup step (CR 514.2) when an <see cref="IEventBus"/> is supplied.
///
/// ## Re-source safety (Agatha's Soul Cauldron)
///
/// The activated effect resolves the searching player off the live
/// <see cref="Majik.Core.Effects.ResolutionContext.Source"/>'s CONTROLLER
/// (this ability's own source at resolution) rather than capturing
/// <c>card</c>, falling back to <c>card</c> / <c>owner</c> only on the
/// context-less legacy sync path (<see cref="ResolutionContext.Legacy"/>,
/// Source = null). The ability is marked <c>rebindSafe: true</c> so Agatha's
/// Soul Cauldron re-homes this REAL ability — its {T} cost auto-re-homed by
/// RebindTo Stage 1 — to a counter-bearing bearer via
/// <see cref="ActivatedAbility.RebindTo"/> (CR 707.2 / 613.1f): the bearer's
/// controller taps the BEARER and digs through THEIR library, never
/// re-reading the exiled Magmatic Channeler. The exile-top-two-and-impulse
/// shape is OUTSIDE the <c>OracleActivatedAbilityBinder</c> reconstructable
/// set, so RebindTo of the real ability is the only sound re-home.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. The activated ability is
///   attached for shape observability; the dynamic static is NOT registered
///   with a continuous-effects service (printed 1/3 only). Suitable for
///   dispatcher / structural tests.
/// - <see cref="Create(Player, IEventBus?, ContinuousEffectsService?)"/> —
///   runtime-wired. When a continuous-effects service is supplied the +3/+1
///   conditional pump registers / unregisters via a battlefield-zone
///   lifecycle handler; the supplied bus drives the impulse-play EOT cleanup.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Reveal-event emission</b>: the printed "choose one of them" should
///   emit a <see cref="Majik.Core.Events.CardRevealedEvent"/> for the chosen
///   card so portal subscribers can flash it. Same gap as Stoneforge
///   Mystic's ETB tutor — deferred behind the reveal-event plumbing pass.
/// - <b>"You may play that card" includes lands</b>: the runtime exile-cast
///   grant authorises CASTING for the printed mana cost; an exiled land would
///   need a parallel "play this land from exile" grant. Same posture as
///   <see cref="LightUpTheStageFactory"/>'s spell-only authorisation.
/// </summary>
[CardName("Magmatic Channeler")]
public static class MagmaticChannelerFactory
{
    public const string CardName = "Magmatic Channeler";
    public const string PrintedManaCost = "{1}{R}";
    public const int Power = 1;
    public const int Toughness = 3;
    public const int ExileCount = 2;
    public const int GraveyardThreshold = 4;
    public const int PumpPower = 3;
    public const int PumpToughness = 1;

    /// <summary>
    /// Construct Magmatic Channeler with no live wiring. The {T}, Discard-a-
    /// card impulse-dig ability is attached; the +3/+1 conditional static is
    /// NOT registered with a continuous-effects service (printed 1/3 only).
    /// Suitable for dispatcher / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, effects: null);

    /// <summary>
    /// Construct Magmatic Channeler with optional runtime services. When a
    /// <see cref="ContinuousEffectsService"/> is supplied the +3/+1 conditional
    /// pump is registered via a battlefield-zone lifecycle handler (mirrors
    /// <see cref="DragonsRageChannelerFactory"/>). When an <see cref="IEventBus"/>
    /// is supplied the impulse-play grant is cleared on the next Cleanup step
    /// (CR 514.2).
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus, ContinuousEffectsService? effects)
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

        // ----------------------------------------------------------------
        // Dynamic conditional static — "As long as there are four or more
        // instant and/or sorcery cards in your graveyard, this creature gets
        // +3/+1." CR 613.1f / 613.4c. A live-sampled Layer-7c pump reading the
        // source's controller's graveyard on every Compute (no subscriptions).
        // Registered only on the runtime overload (effects != null); the
        // shape-only path leaves the printed 1/3 with the ability attached.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            var lifecycle = new GraveyardThresholdLifecycle(card, owner, effects, eventBus);
            lifecycle.Attach();
        }

        // ----------------------------------------------------------------
        // Activated ability — CR 602.1.
        //   "{T}, Discard a card: Exile the top two cards of your library,
        //    then choose one of them. You may play that card this turn."
        // The {T} + discard costs are taken by the cost layer; the exile +
        // pick + impulse-grant are performed in the resolve closure. The
        // searching player is read off ctx.Source's controller (re-source-
        // safe) so Agatha's re-homed copy digs the BEARER's library.
        // ----------------------------------------------------------------
        var digEffect = new Effect(
            $"{CardName}: exile top {ExileCount}, choose one, may play it this turn",
            async ctx =>
            {
                var controller = ctx.Source?.Controller ?? card.Controller ?? owner;
                var library = controller.Zones.Library;

                // CR 701.20 — exile the top two cards (fewer if the library is
                // short; empty library is a clean no-op).
                var exiled = new List<Card>();
                for (var i = 0; i < ExileCount; i++)
                {
                    var top = library.GetCards().FirstOrDefault();
                    if (top is not Card concrete) break;
                    library.RemoveCard(concrete);
                    controller.Zones.Exile.AddCard(concrete);
                    concrete.SetZone(ZoneType.Exile);
                    exiled.Add(concrete);
                }

                if (exiled.Count == 0) return;

                // "then choose one of them" — controller's choice. Agent path:
                // ChooseLibraryPickAsync; deterministic first-exiled fallback
                // pre-agent (matches every other look-and-pick factory).
                ICard chosen = exiled[0];
                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                if (agent != null)
                {
                    var pick = await agent.ChooseLibraryPickAsync(
                        ctx: ctx.Game,
                        candidates: exiled.Cast<ICard>().ToList(),
                        kindLabel: "card to play this turn").ConfigureAwait(false);
                    if (pick != null && exiled.Contains(pick))
                    {
                        chosen = pick;
                    }
                }

                // CR 118.9 — "You may play that card this turn": stamp a
                // runtime exile-cast grant on the chosen card for its printed
                // mana cost (same impulse-play primitive as Abbot of Keral
                // Keep / Light Up the Stage). The OTHER exiled card stays in
                // exile with no grant.
                if (chosen is Card concreteChosen)
                {
                    concreteChosen.GrantRuntimeExileCast(controller, concreteChosen.ManaCostValue);

                    // CR 514.2 — "this turn" = until the next Cleanup. Clear
                    // the grant at the next Cleanup step when a bus is wired.
                    if (eventBus != null)
                    {
                        Action<StepStartedEvent>? handler = null;
                        handler = se =>
                        {
                            if (se.StepType != StepStateType.Cleanup) return;
                            concreteChosen.ClearRuntimeExileCast();
                            if (handler != null) eventBus.Unsubscribe(handler);
                        };
                        eventBus.Subscribe(handler);
                    }
                }
            });

        var digAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(card),
                new DiscardACardCost(),
            },
            effects: new IEffect[] { digEffect },
            rebindSafe: true);

        card.AddAbility(digAbility);

        return card;
    }

    /// <summary>
    /// CR 613.4c sample — true iff <paramref name="controller"/>'s graveyard
    /// holds <see cref="GraveyardThreshold"/>+ instant and/or sorcery cards
    /// (CR 205.2 / 400.1). The conditional pump's
    /// <see cref="GraveyardThresholdPumpEffect.IsActive"/> consults this on
    /// every Compute.
    /// </summary>
    public static bool IsPumpActive(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (controller.Zones?.Graveyard == null) return false;

        var count = 0;
        foreach (var c in controller.Zones.Graveyard.GetCards())
        {
            if (c.HasType(CardType.Instant) || c.HasType(CardType.Sorcery))
            {
                count++;
                if (count >= GraveyardThreshold) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// CR 613.1f — Layer-7c conditional pump: +3/+1 while the source's
    /// controller's graveyard holds 4+ instant and/or sorcery cards. The gate
    /// is sampled live (no subscriptions) so the bonus tracks the graveyard
    /// state at compute time.
    /// </summary>
    private sealed class GraveyardThresholdPumpEffect : ContinuousEffect
    {
        private readonly Creature _source;
        private readonly Player _controller;

        public GraveyardThresholdPumpEffect(Creature source, Player controller)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        public override Layer Layer => Layer.PT_Modify;
        public override Permanent? Source => _source;

        public override bool IsActive() =>
            _source.Zone == Majik.Core.Zones.ZoneType.Battlefield
            && IsPumpActive(_controller);

        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _source);

        public override void Apply(CreatureCharacteristics chars)
        {
            chars.Power += PumpPower;
            chars.Toughness += PumpToughness;
        }

        /// <summary>
        /// Sim-only: reconstruct an identical pump bound to
        /// <paramref name="clonedSource"/> for the search-sandbox clone.
        /// preserves: nothing beyond target; source → clonedSource (as Creature);
        /// controller → clonedSource.Controller.
        /// </summary>
        internal override ContinuousEffect? CloneForSim(
            Permanent clonedSource,
            System.Func<System.Collections.Generic.IReadOnlyList<Player>>? clonedPlayers)
        {
            if (clonedSource is not Creature clonedCreature) return null;
            var clonedController = clonedCreature.Controller;
            if (clonedController == null) return null;
            return new GraveyardThresholdPumpEffect(clonedCreature, clonedController);
        }
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for the +3/+1 conditional static. Registers
    /// the pump when Magmatic Channeler enters the battlefield; unregisters
    /// when it leaves. Mirrors <see cref="DragonsRageChannelerFactory"/>'s
    /// delirium lifecycle.
    /// </summary>
    private sealed class GraveyardThresholdLifecycle
    {
        private readonly Creature _source;
        private readonly Player _controller;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<CardMovedEvent> _handler;
        private GraveyardThresholdPumpEffect? _registered;
        private bool _attached;

        public GraveyardThresholdLifecycle(
            Creature source,
            Player controller,
            ContinuousEffectsService effects,
            IEventBus? eventBus)
        {
            _source = source;
            _controller = controller;
            _effects = effects;
            _eventBus = eventBus;
            _handler = OnEvent;
        }

        public void Attach()
        {
            if (_attached) return;
            _attached = true;
            _source.ActiveEffects = _effects;
            _eventBus?.Subscribe(_handler);
            Sync();
        }

        private void OnEvent(CardMovedEvent e)
        {
            if (!ReferenceEquals(e.Card, _source)) return;
            Sync();
        }

        private void Sync()
        {
            var shouldBeActive = _source.Zone == Majik.Core.Zones.ZoneType.Battlefield;
            if (shouldBeActive && _registered == null)
            {
                _registered = new GraveyardThresholdPumpEffect(_source, _controller);
                _effects.Register(_registered);
            }
            else if (!shouldBeActive && _registered != null)
            {
                _effects.Unregister(_registered);
                _registered = null;
            }
        }
    }
}
