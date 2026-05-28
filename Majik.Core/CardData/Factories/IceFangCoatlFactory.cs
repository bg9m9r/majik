using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ice-Fang Coatl (Modern Horizons, {G}{U}).
///
/// Snow Creature — Snake 1/1. Oracle text:
///   "Flash.
///    Flying.
///    When this creature enters, draw a card.
///    This creature has deathtouch as long as you control at least
///    three other snow permanents."
///
/// ## Implementation
///
/// - 1/1 Snow Creature — Snake, mana cost {G}{U}. Color identity green+blue
///   (both pips per CR 202.2c). Snow supertype (CR 205.4d) applied via
///   <c>supertypes: new[] { CardSupertype.Snow }</c>, same pattern as
///   <see cref="SnowCoveredForestFactory"/> which applies Basic + Snow.
///
/// - <b>Flash</b> (CR 702.8) — wired as a <see cref="KeywordAbility"/> marker,
///   same shape as <see cref="VendilionCliqueFactory"/>. Lets the Coatl be
///   cast at instant speed.
///
/// - <b>Flying</b> (CR 702.9) — wired as a <see cref="KeywordAbility"/> marker,
///   same shape as <see cref="CloudkinSeerFactory"/> and Mulldrifter.
///
/// - <b>ETB triggered ability</b> (CR 603.1, CR 603.6a): "When this creature
///   enters, draw a card." Unconditional self-ETB via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>. Resolves via
///   <see cref="Fx.DrawCards"/>(controller, 1). Active only from the battlefield
///   (CR 603.6a). Same structure as <see cref="CloudkinSeerFactory"/>.
///
/// - <b>Conditional Deathtouch</b> (CR 702.2 / CR 613.1f): "this creature has
///   deathtouch as long as you control at least three other snow permanents."
///   Implemented as a <see cref="ConditionalDeathtouchEffect"/> registered on
///   the supplied <see cref="ContinuousEffectsService"/>. On every
///   <see cref="ContinuousEffectsService.Compute"/> pass the effect evaluates
///   the closure over the current battlefield snow count and conditionally adds
///   "Deathtouch" to <see cref="CreatureCharacteristics.Keywords"/>. The Coatl
///   itself is excluded from the count (oracle: "OTHER snow permanents").
///   <see cref="CombatAbilities.HasDeathtouch"/> reads the keyword via the
///   layer-system path when <see cref="Creature.ActiveEffects"/> is set
///   (CR 613 — characteristic-defining and continuous-effect keywords are
///   resolved via the service, not static markers alone).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. ETB trigger attached but not
///   registered; no <see cref="ContinuousEffectsService"/> bound, so the
///   conditional Deathtouch effect is absent. Suitable for dispatcher /
///   structural tests.
/// - <see cref="Create(Player, ContinuousEffectsService?, Func{IEnumerable{ICard}}?)"/>
///   — fully wired. When <paramref name="effects"/> is supplied, the
///   <see cref="ConditionalDeathtouchEffect"/> is registered and
///   <see cref="Creature.ActiveEffects"/> is bound so keyword lookups flow
///   through the layer system.
///
/// ## Design notes
/// The <c>battlefieldSnowSource</c> closure returns the OTHER snow permanents
/// controlled by the Coatl's controller on the battlefield. The closure is
/// evaluated live on every Compute, so the Deathtouch grant responds
/// dynamically as snow permanents enter/leave play. The Coatl's own Snow
/// supertype does NOT count toward the threshold — the caller is responsible
/// for excluding the Coatl from the source enumeration (mirrors Tarmogoyf's
/// <c>graveyardSource</c> closure convention).
/// </summary>
[CardName("Ice-Fang Coatl")]
public static class IceFangCoatlFactory
{
    public const string CardName = "Ice-Fang Coatl";
    public const string PrintedManaCost = "{G}{U}";
    public const int Power = 1;
    public const int Toughness = 1;
    public const int DrawAmount = 1;
    public const int SnowThreshold = 3;

    /// <summary>
    /// Construct Ice-Fang Coatl with no live wiring. ETB trigger is attached
    /// for shape inspection; not registered with any
    /// <see cref="TriggerManager"/>. No <see cref="ConditionalDeathtouchEffect"/>
    /// registered. Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, battlefieldSnowSource: null);

