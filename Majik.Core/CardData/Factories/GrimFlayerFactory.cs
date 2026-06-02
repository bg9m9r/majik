using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Grim Flayer (Eldritch Moon, {B}{G}).
///
/// Creature — Human Warrior 2/2. Oracle text:
///   "Trample
///    Whenever this creature deals combat damage to a player, surveil 3.
///    (Look at the top three cards of your library, then put any number of
///    them into your graveyard and the rest on top of your library in any
///    order.)
///    Delirium — This creature gets +2/+2 as long as there are four or more
///    card types among cards in your graveyard."
///
/// ## Shape source
///
/// Card identity (name, {B}{G}, 2/2, Creature — Human Warrior) is loaded from
/// <c>Majik.Core/CardData/Cards/grim-flayer.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. Trample, the combat-damage surveil
/// trigger, and the delirium pump are wired in code below.
///
/// ## Implementation
///
/// Grim Flayer is the combat-damage analogue of
/// <see cref="DragonsRageChannelerFactory"/> — it shares the same delirium
/// (CR 702.105) static-pump and surveil (CR 701.42) primitives. The only
/// behavioural differences are the trigger source (combat damage to a player
/// vs. casting a noncreature spell), the surveil count (3 vs. 1), and the
/// delirium grant (+2/+2 only, no keyword grant vs. DRC's +2/+2 + flying).
///
/// - <b>Trample</b> (CR 702.19) as a <see cref="KeywordAbility"/> marker —
///   read by <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/> for
///   the excess-combat-damage rule. Same marker pattern as
///   <see cref="RealitySmasherFactory"/>.
///
/// - <b>Combat-damage surveil trigger (CR 603.1 + CR 701.42)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="CombatDamageDealtEvent"/>
///   matches when the damage source is Grim Flayer AND the target is a player
///   (<see cref="CombatDamageDealtEvent.TargetPlayer"/> non-null). Same source
///   + non-null-TargetPlayer predicate as
///   <see cref="RagavanNimblePilfererFactory"/>. Effect: surveil 3 — peek the
///   top three cards, route the decision through the controller's
///   <see cref="IPlayerAgent.ChooseSurveilDecisionAsync"/> when an agent is
///   registered, falling back to all-to-graveyard otherwise (same fall-back
///   pattern as <see cref="DragonsRageChannelerFactory"/> /
///   <see cref="LedgerShredderFactory"/>).
///
/// - <b>Delirium conditional static (CR 702.105 / 613.1f)</b>: a
///   <see cref="DeliriumPumpEffect"/> registered with the
///   <see cref="ContinuousEffectsService"/> when the runtime overload is used.
///   The effect is active iff Grim Flayer is on the battlefield AND the
///   controller's graveyard has 4+ distinct <see cref="CardType"/> values
///   (sampled live via <see cref="TarmogoyfFactory.CountDistinctCardTypes"/>
///   on every Compute, so graveyard changes reflect immediately — no event
///   subscriptions). When active, the effect adds +2/+2 in Layer 7c. Unlike
///   DRC, Grim Flayer grants no keyword, so only one Layer-7c effect is
///   registered.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only (Trample marker + surveil
///   trigger attached for observability, but no TriggerManager / continuous
///   effects wired). Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ContinuousEffectsService?)"/>
///   — fully wired. The surveil trigger fires from the bus when a trigger
///   manager is supplied; the +2/+2 pump registers / unregisters via a
///   battlefield-zone lifecycle handler when a continuous-effects service is
///   supplied (mirrors <see cref="DragonsRageChannelerFactory"/>).
/// </summary>
[CardName("Grim Flayer")]
public static class GrimFlayerFactory
{
    public const string CardName = "Grim Flayer";
    public const int SurveilCount = 3;
    public const int DeliriumThreshold = 4;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("grim-flayer");

    /// <summary>
    /// Construct Grim Flayer with no live wiring. Trample marker + surveil
    /// trigger are attached for shape observability; the delirium static is
    /// not registered with a continuous-effects service. Suitable for
    /// dispatcher / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, effects: null);

