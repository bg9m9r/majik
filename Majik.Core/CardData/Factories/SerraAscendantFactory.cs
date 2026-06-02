using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Serra Ascendant (Magic 2011 / reprints, {W}).
/// Creature — Human Monk 1/1. Oracle text (verified against Scryfall
/// 2026-06):
///   "Lifelink (Damage dealt by this creature also causes you to gain that
///    much life.)
///    As long as you have 30 or more life, this creature gets +5/+5 and has
///    flying."
///
/// Mechanically a conditional-self-buff sibling of <see cref="LoamLionFactory"/>
/// / <see cref="InventorsApprenticeFactory"/> (the "+X/+Y as long as &lt;cond&gt;"
/// Layer-7c self-pump shape), extended with (a) a printed Lifelink keyword
/// marker and (b) a Layer-6 conditional ability grant (Flying) sharing the
/// same predicate as the pump. The predicate is "you have 30 or more life"
/// rather than the "you control a Forest / artifact" predicate of those cards.
///
/// Base shape (name, Creature, Human/Monk subtypes, {W}, 1/1) is materialised
/// from the embedded JSON definition (<c>serra-ascendant.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The Lifelink keyword marker and
/// the conditional buff are layered on top here.
///
/// ## Implemented (v1)
///
/// - 1/1 Creature — Human Monk at {W}, owner/controller wired (from JSON).
/// - <b>Lifelink (CR 702.15)</b>: a <see cref="KeywordAbility"/> marker,
///   always present (same posture as <see cref="TrainedCaracalFactory"/>).
///   <see cref="Effects.ContinuousEffectsService.Compute"/> seeds printed
///   <see cref="KeywordAbility"/> markers into the computed keyword set, so
///   Lifelink survives even when a continuous-effects service is wired.
/// - <b>Life-threshold buff (CR 613 — Layer 7c P/T + Layer 6 ability)</b>:
///   while the controller's life total is 30 or more, Serra Ascendant gets
///   +5/+5 (a <see cref="LifeThresholdPumpStaticEffect"/> at
///   <see cref="Layer.PT_Modify"/>) and has Flying (a
///   <see cref="LifeThresholdFlyingStaticEffect"/> at
///   <see cref="Layer.Abilities"/>). The predicate re-evaluates on every
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> pass: gaining
///   life across the 30 threshold flips both on; dropping below 30 flips both
///   off — no trigger / re-register cycle required (CR 613.7c — an "as long
///   as" continuous effect gates its contribution on the predicate, it does
///   not unregister).
///
/// ## Lifecycle
///
/// ETB/LTB lifecycle mirrors <see cref="LoamLionFactory"/>: subscribe to
/// <see cref="CardMovedEvent"/>; register both static effects when Serra
/// Ascendant enters the battlefield, unregister when it leaves. The
/// <c>IsActive</c> battlefield gate on each effect is belt-and-braces
/// redundancy if no event bus is supplied.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. Lifelink marker is attached;
///   the conditional buff is NOT (no continuous-effects service). Card is
///   structurally correct (1/1, Human Monk, Lifelink, owner/controller) but
///   the +5/+5 / flying buff doesn't fire without a continuous-effects
///   service. This is the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ContinuousEffectsService?, IEventBus?)"/> —
///   fully wired. Both effects register on ETB and unregister on LTB.
///
/// ## Life-threshold semantics (CR 119.1)
///
/// "You have 30 or more life" reads the controller's current
/// <see cref="Player.LifeTotal"/> live (>= 30). Reads
/// <see cref="Permanent.Controller"/> live so a control-changing effect on
/// Serra Ascendant routes the life check through the NEW controller (CR
/// 109.5) — the buff follows whoever currently controls it.
/// </summary>
[CardName("Serra Ascendant")]
public static class SerraAscendantFactory
{
    public const string CardName = "Serra Ascendant";
    public const string Slug = "serra-ascendant";
    public const int Power = 1;
    public const int Toughness = 1;
    public const int LifeThreshold = 30;
    public const int BonusPower = 5;
    public const int BonusToughness = 5;

