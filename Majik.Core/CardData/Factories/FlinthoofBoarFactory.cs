using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Flinthoof Boar (Magic 2013 / Modern, {1}{G}).
/// Creature — Boar 2/2. Oracle text (verified against Scryfall 2026-06):
///   "This creature gets +1/+1 as long as you control a Mountain.
///    {R}: This creature gains haste until end of turn."
///
/// Base shape (name, Creature, Boar subtype, {1}{G}, 2/2) is materialised
/// from the embedded JSON definition (<c>flinthoof-boar.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours —
/// the Mountain-conditional pump and the {R} self-haste grant — are layered
/// on here because the JSON <c>AbilityDefinition</c> schema doesn't yet
/// express "as long as you control a land subtype" statics.
///
/// ## Implemented (v1)
///
/// - 2/2 Creature — Boar at {1}{G}, owner/controller wired (from JSON).
/// - <b>Mountain-conditional pump (CR 613.7c — Layer 7c)</b>: a
///   <see cref="MountainSelfPumpStaticEffect"/> registers against the
///   supplied <see cref="ContinuousEffectsService"/>. On every
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> for Flinthoof
///   Boar the effect tests whether the controller controls at least one
///   Mountain and applies +1/+1 when so. The condition re-evaluates
///   dynamically: a Mountain ETBing flips the bonus on, the last Mountain
///   leaving (or being retyped off Mountain) flips it back off — no trigger
///   / re-register cycle required. Directly mirrors
///   <see cref="KirdApeFactory.ForestSelfPumpStaticEffect"/> /
///   <see cref="LoamLionFactory.ForestSelfPumpStaticEffect"/>; the only
///   differences are the predicate ("control a Mountain" rather than "control
///   a Forest") and the bonus (+1/+1 rather than +1/+2).
/// - <b>{R}: gains haste until end of turn (CR 702.10 / CR 613.1c — Layer
///   6)</b>: an ordinary <see cref="ActivatedAbility"/> (CR 602; uses the
///   stack) with a {R} <see cref="ManaCostCost"/>. On resolution it registers
///   a <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting Haste, which
///   expires in the cleanup step (CR 514). Same self-grant shape as
///   <see cref="WerewolfPackLeaderFactory"/>'s {3}{G} EOT animate.
///
/// ## Lifecycle
///
/// ETB/LTB lifecycle for the Mountain pump mirrors <see cref="KirdApeFactory"/>:
/// subscribe to <see cref="CardMovedEvent"/>; register the static effect when
/// the Boar enters the battlefield, unregister when it leaves. The
/// <see cref="MountainSelfPumpStaticEffect.IsActive"/> battlefield gate is
/// belt-and-braces redundancy if no event bus is supplied.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. No layer-7c pump (no
///   continuous-effects service); the {R} haste ability is attached but its
///   resolution no-ops without an <see cref="ContinuousEffectsService"/>.
///   Card is structurally correct (2/2, Boar, owner/controller). This is the
///   overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ContinuousEffectsService?, IEventBus?)"/> —
///   fully wired. The pump registers on ETB and unregisters on LTB, and the
///   {R} ability registers the EOT haste grant on resolution.
///
/// ## Mountain-count semantics (CR 109.5, CR 305.6)
///
/// "Control a Mountain" reads true when the controller controls at least one
/// permanent with the <see cref="CardSubtype.Mountain"/> land subtype —
/// includes basic Mountains, dual lands typed Mountain, and any non-land
/// permanent retyped to Mountain (CR 305.6 — the predicate tests the subtype
/// directly so it stays robust).
/// </summary>
[CardName("Flinthoof Boar")]
public static class FlinthoofBoarFactory
{
    public const string CardName = "Flinthoof Boar";
    public const string Slug = "flinthoof-boar";
    public const int Power = 2;
    public const int Toughness = 2;
    public const int MountainBonusPower = 1;
    public const int MountainBonusToughness = 1;

    /// <summary>{R} haste-grant activation cost. CR 602.</summary>
    public const string HasteCost = "{R}";

    /// <summary>Keyword granted by the {R} ability. CR 702.10.</summary>
    public const string Haste = "Haste";

