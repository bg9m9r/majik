using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Heartless Summoning (Innistrad, {1}{B}).
///
/// Enchantment. Oracle text:
///   "Creature spells you cast cost {2} less to cast.
///    Creatures you control get -1/-1."
///
/// ## Implemented (v1)
/// - Card identity: Enchantment, mana cost {1}{B}, owner / controller wiring.
/// - <b>Creature-spell cost reduction (CR 117.7)</b>: registered as a
///   <see cref="SpellCostReductionAbility"/> with
///   <c>predicate: c =&gt; c.HasType(CardType.Creature)</c> and
///   <c>reduction: 2</c>. <see cref="CostReduction.GetEffectiveCost"/>
///   scans the caster's battlefield for these abilities at cost-calc time
///   and folds the {2} reduction into the spell's generic-mana cost.
///   Coloured pips are untouched (CR 117.7c) and the cost floors at zero.
///   Two copies of Heartless Summoning stack additively to {4} less.
/// - <b>Anthem (-1/-1) to all creatures you control</b>: registered as a
///   <see cref="ControllerCreatureAnthemEffect"/> static at Layer 7c
///   (CR 613.7c). Symmetric on the controller's side — every creature
///   they control gets -1/-1; opponents' creatures are unaffected.
///   <see cref="ContinuousEffect.IsActive"/> short-circuits when Heartless
///   Summoning isn't on the battlefield so the penalty lifts on LTB.
///   Heartless Summoning itself is an Enchantment (not a Creature), so
///   the includeSelf question doesn't apply.
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the registered effect stays on the
///   <see cref="ContinuousEffectsService"/> across zone changes;
///   <see cref="ContinuousEffect.IsActive"/> gates it off when the source
///   isn't on the battlefield, but a future Prune pass could drop the
///   entry. Same shape as Goblin Chieftain / Engineered Plague.
/// - <b>Control-change re-evaluation</b>: controller is captured at
///   register time. Mind Control on Heartless Summoning won't currently
///   flip the affected side.
/// </summary>
[CardName("Heartless Summoning")]
public static class HeartlessSummoningFactory
{
    public const string CardName = "Heartless Summoning";
    public const string Cost = "{1}{B}";

    /// <summary>
    /// Generic-mana reduction granted to creature spells the controller
    /// casts. Exposed for tests / docs.
    /// </summary>
    public const int CreatureCostReduction = 2;

    /// <summary>
    /// Construct Heartless Summoning without live continuous-effects
    /// wiring. The <see cref="SpellCostReductionAbility"/> is always wired
    /// on the card (it's consulted via the card's ability list by
    /// <see cref="CostReduction.GetEffectiveCost"/>); the -1/-1 anthem
    /// requires a live <see cref="ContinuousEffectsService"/> to take
    /// effect. Suitable for dispatcher / shape tests.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Heartless Summoning. When
    /// <paramref name="continuousEffects"/> is supplied, the -1/-1 anthem
    /// against the controller's creatures is registered against the layers
    /// service. The cost reducer is always wired on the card.
    /// </summary>
    public static Enchantment Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, Cost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 117.7 — "Creature spells you cast cost {2} less to cast."
        // SpellCostReductionAbility is scanned at cost-calc time by
        // CostReduction.GetEffectiveCost against the caster's battlefield.
        card.AddAbility(new SpellCostReductionAbility(
            predicate: c => c.HasType(CardType.Creature),
            reduction: (_, _) => CreatureCostReduction,
            description: "Creature spells you cast cost {2} less to cast."));

        if (continuousEffects != null)
        {
            // CR 613.7c — "Creatures you control get -1/-1." Layer 7c P/T
            // modification scoped to the source's controller. The custom
            // ContinuousEffect below covers the "no subtype filter" case
            // that LordStaticEffect doesn't (LordStaticEffect always
            // requires a matchingSubtype).
            continuousEffects.Register(new ControllerCreatureAnthemEffect(
                source: card, power: -1, toughness: -1));
        }

        return card;
    }
}

