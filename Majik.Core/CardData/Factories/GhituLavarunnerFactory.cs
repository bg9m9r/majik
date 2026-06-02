using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ghitu Lavarunner (Dominaria, {R}). Creature — Human
/// Wizard 1/2. Oracle text (verified against Scryfall 2026-06):
///   "As long as there are two or more instant and/or sorcery cards in your
///    graveyard, this creature gets +1/+0 and has haste. (It can attack and
///    {T} as soon as it comes under your control.)"
///
/// The base shape (name, Creature, Human Wizard subtypes, {R}, 1/2) is
/// materialised from the embedded JSON definition (<c>ghitu-lavarunner.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The conditional static is layered
/// on top here — the JSON <see cref="AbilityDefinition"/> schema expresses
/// neither a graveyard-count-gated P/T pump nor a conditional keyword grant, so
/// it lives in the factory (same posture as <see cref="LoamLionFactory"/> for
/// the conditional self-pump and <see cref="VoiceOfTheBlessedFactory"/> for the
/// conditional keyword grant, both of which this cribs).
///
/// ## Implemented (v1)
///
/// - 1/2 <see cref="Creature"/> — Human Wizard at {R}, owner/controller wired
///   (from JSON).
/// - <b>Conditional +1/+0 (CR 613.7c — Layer 7c)</b>: a
///   <see cref="GhituPumpStaticEffect"/> applies +1/+0 to Lavarunner while the
///   controller has two or more instant/sorcery cards in their own graveyard.
///   The count re-evaluates live each layer pass (CR 613.2 read semantics for
///   the "as long as" gate — CR 613.7c), so the bonus appears the moment the
///   second qualifying card lands in the graveyard and lifts if the count drops
///   below two.
/// - <b>Conditional Haste (CR 613.1f — Layer 6 / CR 702.10)</b>: a separate
///   <see cref="GhituHasteStaticEffect"/> grants "Haste" under the identical
///   gate. <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/> reads the
///   layer-computed keyword set when the card's
///   <see cref="Permanent.ActiveEffects"/> is wired, so the granted Haste lets
///   the creature attack / tap despite summoning sickness (CR 302.6) exactly
///   while the threshold is met.
///
///   Two effects (not one) because each <see cref="ContinuousEffect"/> carries a
///   single <see cref="Layer"/> — the +1/+0 is Layer 7c, the Haste grant is
///   Layer 6. Both share the same <see cref="ThresholdMet"/> gate so they flip
///   together.
///
/// ## Lifecycle
///
/// ETB/LTB lifecycle mirrors <see cref="LoamLionFactory"/>: subscribe to
/// <see cref="CardMovedEvent"/>; register both static effects when Lavarunner
/// enters the battlefield, unregister when it leaves. Each effect's
/// <see cref="ContinuousEffect.IsActive"/> battlefield gate is belt-and-braces
/// redundancy if no event bus is supplied.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. No layer effects; card is
///   structurally correct (1/2, Human Wizard, {R}, owner/controller) but the
///   conditional pump + haste don't fire without a continuous-effects service.
///   This is the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ContinuousEffectsService?, IEventBus?)"/> —
///   fully wired. Both statics register on ETB and unregister on LTB.
///
/// ## "your graveyard" semantics (CR 109.5, CR 404.2)
///
/// Cards in a player's graveyard are always owned by that player (CR 404.2 —
/// cards go to their owner's graveyard); the count helper additionally filters
/// by <c>Owner == controller</c> as belt-and-braces (CR 109.5 — "you" / "your"
/// refer to the controller). The gate scans the controller's graveyard ONLY
/// (not exile, hand, or any other zone).
/// </summary>
[CardName("Ghitu Lavarunner")]
public static class GhituLavarunnerFactory
{
    public const string CardName = "Ghitu Lavarunner";
    public const string Slug = "ghitu-lavarunner";

    /// <summary>CR 122.1-style threshold — "two or more instant and/or sorcery
    /// cards in your graveyard".</summary>
    public const int Threshold = 2;

    /// <summary>Conditional power bonus (+1/+0).</summary>
    public const int BonusPower = 1;

