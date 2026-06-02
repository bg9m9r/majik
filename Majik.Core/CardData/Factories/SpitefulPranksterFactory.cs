using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spiteful Prankster (Eldritch Moon, {2}{R}).
/// Creature — Devil 3/2. Oracle text (verified against Scryfall 2026-06):
///   "During your turn, this creature has first strike.
///    Whenever another creature dies, this creature deals 1 damage to target
///    player or planeswalker."
///
/// The base shape (name, Creature, Devil subtype, {2}{R}, 3/2) is materialised
/// from the embedded JSON definition (<c>spiteful-prankster.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours are
/// layered on here — the JSON <see cref="AbilityDefinition"/> schema expresses
/// neither a turn-gated conditional keyword static nor a death-triggered
/// targeted-damage ability.
///
/// ## Implemented (v1)
///
/// - 3/2 <see cref="Creature"/> — Devil at {2}{R}, owner/controller wired
///   (from JSON).
/// - <b>Conditional first strike (CR 613.1f — Layer 6 / CR 702.7)</b>: a
///   <see cref="ConditionalFirstStrikeStaticEffect"/> grants "First strike" to
///   the Prankster while it is on the battlefield AND it is the controller's
///   turn (the "during your turn" window, CR 500.1 / CR 109.5). The turn gate
///   is supplied as a <c>isControllersTurn</c> predicate (backed in production
///   by <see cref="TurnManager.ActivePlayer"/> == controller — same active-player
///   read posture as <see cref="BrineborneCutthroatFactory"/>). Directly mirrors
///   <see cref="GhituLavarunnerFactory"/>'s Layer-6 conditional Haste grant; only
///   the gate (turn rather than graveyard threshold) and the granted keyword
///   (First strike rather than Haste) differ. The keyword surfaces through
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFirstStrike"/> when the
///   card's <see cref="Permanent.ActiveEffects"/> is wired, so the creature
///   strikes first only on its controller's turn (correct for an attacker; the
///   keyword is absent while defending on an opponent's turn — exactly the
///   printed asymmetry).
/// - <b>Dies trigger (CR 603.1 + CR 700.4)</b>: a single
///   <see cref="TriggeredAbility"/> fires on <see cref="CardMovedEvent"/> with
///   FromZone = Battlefield + ToZone = Graveyard when the moved card is a
///   creature AND is NOT Spiteful Prankster itself ("another creature" —
///   CR 603.1 explicitly excludes the source, unlike Blood Artist's self-or-other
///   union). On resolution it deals 1 damage to the chosen player/planeswalker
///   via <see cref="Fx.DealDamageAny(object, int)"/> (a planeswalker target
///   converts to loyalty removal, CR 306.8 — same damage shape as
///   <see cref="ViashinoPyromancerFactory"/>).
///
/// ## Targeting
///
/// "target player or planeswalker" (CR 115.1 — creatures excluded). The trigger
/// carries a 1..1 <see cref="TargetRequest"/> whose CandidateGatherer surfaces
/// every live player plus every planeswalker on any battlefield (agent-driven
/// production path, identical to Viashino Pyromancer). For deterministic unit
/// tests an optional <paramref name="targetResolver"/> short-circuits the agent
/// pick; when both are absent the damage silently no-ops (shape-test path).
///
/// ## Self-trigger / active-zone posture
///
/// Because the trigger reads "ANOTHER creature" it never fires on the
/// Prankster's own death, so — unlike the self-naming Blood Artist / Falkenrath
/// Noble — it does NOT need to remain active in the graveyard. ActiveZones is
/// Battlefield only (CR 603.6a — the trigger is live only while the Prankster is
/// on the battlefield).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. No continuous-effects service
///   (the conditional first strike does not surface) and no target resolver /
///   trigger registration. Card is structurally correct (3/2, Devil, {2}{R},
///   owner/controller). This is the overload <see cref="NamedCardFactory"/>
///   dispatches to.
/// - <see cref="Create(Player, Func{bool}?, Func{Player?}?, ContinuousEffectsService?, IEventBus?, TriggerManager?)"/>
///   — fully wired.
/// </summary>
[CardName("Spiteful Prankster")]
public static class SpitefulPranksterFactory
{
    public const string CardName = "Spiteful Prankster";
    public const string Slug = "spiteful-prankster";
    public const int Power = 3;
    public const int Toughness = 2;