    /// <summary>
    /// Construct Serra Ascendant with no continuous-effects wiring. The
    /// Lifelink keyword marker IS attached; the +5/+5 / flying conditional
    /// buff is NOT (no continuous-effects service). Card shape (name, type,
    /// subtypes, mana cost, P/T) is fully correct. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Construct Serra Ascendant with optional runtime services. When
    /// <paramref name="effects"/> is supplied a
    /// <see cref="LifeThresholdPumpStaticEffect"/> (+5/+5) and a
    /// <see cref="LifeThresholdFlyingStaticEffect"/> (Flying) register so the
    /// conditional buff is evaluated on every
    /// <see cref="ContinuousEffectsService.Compute"/> invocation. When
    /// <paramref name="eventBus"/> is also supplied the lifecycle binder
    /// subscribes to <see cref="CardMovedEvent"/> so the effects register on
    /// ETB and unregister on LTB.
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Human/
        // Monk subtypes, {W}, 1/1). The JSON carries no abilities — the
        // Lifelink marker and conditional buff are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.15 — Lifelink. KeywordAbility marker consumed by the standard
        // combat-damage life-gain pipeline. Seeded into the computed keyword
        // set by ContinuousEffectsService.Compute (printed-keyword seed) so it
        // survives even when a continuous-effects service is wired. Same
        // posture as Trained Caracal.
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        if (effects != null)
        {
            var lifecycle = new LifeThresholdBuffLifecycle(card, effects, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// CR 119.1 — "you have 30 or more life" reads the controller's current
    /// life total (>= 30).
    /// </summary>
    public static bool HasLifeThreshold(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.LifeTotal >= LifeThreshold;
    }

    // -----------------------------------------------------------------------
    // LifeThresholdPumpStaticEffect — Layer 7c conditional +5/+5.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Layer 7c continuous effect for Serra Ascendant's life-threshold pump.
    /// On every <see cref="ContinuousEffectsService.Compute"/> invocation the
    /// effect tests whether the controller has 30 or more life and, if so,
    /// applies +5/+5 to Serra Ascendant. Below 30 the effect contributes
    /// nothing (CR 613.7c — an "as long as" continuous effect gates its
    /// application on the predicate; it does not unregister, but its
    /// <see cref="AppliesTo"/> returns true and <see cref="Apply"/>
    /// contributes 0 when the predicate is false).
    ///
    /// Active only while Serra Ascendant is on the battlefield
    /// (<see cref="IsActive"/> gate — belt-and-braces alongside the ETB/LTB
    /// lifecycle wiring in <see cref="LifeThresholdBuffLifecycle"/>).
    /// </summary>
    public sealed class LifeThresholdPumpStaticEffect : ContinuousEffect
    {
        private readonly Creature _source;

        public LifeThresholdPumpStaticEffect(Creature source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>CR 613.1g — the permanent generating this effect.</summary>
        public override Permanent? Source => _source;

        /// <inheritdoc/>
        public override Layer Layer => Layer.PT_Modify;

        /// <summary>Active while Serra Ascendant is on the battlefield.</summary>
        public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

        /// <summary>Applies only to Serra Ascendant itself.</summary>
        public override bool AppliesTo(Creature creature) =>
            ReferenceEquals(creature, _source);

        /// <summary>
        /// Apply +5/+5 when the controller has 30 or more life; otherwise no
        /// contribution. Reads <see cref="Permanent.Controller"/> live so a
        /// control-changing effect on Serra Ascendant routes the life check
        /// through the new controller (CR 109.5).
        /// </summary>
        public override void Apply(CreatureCharacteristics chars)
        {
            var controller = _source.Controller;
            if (controller == null) return;
            if (!HasLifeThreshold(controller)) return;
            chars.Power += BonusPower;
            chars.Toughness += BonusToughness;
        }
    }

    // -----------------------------------------------------------------------
    // LifeThresholdFlyingStaticEffect — Layer 6 conditional Flying grant.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Layer 6 (ability-adding, CR 613.3) continuous effect for Serra
    /// Ascendant's life-threshold Flying grant. On every
    /// <see cref="ContinuousEffectsService.Compute"/> invocation the effect
    /// tests whether the controller has 30 or more life and, if so, adds the
    /// Flying keyword (CR 702.9) to the computed keyword set. Below 30 the
    /// keyword is not added — so dropping below 30 removes Flying with no extra
    /// wiring (same shape as <see cref="HexproofWhileUntappedEffect"/>'s
    /// untapped-gated Hexproof grant).
    ///
    /// Active only while Serra Ascendant is on the battlefield
    /// (<see cref="IsActive"/> gate — the predicate is checked in
    /// <see cref="Apply"/> rather than here so the effect stays attached and
    /// re-evaluated across life-total changes without being unregistered).
    /// </summary>
    public sealed class LifeThresholdFlyingStaticEffect : ContinuousEffect
    {
        private readonly Creature _source;

        public LifeThresholdFlyingStaticEffect(Creature source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>CR 613.1g — the permanent generating this effect.</summary>
        public override Permanent? Source => _source;

        /// <inheritdoc/>
        public override Layer Layer => Layer.Abilities;

        /// <summary>Active while Serra Ascendant is on the battlefield.</summary>
        public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

        /// <summary>Applies only to Serra Ascendant itself.</summary>
        public override bool AppliesTo(Creature creature) =>
            ReferenceEquals(creature, _source);

        /// <summary>
        /// Grant Flying when the controller has 30 or more life; otherwise no
        /// contribution. Reads <see cref="Permanent.Controller"/> live (CR
        /// 109.5).
        /// </summary>
        public override void Apply(CreatureCharacteristics chars)
        {
            var controller = _source.Controller;
            if (controller == null) return;
            if (!HasLifeThreshold(controller)) return;
            chars.Keywords.Add("Flying");
        }
    }

    // -----------------------------------------------------------------------
    // LifeThresholdBuffLifecycle — ETB/LTB wiring for both buff effects.
    // -----------------------------------------------------------------------

    /// <summary>
    /// ETB/LTB lifecycle binder for Serra Ascendant's life-threshold buff.
    /// Subscribes to <see cref="CardMovedEvent"/>; registers both the +5/+5
    /// pump and the Flying grant when Serra Ascendant enters the battlefield,
    /// unregisters both when it leaves. Mirrors <see cref="LoamLionFactory"/>'s
    /// <c>ForestPumpLifecycle</c>, extended to manage two effects.
    /// </summary>
    private sealed class LifeThresholdBuffLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<CardMovedEvent> _handler;
        private LifeThresholdPumpStaticEffect? _pump;
        private LifeThresholdFlyingStaticEffect? _flying;
        private bool _attached;

        public LifeThresholdBuffLifecycle(
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
            if (shouldBeActive && _pump == null)
            {
                _pump = new LifeThresholdPumpStaticEffect(_source);
                _flying = new LifeThresholdFlyingStaticEffect(_source);
                _effects.Register(_pump);
                _effects.Register(_flying);
            }
            else if (!shouldBeActive && _pump != null)
            {
                _effects.Unregister(_pump);
                if (_flying != null) _effects.Unregister(_flying);
                _pump = null;
                _flying = null;
            }
        }
    }
}