    /// <summary>
    /// Construct Ghitu Lavarunner with no live wiring. The conditional pump +
    /// haste statics are NOT attached (no continuous-effects service). Card
    /// shape (name, type, subtypes, mana cost, P/T) is fully correct. This is
    /// the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Construct Ghitu Lavarunner with optional runtime services. When
    /// <paramref name="effects"/> is supplied the conditional +1/+0 (Layer 7c)
    /// and Haste grant (Layer 6) register so they are evaluated on every
    /// <see cref="ContinuousEffectsService.Compute"/> invocation. When
    /// <paramref name="eventBus"/> is also supplied the lifecycle binder
    /// subscribes to <see cref="CardMovedEvent"/> so the effects register on ETB
    /// and unregister on LTB.
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Human
        // Wizard, {R}, 1/2). The JSON carries no abilities — the conditional
        // static is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        if (effects != null)
        {
            var lifecycle = new GhituStaticLifecycle(card, effects, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Count instant + sorcery cards in <paramref name="cards"/> owned by
    /// <paramref name="owner"/> (CR 109.5 — "you" = controller; "your
    /// graveyard" restricts to the controller's cards). Pure helper exposed for
    /// tests; mirrors the scan baked into the live static effects.
    /// </summary>
    public static int CountInstantsAndSorceries(IEnumerable<ICard> cards, Player owner)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(owner);

        var count = 0;
        foreach (var card in cards)
        {
            if (!ReferenceEquals(card.Owner, owner)) continue;
            if (card.HasType(CardType.Instant) || card.HasType(CardType.Sorcery)) count++;
        }
        return count;
    }

    /// <summary>
    /// CR 109.5 — "your graveyard" reads against the live controller. True when
    /// the controller owns two or more instant/sorcery cards in their own
    /// graveyard. Read fresh on every layer pass (CR 613.2).
    /// </summary>
    internal static bool ThresholdMet(Creature source)
    {
        var controller = source.Controller;
        if (controller == null) return false;
        return CountInstantsAndSorceries(controller.Zones.Graveyard.GetCards(), controller)
            >= Threshold;
    }

    // -----------------------------------------------------------------------
    // GhituPumpStaticEffect — Layer 7c conditional +1/+0 self-pump.
    // -----------------------------------------------------------------------

    /// <summary>
    /// CR 613.7c (Layer 7c — P/T modification) — applies +1/+0 to its
    /// <see cref="Creature"/> source while that source is on the battlefield AND
    /// the controller has two or more instant/sorcery cards in their graveyard
    /// (<see cref="ThresholdMet"/>). The gate re-evaluates live each layer pass,
    /// so the bonus appears the moment the threshold is reached and lifts if the
    /// graveyard drops below two qualifying cards.
    /// </summary>
    public sealed class GhituPumpStaticEffect : ContinuousEffect
    {
        private readonly Creature _source;

        public GhituPumpStaticEffect(Creature source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>CR 613.1g — the permanent generating this effect.</summary>
        public override Permanent? Source => _source;

        /// <inheritdoc/>
        public override Layer Layer => Layer.PT_Modify;

        /// <summary>Active while Lavarunner is on the battlefield. The threshold
        /// is checked in <see cref="Apply"/> so the layer cache still tracks the
        /// effect even when the bonus currently contributes 0 (CR 613.7c — the
        /// "as long as" gate suppresses the contribution, not the effect).</summary>
        public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

        /// <summary>Applies only to Lavarunner itself.</summary>
        public override bool AppliesTo(Creature creature) =>
            ReferenceEquals(creature, _source);

        /// <summary>Apply +1/+0 when the controller has two or more
        /// instant/sorcery cards in their graveyard; otherwise no
        /// contribution.</summary>
        public override void Apply(CreatureCharacteristics chars)
        {
            if (!ThresholdMet(_source)) return;
            chars.Power += BonusPower;
        }
    }

    // -----------------------------------------------------------------------
    // GhituHasteStaticEffect — Layer 6 conditional Haste grant.
    // -----------------------------------------------------------------------

    /// <summary>
    /// CR 613.1f (Layer 6 — ability-adding) / CR 702.10 (Haste) — grants "Haste"
    /// to its <see cref="Creature"/> source while that source is on the
    /// battlefield AND the controller has two or more instant/sorcery cards in
    /// their graveyard (<see cref="ThresholdMet"/>). Read live each layer pass so
    /// the keyword appears / lifts with the threshold; the IsActive gate folds
    /// the threshold in so the keyword set is empty below it.
    /// </summary>
    public sealed class GhituHasteStaticEffect : ContinuousEffect
    {
        private readonly Creature _source;

        public GhituHasteStaticEffect(Creature source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>CR 613.1g — the permanent generating this effect.</summary>
        public override Permanent? Source => _source;

        /// <inheritdoc/>
        public override Layer Layer => Layer.Abilities;

        /// <summary>Active only while Lavarunner is on the battlefield AND the
        /// graveyard threshold is met — so the Haste keyword is absent from the
        /// computed keyword set below the threshold (CR 702.10 / CR 613.7c).</summary>
        public override bool IsActive() =>
            _source.Zone == ZoneType.Battlefield && ThresholdMet(_source);

        /// <summary>The static grants the keyword to its own source only.</summary>
        public override bool AppliesTo(Creature creature) =>
            ReferenceEquals(creature, _source);

        /// <summary>CR 702.10 — grant Haste.</summary>
        public override void Apply(CreatureCharacteristics chars)
        {
            chars.Keywords.Add("Haste");
        }
    }

    // -----------------------------------------------------------------------
    // GhituStaticLifecycle — ETB/LTB wiring for both conditional statics.
    // -----------------------------------------------------------------------

    /// <summary>
    /// ETB/LTB lifecycle binder for Ghitu Lavarunner's conditional statics.
    /// Subscribes to <see cref="CardMovedEvent"/>; registers the +1/+0 pump
    /// (Layer 7c) and the Haste grant (Layer 6) when Lavarunner enters the
    /// battlefield, unregisters both when it leaves. Mirrors
    /// <see cref="LoamLionFactory"/>'s lifecycle binder.
    /// </summary>
    private sealed class GhituStaticLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<CardMovedEvent> _handler;
        private GhituPumpStaticEffect? _pump;
        private GhituHasteStaticEffect? _haste;
        private bool _attached;

        public GhituStaticLifecycle(
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
                _pump = new GhituPumpStaticEffect(_source);
                _haste = new GhituHasteStaticEffect(_source);
                _effects.Register(_pump);
                _effects.Register(_haste);
            }
            else if (!shouldBeActive && _pump != null)
            {
                _effects.Unregister(_pump);
                if (_haste != null) _effects.Unregister(_haste);
                _pump = null;
                _haste = null;
            }
        }
    }
}
