using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Loxodon Warhammer (Mirrodin, {3}).
///
/// Artifact — Equipment. Oracle text (Scryfall, verified):
///   "Equipped creature gets +3/+0 and has trample and lifelink."
///   "Equip {3}"
///
/// ## Why a hand-rolled C# factory (not the JSON CardDefinition path)
///
/// The data-driven
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/> only
/// supports the effect/ability shapes enumerated in its dispatch (counters,
/// draw, scry/surveil, …). It has NO equip ability, NO dynamic attached-boost
/// effect and NO attached keyword-grant — a JSON def alone produces only a
/// vanilla Artifact shell. The shipped <c>loxodon-warhammer.json</c> therefore
/// mirrors <c>maul-of-the-skyclaves.json</c> / <c>lavaspur-boots.json</c>:
/// name + types + subtypes + cost only. The functioning behaviour is
/// hand-rolled here, the established pattern across the equipment cycle
/// (<see cref="ShadowspearFactory"/>, <see cref="BonesplitterFactory"/>,
/// <see cref="MaulOfTheSkyclavesFactory"/>).
///
/// ## Implementation
///
/// Mechanically identical in shape to <see cref="ShadowspearFactory"/>'s
/// "Equipped creature gets +1/+1 and has trample and lifelink" static line,
/// differing only in the boost magnitude (+3/+0), the equip cost ({3}), the
/// absence of the legendary supertype, and the absence of Shadowspear's
/// keyword-strip activated ability.
///
/// - <b>Static "equipped creature gets +3/+0 and has trample and lifelink"</b>
///   — two <see cref="AttachedBoostEffect"/> instances:
///     - +3/+0 P/T boost (CR 613 Layer 7c) — reads
///       <see cref="Permanent.AttachedTo"/> dynamically, so re-equipping
///       transfers the boost without re-registration.
///     - Granted "Trample" + "Lifelink" (CR 613.1c — Layer 6 ability
///       addition) via a parallel <see cref="AttachedBoostEffect"/> with
///       <c>grantedKeywords</c> registered at <see cref="Layer.Abilities"/>.
///       <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/> /
///       <see cref="Majik.Core.Combat.CombatAbilities.HasLifelink"/> read the
///       keyword markers off the equipped creature's working set.
///   Both gate on the Warhammer being on the battlefield AND attached
///   (see <see cref="AttachedBoostEffect.IsActive"/>).
/// - <b>Equip {3}</b> — activated ability (CR 702.6) via the shared
///   <see cref="EquipActivatedAbility"/> primitive with the Puresteel
///   zero-equip cost-provider hook for cycle parity. Sorcery-speed gate +
///   "creature you control" candidate gathering + attach-on-resolve are
///   encapsulated.
///
/// ## Lifecycle
///
/// When <paramref name="continuousEffects"/> is supplied, the +3/+0 boost
/// (Layer 7c) and the Trample / Lifelink grant (Layer 6) are registered
/// immediately; each gates on the Warhammer being on the battlefield AND
/// attached. The single-arg <see cref="Create(Player)"/> overload omits all
/// service wiring and produces the correct card shape only — suitable for
/// factory-shape / dispatch tests.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for the Equip activation — v1 picks the
///   first controller-side creature deterministically (same gap as the rest
///   of the equipment cycle).
/// </summary>
[CardName("Loxodon Warhammer")]
public static class LoxodonWarhammerFactory
{
    public const string CardName = "Loxodon Warhammer";
    public const string Cost = "{3}";
    public const string EquipCost = "{3}";

    /// <summary>
    /// Constructs Loxodon Warhammer with no live continuous-effects wiring
    /// (the shape / dispatcher path). The +3/+0 boost and Trample / Lifelink
    /// grant are NOT registered against any service. Suitable for unit /
    /// shape tests.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs Loxodon Warhammer. When <paramref name="continuousEffects"/>
    /// is supplied, the +3/+0 boost (Layer 7c) and the Trample / Lifelink
    /// grant (Layer 6) are registered against it; both gate on the Warhammer
    /// being on the battlefield AND attached.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: Cost,
            subtypes: new[] { CardSubtype.Equipment });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------
        // Static — "Equipped creature gets +3/+0 and has trample and
        // lifelink." Two AttachedBoostEffects: Layer 7c for the +3/+0,
        // Layer 6 for the granted keywords (CR 613.7c + CR 613.1c). Same
        // paired-effect shape as Shadowspear's "+1/+1 and has trample and
        // lifelink."
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 3, toughness: 0));

            continuousEffects.Register(
                new AttachedBoostEffect(
                    source: card,
                    power: 0,
                    toughness: 0,
                    grantedKeywords: new[] { "Trample", "Lifelink" },
                    layer: Layer.Abilities));
        }

        // --------------------------------------------------------------
        // Equip {3} — activated ability (CR 702.6) via the shared
        // EquipActivatedAbility primitive with the Puresteel zero-cost
        // provider hook.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }
}