    /// <summary>
    /// Construct Flinthoof Boar with no live wiring. The Mountain conditional
    /// pump is NOT attached (no continuous-effects service); the {R} haste
    /// ability is attached but no-ops on resolution without an effects
    /// service. Card shape (name, type, subtype, mana cost, P/T) is fully
    /// correct. This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Construct Flinthoof Boar with optional runtime services. When
    /// <paramref name="effects"/> is supplied a
    /// <see cref="MountainSelfPumpStaticEffect"/> registers so the +1/+1
    /// conditional pump is evaluated on every
    /// <see cref="ContinuousEffectsService.Compute"/> invocation, and the {R}
    /// ability registers the EOT haste grant on resolution. When
    /// <paramref name="eventBus"/> is also supplied the lifecycle binder
    /// subscribes to <see cref="CardMovedEvent"/> so the pump registers on ETB
    /// and unregisters on LTB.
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Boar
        // subtype, {1}{G}, 2/2). The JSON carries no abilities — the two
        // printed behaviours are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // "This creature gets +1/+1 as long as you control a Mountain."
        // CR 613.7c — Layer 7c conditional self-pump.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            var lifecycle = new MountainPumpLifecycle(card, effects, eventBus);
            lifecycle.Attach();
        }

        // ----------------------------------------------------------------
        // "{R}: This creature gains haste until end of turn." CR 602 —
        // ordinary activated ability (uses the stack). Cost = {R}, no tap.
        // Resolution registers a Layer 6 EOT-expirable Haste grant
        // (CR 702.10 / CR 613.1c). No-ops when no effects service is wired.
        // ----------------------------------------------------------------
        var hasteEffect = new Effect(
            $"{CardName}: gains haste until end of turn",
            () =>
            {
                var svc = card.ActiveEffects ?? effects;
                svc?.Register(new GrantKeywordUntilEndOfTurnEffect(card, Haste));
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(HasteCost) },
            effects: new IEffect[] { hasteEffect }));

        return card;
    }

    /// <summary>
    /// CR 109.5 — "you control" reads each permanent's current controller.
    /// True when the controller controls at least one permanent with the
    /// Mountain land subtype.
    /// </summary>
    public static bool ControlsMountain(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        foreach (var c in controller.Zones.Battlefield.GetCards())
        {
            if (c.HasSubtype(CardSubtype.Mountain)) return true;
        }
        return false;
    }

    // -----------------------------------------------------------------------
    // MountainSelfPumpStaticEffect — Layer 7c conditional self-pump.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Layer 7c continuous effect for Flinthoof Boar's Mountain pump. On every
    /// <see cref="ContinuousEffectsService.Compute"/> invocation the effect
    /// tests whether the controller controls a Mountain and, if so, applies
    /// +1/+1 to the Boar. Without a Mountain the effect contributes nothing
    /// (CR 613.7c — a continuous effect that reads "as long as" gates its
    /// application on the predicate; it does not unregister, but its
    /// <see cref="Apply"/> contributes 0 when the predicate is false).
    ///
    /// Active only while the Boar is on the battlefield
    /// (<see cref="IsActive"/> gate — belt-and-braces alongside the ETB/LTB
    /// lifecycle wiring in <see cref="MountainPumpLifecycle"/>).
    /// </summary>
    public sealed class MountainSelfPumpStaticEffect : ContinuousEffect
    {
        private readonly Creature _source;

        public MountainSelfPumpStaticEffect(Creature source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>CR 613.1g — the permanent generating this effect.</summary>
        public override Permanent? Source => _source;

        /// <inheritdoc/>
        public override Layer Layer => Layer.PT_Modify;

        /// <summary>Active while Flinthoof Boar is on the battlefield.</summary>
        public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

        /// <summary>Applies only to Flinthoof Boar itself.</summary>
        public override bool AppliesTo(Creature creature) =>
            ReferenceEquals(creature, _source);

        /// <summary>
        /// Apply +1/+1 when the controller controls a Mountain; otherwise no
        /// contribution. Reads <see cref="Permanent.Controller"/> live so a
        /// control-changing effect on the Boar routes the Mountain check
        /// through the new controller's battlefield (CR 109.5).
        /// </summary>
        public override void Apply(CreatureCharacteristics chars)
        {
            var controller = _source.Controller;
            if (controller == null) return;
            if (!ControlsMountain(controller)) return;
            chars.Power += MountainBonusPower;
            chars.Toughness += MountainBonusToughness;
        }

        /// <summary>
        /// Sim-only: reconstruct an identical <see cref="MountainSelfPumpStaticEffect"/>
        /// bound to <paramref name="clonedSource"/> for the search-sandbox clone.
        /// The controller is read live from clonedSource.Controller (correctly remapped).
        /// preserves: nothing scalar; source → clonedSource (as Creature).
        /// </summary>
        internal override ContinuousEffect? CloneForSim(
            Majik.Core.Cards.Permanent clonedSource,
            System.Func<System.Collections.Generic.IReadOnlyList<Majik.Core.Players.Player>>? clonedPlayers)
            => clonedSource is Majik.Core.Cards.Creature clonedCreature
                ? new MountainSelfPumpStaticEffect(clonedCreature)
                : null;
    }

    // -----------------------------------------------------------------------
    // MountainPumpLifecycle — ETB/LTB wiring for the Mountain pump effect.
    // -----------------------------------------------------------------------

    /// <summary>
    /// ETB/LTB lifecycle binder for Flinthoof Boar's Mountain pump. Subscribes
    /// to <see cref="CardMovedEvent"/>; registers
    /// <see cref="MountainSelfPumpStaticEffect"/> when the Boar enters the
    /// battlefield, unregisters when it leaves. Mirrors
    /// <see cref="KirdApeFactory"/>'s <c>ForestPumpLifecycle</c>.
    /// </summary>
    private sealed class MountainPumpLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<CardMovedEvent> _handler;
        private MountainSelfPumpStaticEffect? _registered;
        private bool _attached;

        public MountainPumpLifecycle(
            Creature source,
            ContinuousEffectsService effects,
            IEventBus? eventBus)
        {
            _source = source;
            _effects = effects;
            _eventBus = eventBus;
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
                _registered = new MountainSelfPumpStaticEffect(_source);
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
