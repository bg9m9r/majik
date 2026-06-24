using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Basilisk Collar (Worldwake, {1}).
///
/// Artifact — Equipment. Oracle text (Scryfall, verified):
///   "Equipped creature has deathtouch and lifelink."
///   "Equip {2}"
///
/// ## Why a hand-rolled C# factory (not the JSON CardDefinition path)
///
/// The data-driven
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/> only
/// supports the effect/ability shapes enumerated in its dispatch (counters,
/// draw, scry/surveil, …). It has NO equip ability and NO attached
/// keyword-grant — a JSON def alone produces only a vanilla Artifact shell.
/// The shipped <c>basilisk-collar.json</c> therefore mirrors
/// <c>loxodon-warhammer.json</c> / <c>shadowspear.json</c>: name + types +
/// subtypes + cost only. The functioning behaviour is hand-rolled here, the
/// established pattern across the equipment cycle
/// (<see cref="ShadowspearFactory"/>, <see cref="LoxodonWarhammerFactory"/>,
/// <see cref="MaulOfTheSkyclavesFactory"/>).
///
/// ## Implementation
///
/// Mechanically identical in shape to <see cref="LoxodonWarhammerFactory"/>'s
/// "Equipped creature gets +3/+0 and has trample and lifelink" static line,
/// differing only in: no P/T boost (Basilisk Collar grants keywords only),
/// the granted keywords (Deathtouch + Lifelink, not Trample + Lifelink), and
/// the equip cost ({2}, not {3}).
///
/// - <b>Static "equipped creature has deathtouch and lifelink"</b> — a single
///   <see cref="AttachedBoostEffect"/> granting "Deathtouch" (CR 702.2) +
///   "Lifelink" (CR 702.15) at <see cref="Layer.Abilities"/> (CR 613.1c —
///   Layer 6 ability addition), with a zero P/T delta. Gates on the Collar
///   being on the battlefield AND attached (see
///   <see cref="AttachedBoostEffect.IsActive"/>).
///   <see cref="Majik.Core.Combat.CombatAbilities.HasDeathtouch"/> /
///   <see cref="Majik.Core.Combat.CombatAbilities.HasLifelink"/> read the
///   keyword markers off the equipped creature's working set.
/// - <b>Equip {2}</b> — activated ability (CR 702.6) via the shared
///   <see cref="EquipActivatedAbility"/> primitive with the Puresteel
///   zero-equip cost-provider hook for cycle parity. Sorcery-speed gate +
///   "creature you control" candidate gathering + attach-on-resolve are
///   encapsulated.
///
/// ## Lifecycle
///
/// When <paramref name="continuousEffects"/> is supplied, the Deathtouch /
/// Lifelink grant (Layer 6) is registered immediately; it gates on the Collar
/// being on the battlefield AND attached. The single-arg
/// <see cref="Create(Player)"/> overload omits all service wiring and produces
/// the correct card shape only — suitable for factory-shape / dispatch tests.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for the Equip activation — v1 picks the
///   first controller-side creature deterministically (same gap as the rest
///   of the equipment cycle).
/// </summary>
[CardName("Basilisk Collar")]
public static class BasiliskCollarFactory
{
    public const string CardName = "Basilisk Collar";
    public const string Cost = "{1}";
    public const string EquipCost = "{2}";

    /// <summary>
    /// Constructs Basilisk Collar with no live continuous-effects wiring
    /// (the shape / dispatcher path). The Deathtouch / Lifelink grant is NOT
    /// registered against any service. Suitable for unit / shape tests.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs Basilisk Collar. When <paramref name="continuousEffects"/>
    /// is supplied, the Deathtouch / Lifelink grant (Layer 6) is registered
    /// against it; it gates on the Collar being on the battlefield AND
    /// attached.
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
        // Static — "Equipped creature has deathtouch and lifelink."
        // A single AttachedBoostEffect with zero P/T delta granting the
        // two keywords at Layer 6 (CR 613.1c ability addition). Same
        // grant shape as Loxodon Warhammer's Trample / Lifelink line,
        // minus the P/T boost.
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(
                    source: card,
                    power: 0,
                    toughness: 0,
                    grantedKeywords: new[] { "Deathtouch", "Lifelink" },
                    layer: Layer.Abilities));
        }

        // --------------------------------------------------------------
        // Equip {2} — activated ability (CR 702.6) via the shared
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