    /// <summary>
    /// Construct Ice-Fang Coatl with optional runtime services. When
    /// <paramref name="effects"/> and <paramref name="battlefieldSnowSource"/>
    /// are supplied:
    /// <list type="bullet">
    ///   <item><see cref="Creature.ActiveEffects"/> is bound so keyword lookups
    ///   flow through the layer system (CR 613).</item>
    ///   <item>A <see cref="ConditionalDeathtouchEffect"/> is registered; on
    ///   every Compute it evaluates the snow count via
    ///   <paramref name="battlefieldSnowSource"/> and conditionally adds
    ///   "Deathtouch" to the working-set keywords.</item>
    /// </list>
    /// The <paramref name="triggers"/> parameter is retained for callers that
    /// want the ETB draw registered on a live <see cref="TriggerManager"/>.
    /// </summary>
    /// <param name="owner">Card's owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service. Pass null for
    /// shape-only.</param>
    /// <param name="battlefieldSnowSource">Closure returning the OTHER snow
    /// permanents controlled by the Coatl's controller currently on the
    /// battlefield. The Coatl itself must be excluded by the caller. Evaluated
    /// live on every Compute. Pass null for shape-only.</param>
    /// <param name="triggers">Optional TriggerManager for registering the ETB
    /// draw trigger. Pass null to skip registration.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        Func<IEnumerable<ICard>>? battlefieldSnowSource,
        TriggerManager? triggers = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Snow },
            subtypes: new[] { CardSubtype.Snake });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Flash (CR 702.8). Allows casting at instant speed. Same wiring
        // shape as VendilionCliqueFactory.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // ----------------------------------------------------------------
        // Flying (CR 702.9). Evasion keyword marker. Same wiring shape as
        // CloudkinSeerFactory and Mulldrifter.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.1 / CR 603.6a).
        //   "When this creature enters, draw a card."
        // Unconditional self-ETB — no intervening-if (CR 603.4 does not
        // apply here). Routed through Fx.DrawCards so the replacement bus
        // + empty-library SBA flag fire correctly per CR 121.1 + CR 704.5b.
        // The controller closure re-resolves at execute time so blink /
        // control-change scenarios draw for the correct player.
        // ----------------------------------------------------------------
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: draw {DrawAmount} card (when this creature enters)",
            () =>
            {
                var controller = card.Controller ?? owner;
                Fx.DrawCards(controller, DrawAmount);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Conditional Deathtouch (CR 702.2 / CR 613.1f).
        //   "This creature has deathtouch as long as you control at least
        //    three other snow permanents."
        //
        // Implemented as a continuous effect registered on the supplied
        // ContinuousEffectsService. The effect applies in Layer 6 (ability
        // grant — CR 613.1f) and conditionally adds "Deathtouch" to the
        // creature's working-set keywords when the snow-permanent count ≥
        // SnowThreshold (3). The source permanent is the Coatl itself;
        // IsActive() gates on Battlefield zone (CR 603.6a — characteristic-
        // modifying effects are only live while the source is on the
        // battlefield). CombatAbilities.HasDeathtouch reads via the layer-
        // system path because Creature.ActiveEffects is bound.
        // ----------------------------------------------------------------
        if (effects != null && battlefieldSnowSource != null)
        {
            card.ActiveEffects = effects;

            var dtEffect = new ConditionalDeathtouchEffect(
                card,
                battlefieldSnowSource,
                SnowThreshold);

            effects.Register(dtEffect);
        }

        return card;
    }

    /// <summary>
    /// CR 613.1f / CR 702.2 — Layer 6 continuous effect that grants
    /// Deathtouch to Ice-Fang Coatl while ≥ <c>threshold</c> OTHER snow
    /// permanents are controlled by the Coatl's controller and the Coatl
    /// is on the battlefield.
    ///
    /// Evaluated live on every <see cref="ContinuousEffectsService.Compute"/>
    /// pass via the <c>snowSource</c> closure. The Coatl itself must be
    /// excluded from the closure by the caller.
    /// </summary>
    public sealed class ConditionalDeathtouchEffect : ContinuousEffect
    {
        private readonly Creature _coatl;
        private readonly Func<IEnumerable<ICard>> _snowSource;
        private readonly int _threshold;

        public ConditionalDeathtouchEffect(
            Creature coatl,
            Func<IEnumerable<ICard>> snowSource,
            int threshold)
        {
            _coatl = coatl ?? throw new ArgumentNullException(nameof(coatl));
            _snowSource = snowSource ?? throw new ArgumentNullException(nameof(snowSource));
            _threshold = threshold;
        }

        /// <summary>CR 613.1f — Layer 6: ability adding.</summary>
        public override Layer Layer => Layer.Abilities;

        public override Permanent? Source => _coatl;

        /// <summary>
        /// Active only while the Coatl is on the battlefield (CR 613 —
        /// continuous effects from permanents are only active while that
        /// permanent is on the battlefield).
        /// </summary>
        public override bool IsActive() => _coatl.Zone == ZoneType.Battlefield;

        public override bool AppliesTo(Creature creature) =>
            ReferenceEquals(creature, _coatl);

        public override void Apply(CreatureCharacteristics chars)
        {
            // Count the OTHER snow permanents supplied by the closure.
            // The Coatl itself must not appear in the source — the oracle
            // text says "at least three OTHER snow permanents."
            var snowCount = _snowSource()
                .Count(c => c.HasSupertype(CardSupertype.Snow)
                            && c.Zone == ZoneType.Battlefield);

            if (snowCount >= _threshold)
            {
                chars.Keywords.Add("Deathtouch");
            }
        }
    }
}