    /// <summary>
    /// Construct Grim Flayer with optional runtime services.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.19 — Trample, as a KeywordAbility marker read by
        // CombatAbilities.HasTrample for the excess-combat-damage rule.
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        // ----------------------------------------------------------------
        // Trigger — "Whenever this creature deals combat damage to a
        // player, surveil 3." CR 603.1 + CR 701.42. Predicate: damage
        // source is Grim Flayer AND the damage was dealt to a player
        // (TargetPlayer non-null). Same shape as Ragavan, Nimble Pilferer.
        // ----------------------------------------------------------------
        var surveilEffect = new Effect(
            "Grim Flayer — surveil 3 (whenever this creature deals combat damage to a player)",
            async ctx =>
            {
                var peeked = Majik.Core.Keywords.SurveilAction.Peek(owner, SurveilCount);
                if (peeked.Count == 0) return;

                var agent = ctx.Agent ?? AgentRegistry.Get(owner);
                Majik.Core.Keywords.SurveilAction.SurveilDecision decision;
                if (agent != null)
                {
                    decision = await agent.ChooseSurveilDecisionAsync(ctx.Game, peeked).ConfigureAwait(false);
                }
                else
                {
                    // Default: all peeked cards go to graveyard (matches
                    // Dragon's Rage Channeler / Ledger Shredder behavior
                    // when no agent is registered).
                    decision = new Majik.Core.Keywords.SurveilAction.SurveilDecision(
                        ToGraveyard: peeked.ToList(),
                        TopOrder: Array.Empty<ICard>());
                }
                Majik.Core.Keywords.SurveilAction.Apply(owner, SurveilCount, decision);
            });

        var surveilTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
                ReferenceEquals(e.Source, card) && e.TargetPlayer != null),
            effects: new IEffect[] { surveilEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(surveilTrigger);
        triggers?.RegisterTriggeredAbility(surveilTrigger);

        // ----------------------------------------------------------------
        // Delirium static — "Grim Flayer gets +2/+2 as long as there are
        // four or more card types among cards in your graveyard."
        // (CR 702.105 / CR 613.1f). One Layer-7c continuous effect, gated
        // on Grim Flayer being on the battlefield AND delirium being
        // satisfied (sampled live from the controller's graveyard on every
        // Compute). When no ContinuousEffectsService is supplied (shape-only
        // path) the effect isn't registered — the card still reflects the
        // printed 2/2 with the trigger attached.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            var lifecycle = new DeliriumLifecycle(card, owner, effects, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Sample the controller's graveyard for delirium (CR 702.105): true iff
    /// there are 4+ distinct <see cref="CardType"/> values across cards in
    /// <paramref name="controller"/>'s graveyard. Reuses
    /// <see cref="TarmogoyfFactory.CountDistinctCardTypes"/>.
    /// </summary>
    public static bool IsDeliriumActive(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return TarmogoyfFactory.CountDistinctCardTypes(
            controller.Zones.Graveyard.GetCards()) >= DeliriumThreshold;
    }

    /// <summary>
    /// CR 613.1f — Layer-7c continuous effect that pumps Grim Flayer's P/T by
    /// +2/+2, gated on delirium (CR 702.105) and on Grim Flayer being on the
    /// battlefield.
    /// </summary>
    private sealed class DeliriumPumpEffect : ContinuousEffect
    {
        private readonly Creature _source;
        private readonly Player _controller;

        public DeliriumPumpEffect(Creature source, Player controller)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        public override Layer Layer => Layer.PT_Modify;

        public override Permanent? Source => _source;

        public override bool IsActive() =>
            _source.Zone == ZoneType.Battlefield
            && IsDeliriumActive(_controller);

        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _source);

        public override void Apply(CreatureCharacteristics chars)
        {
            chars.Power += 2;
            chars.Toughness += 2;
        }
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for Grim Flayer's delirium static. Registers
    /// the +2/+2 (Layer 7c) effect when Grim Flayer enters the battlefield;
    /// unregisters when it leaves. Mirrors
    /// <see cref="DragonsRageChannelerFactory"/>'s lifecycle shape.
    /// </summary>
    private sealed class DeliriumLifecycle
    {
        private readonly Creature _source;
        private readonly Player _controller;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<CardMovedEvent> _handler;
        private DeliriumPumpEffect? _pumpRegistered;
        private bool _attached;

        public DeliriumLifecycle(
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
            var shouldBeActive = _source.Zone == ZoneType.Battlefield;
            if (shouldBeActive && _pumpRegistered == null)
            {
                _pumpRegistered = new DeliriumPumpEffect(_source, _controller);
                _effects.Register(_pumpRegistered);
            }
            else if (!shouldBeActive && _pumpRegistered != null)
            {
                _effects.Unregister(_pumpRegistered);
                _pumpRegistered = null;
            }
        }
    }
}