/// <summary>
/// CR 613.7c — generic "creatures you control get +P/+T" anthem. Differs
/// from <see cref="LordStaticEffect"/> in that there is no subtype filter:
/// every creature the source's controller controls (excluding the source
/// itself if it happens to be a creature) is affected. Used by Heartless
/// Summoning (-1/-1) and conceptually fits Glorious Anthem / Honor of the
/// Pure / Crusade-shaped cards.
///
/// <para>While the source is on the battlefield, every creature controlled
/// by the source's controller (excluding the source itself) receives the
/// P/T delta. <see cref="IsActive"/> short-circuits when the source leaves
/// the battlefield (CR 614), so the bonus lifts on LTB.</para>
/// </summary>
public sealed class ControllerCreatureAnthemEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly int _power;
    private readonly int _toughness;
    private readonly bool _includeSelf;
    private readonly Majik.Core.ValueObjects.ManaColor? _requiredColor;
    private readonly System.Func<Majik.Core.ValueObjects.ManaColor?>? _colorProvider;

    /// <summary>
    /// Construct an anthem.
    /// </summary>
    /// <param name="source">The permanent generating the effect (typically
    /// the enchantment).</param>
    /// <param name="power">P delta (negative for penalties like Heartless
    /// Summoning's -1/-1).</param>
    /// <param name="toughness">T delta.</param>
    /// <param name="includeSelf">If the source IS a creature, whether to
    /// apply the bonus to itself. Defaults to false (Glorious Anthem
    /// shape — though it's an enchantment so the question is moot).</param>
    /// <param name="requiredColor">Optional FIXED colour gate (CR 105 / CR 613.7c).
    /// When non-null, only creatures whose printed colour set
    /// (<see cref="Majik.Core.Cards.CardColors.GetColors"/>) contains this
    /// colour are affected — the "White creatures you control get +1/+1"
    /// (Honor of the Pure / Crusade) shape. Printed colour is used rather
    /// than effective colour because GetEffectiveColors re-enters the layer
    /// service and would recurse during layer evaluation. Null keeps the
    /// all-creatures behaviour (Glorious Anthem / Heartless Summoning).</param>
    /// <param name="colorProvider">Optional DYNAMIC colour gate (CR 614.12) —
    /// the "creatures you control OF THE CHOSEN COLOR get +N/+M" shape
    /// (Heraldic Banner). When supplied it is consulted at every layer
    /// evaluation so the live chosen colour (stamped onto the source's
    /// <see cref="Majik.Core.CardData.ColorChoice"/> holder as it entered)
    /// drives membership; a null return from the provider means "no colour
    /// chosen yet → restrict to nothing". When supplied it takes precedence
    /// over <paramref name="requiredColor"/> (it reflects the resolved
    /// decision). Like the fixed gate it reads PRINTED colour
    /// (<see cref="Majik.Core.Cards.CardColors.GetColors"/>) to avoid
    /// re-entering the layer service.</param>
    public ControllerCreatureAnthemEffect(
        Permanent source,
        int power,
        int toughness,
        bool includeSelf = false,
        Majik.Core.ValueObjects.ManaColor? requiredColor = null,
        System.Func<Majik.Core.ValueObjects.ManaColor?>? colorProvider = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _power = power;
        _toughness = toughness;
        _includeSelf = includeSelf;
        _requiredColor = requiredColor;
        _colorProvider = colorProvider;
    }

    public override Layer Layer => Layer.PT_Modify;

    /// <summary>CR 613.1g — the permanent generating this effect.</summary>
    public override Permanent? Source => _source;

    public override bool IsActive() =>
        _source.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature)
    {
        if (creature.Zone != Majik.Core.Zones.ZoneType.Battlefield) return false;
        if (!ReferenceEquals(creature.Controller, _source.Controller)) return false;
        if (!_includeSelf && ReferenceEquals(creature, _source)) return false;
        // CR 105 / CR 613.7c — optional colour gate ("White creatures you
        // control"). Use the printed/static colour derivation
        // (CardColors.GetColors) rather than GetEffectiveColors() here: the
        // latter re-enters the layer service (Compute → AppliesTo →
        // GetEffectiveColors) and would recurse infinitely while the layers
        // are mid-evaluation. Reading printed colour avoids the cycle.
        // Deferred (v1 gap): a Layer-5 colour changer (e.g. a creature turned
        // white) is not reflected by this gate. Null means no restriction.
        //
        // CR 614.12 — the DYNAMIC colour gate (Heraldic Banner's "of the chosen
        // color") wins when supplied: it reads the live chosen colour each
        // evaluation. A null provider result means no colour has been chosen
        // yet, so the anthem restricts to nothing (no creature qualifies).
        var gateColor = _colorProvider != null ? _colorProvider() : _requiredColor;
        if (_colorProvider != null && gateColor == null)
        {
            return false;
        }
        if (gateColor != null
            && !Majik.Core.Cards.CardColors.GetColors(creature).Contains(gateColor.Value))
        {
            return false;
        }
        return true;
    }

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power += _power;
        chars.Toughness += _toughness;
    }

    /// <summary>
    /// Sim-only: reconstruct an identical <see cref="ControllerCreatureAnthemEffect"/>
    /// bound to <paramref name="clonedSource"/> for the search-sandbox clone.
    /// All filtering reads clonedSource.Controller live (correctly remapped).
    /// preserves: _power, _toughness, _includeSelf, _requiredColor, _colorProvider; source → clonedSource.
    /// The dynamic _colorProvider closure is shared by reference — it reads the
    /// per-card ColorChoice holder, which is stable across the sim clone (the
    /// chosen colour is fixed once the permanent has entered, CR 614.12).
    /// </summary>
    internal override ContinuousEffect? CloneForSim(
        Permanent clonedSource,
        System.Func<System.Collections.Generic.IReadOnlyList<Majik.Core.Players.Player>>? clonedPlayers)
        => new ControllerCreatureAnthemEffect(
            source:        clonedSource,
            power:         _power,
            toughness:     _toughness,
            includeSelf:   _includeSelf,
            requiredColor: _requiredColor,
            colorProvider: _colorProvider);
}