    /// <summary>CR 119 — fixed 1 damage on each death trigger.</summary>
    public const int DamageAmount = 1;

    /// <summary>Conditional keyword granted "during your turn". The exact
    /// casing matches <see cref="Majik.Core.Combat.CombatAbilities.HasFirstStrike"/>'s
    /// computed-keyword lookup ("First strike").</summary>
    public const string FirstStrike = "First strike";

    /// <summary>
    /// Construct Spiteful Prankster with no live wiring. The conditional
    /// first-strike static is NOT attached (no continuous-effects service) and
    /// the dies trigger is attached for shape inspection but not registered with
    /// a <see cref="TriggerManager"/>; no target resolver is wired (so the damage
    /// side no-ops). Card shape (name, type, subtype, mana cost, P/T) is fully
    /// correct. This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, isControllersTurn: null, targetResolver: null,
            effects: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Spiteful Prankster with optional runtime services.
    /// <paramref name="isControllersTurn"/> gates the conditional first-strike
    /// static ("during your turn"); in production it returns
    /// <c>TurnManager.ActivePlayer == controller</c>. <paramref name="effects"/>
    /// registers the Layer-6 keyword static (and, with
    /// <paramref name="eventBus"/>, drives its ETB/LTB lifecycle).
    /// <paramref name="targetResolver"/> supplies the chosen player the dies
    /// trigger damages on resolution (short-circuits the agent pick for tests).
    /// <paramref name="triggers"/> registers the dies trigger so the bus drives
    /// it automatically.
    /// </summary>
    public static Creature Create(
        Player owner,
        Func<bool>? isControllersTurn,
        Func<Player?>? targetResolver,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Devil,
        // {2}{R}, 3/2). The JSON carries no abilities — both behaviours are
        // layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "During your turn, this creature has first strike."
        // CR 613.1f — Layer 6 (ability-adding) / CR 702.7 — First strike.
        // The "as long as it's your turn" gate re-evaluates live each layer
        // pass (CR 613.2), so the keyword appears on the controller's turn
        // and lifts on an opponent's turn.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            var lifecycle = new FirstStrikeStaticLifecycle(
                card, effects, eventBus, isControllersTurn ?? (() => true));
            lifecycle.Attach();
        }

        // ----------------------------------------------------------------
        // "Whenever another creature dies, this creature deals 1 damage to
        //  target player or planeswalker." CR 603.1 + CR 700.4.
        // "ANOTHER creature" excludes Spiteful Prankster itself (reference
        // inequality on the moved card). Damage routes through
        // Fx.DealDamageAny so a planeswalker target converts to loyalty
        // removal (CR 306.8). ActiveZones is Battlefield only — the trigger
        // never reads the Prankster's own death, so it needs no graveyard
        // active zone (contrast Blood Artist / Falkenrath Noble).
        // ----------------------------------------------------------------
        var diesCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.FromZone != ZoneType.Battlefield) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            if (!e.Card.HasType(CardType.Creature)) return false;
            // "another" — exclude Spiteful Prankster itself (CR 603.1).
            return !ReferenceEquals(e.Card, card);
        });

        TriggeredAbility? diesTrigger = null;
        var damageEffect = new Effect(
            $"{CardName}: deal {DamageAmount} damage to target player or planeswalker",
            () =>
            {
                // Deterministic test path: explicit resolver wins.
                object? target = targetResolver?.Invoke();

                // Production path: read the agent-chosen target.
                if (target == null && diesTrigger != null)
                {
                    var chosen = diesTrigger.ChosenTargets;
                    if (chosen.Count > 0 && chosen[0].Count > 0)
                        target = chosen[0][0];
                }

                // CR 608.2b — only Player / Planeswalker are legal targets.
                if (target is Player || target is Planeswalker)
                    Fx.DealDamageAny(target, DamageAmount);
            });

        diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: diesCondition,
            effects: new IEffect[] { damageEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player or planeswalker",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Burn,
                    // CR 115.1 — legal targets are players and planeswalkers
                    // only (creatures excluded). Every live player plus every
                    // planeswalker on any battlefield; the resolve-time
                    // Player/Planeswalker gate (CR 608.2b) further validates.
                    CandidateGatherer: ctx =>
                    {
                        var candidates = new List<object>(ctx.AllPlayers);
                        candidates.AddRange(ctx.AllPlayers
                            .SelectMany(p => p.Zones.Battlefield.GetCards())
                            .OfType<Planeswalker>());
                        return candidates;
                    }),
            });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }

    // -----------------------------------------------------------------------
    // ConditionalFirstStrikeStaticEffect — Layer 6 turn-gated keyword grant.
    // -----------------------------------------------------------------------

    /// <summary>
    /// CR 613.1f (Layer 6 — ability-adding) / CR 702.7 (First strike) — grants
    /// "First strike" to its <see cref="Creature"/> source while that source is
    /// on the battlefield AND it is the controller's turn (the "during your turn"
    /// window). The turn gate is read live each layer pass via the supplied
    /// predicate, so the keyword appears on the controller's turn and lifts on an
    /// opponent's turn. Mirrors
    /// <see cref="GhituLavarunnerFactory.GhituHasteStaticEffect"/>; only the gate
    /// and granted keyword differ.
    /// </summary>
    public sealed class ConditionalFirstStrikeStaticEffect : ContinuousEffect
    {
        private readonly Creature _source;
        private readonly Func<bool> _isControllersTurn;

        public ConditionalFirstStrikeStaticEffect(
            Creature source, Func<bool> isControllersTurn)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _isControllersTurn = isControllersTurn
                ?? throw new ArgumentNullException(nameof(isControllersTurn));
        }

        /// <summary>CR 613.1g — the permanent generating this effect.</summary>
        public override Permanent? Source => _source;

        /// <inheritdoc/>
        public override Layer Layer => Layer.Abilities;

        /// <summary>Active only while the Prankster is on the battlefield AND it
        /// is the controller's turn — so the keyword is absent from the computed
        /// set on an opponent's turn (CR 702.7 / CR 613.7c).</summary>
        public override bool IsActive() =>
            _source.Zone == ZoneType.Battlefield && _isControllersTurn();

        /// <summary>The static grants the keyword to its own source only.</summary>
        public override bool AppliesTo(Creature creature) =>
            ReferenceEquals(creature, _source);

        /// <summary>CR 702.7 — grant First strike.</summary>
        public override void Apply(CreatureCharacteristics chars)
        {
            chars.Keywords.Add(FirstStrike);
        }
    }

    // -----------------------------------------------------------------------
    // FirstStrikeStaticLifecycle — ETB/LTB wiring for the conditional static.
    // -----------------------------------------------------------------------

    /// <summary>
    /// ETB/LTB lifecycle binder for Spiteful Prankster's conditional first-strike
    /// static. Subscribes to <see cref="CardMovedEvent"/>; registers the Layer-6
    /// keyword grant when the Prankster enters the battlefield, unregisters when
    /// it leaves. Mirrors <see cref="GhituLavarunnerFactory"/>'s lifecycle binder.
    /// </summary>
    private sealed class FirstStrikeStaticLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Func<bool> _isControllersTurn;
        private readonly Action<CardMovedEvent> _handler;
        private ConditionalFirstStrikeStaticEffect? _registered;
        private bool _attached;

        public FirstStrikeStaticLifecycle(
            Creature source,
            ContinuousEffectsService effects,
            IEventBus? eventBus,
            Func<bool> isControllersTurn)
        {
            _source = source;
            _effects = effects;
            _eventBus = eventBus;
            _isControllersTurn = isControllersTurn;
            _handler = OnEvent;
        }

        public void Attach()
        {
            if (_attached) return;
            _attached = true;
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
            if (shouldBeActive && _registered == null)
            {
                _registered = new ConditionalFirstStrikeStaticEffect(
                    _source, _isControllersTurn);
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
