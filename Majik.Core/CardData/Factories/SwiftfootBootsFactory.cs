using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Swiftfoot Boots (Magic 2012 et al., {2}).
///
/// Artifact — Equipment. Oracle text (Scryfall, verified 2026-06-02):
///   "Equipped creature has hexproof and haste. (It can't be the target of
///    spells or abilities your opponents control. It can attack and {T} no
///    matter when it came under your control.)"
///   "Equip {1}"
///
/// ## Why a hand-rolled C# factory (not the JSON CardDefinition path)
///
/// The data-driven
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/> has
/// NO equip ability and NO attached keyword-grant, so a JSON def alone
/// produces only a vanilla Artifact shell. The functioning equipment
/// analogues (<see cref="ShadowspearFactory"/>, <see cref="LavaspurBootsFactory"/>,
/// <see cref="ColossusHammerFactory"/>) are hand-rolled for exactly this
/// reason, so Swiftfoot Boots follows that established pattern. The shipped
/// <c>swiftfoot-boots.json</c> mirrors <c>lavaspur-boots.json</c> (a bare
/// Artifact/Equipment shell — the abilities are added here).
///
/// ## Implementation
///
/// - <b>"Equipped creature has hexproof and haste."</b> — a single
///   Layer-6 ability-adding effect (CR 613.1f) via an
///   <see cref="AttachedBoostEffect"/> carrying <c>grantedKeywords</c>
///   = { "Hexproof", "Haste" } at <see cref="Layer.Abilities"/>, with no
///   P/T change (0/0). Same paired-grant shape as
///   <see cref="ShadowspearFactory"/>'s "has trample and lifelink". The
///   effect reads the source's <see cref="Permanent.AttachedTo"/>
///   dynamically and gates on the Boots being on the battlefield AND
///   attached (<see cref="AttachedBoostEffect.IsActive"/>), so re-equipping
///   transfers the grant and detach / LTB revokes it.
///     - <b>Hexproof</b> (CR 702.11) — the granted "Hexproof" marker is
///       projected onto the equipped creature's computed keyword set and is
///       consulted by <see cref="Majik.Core.Targeting.TargetLegality"/>
///       (which reads <c>ActiveEffects.Compute(c).Keywords</c>), so the
///       equipped creature can't be targeted by opponents' spells/abilities.
///     - <b>Haste</b> (CR 702.10) — the granted "Haste" marker is read by
///       <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/> through
///       the same computed keyword set.
/// - <b>Equip {1}</b> — activated ability (CR 702.6) via the shared
///   <see cref="EquipActivatedAbility"/> primitive, threading the
///   Puresteel-Paladin zero-equip cost-provider hook for cycle parity.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only (factory-shape / dispatch
/// tests). The keyword grant is not registered against any
/// <see cref="ContinuousEffectsService"/> on that path. Use the two-arg
/// overload to wire the continuous effects.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first
///   controller-side creature deterministically (same gap as the rest of
///   the equipment cycle).
/// </summary>
[CardName("Swiftfoot Boots")]
public static class SwiftfootBootsFactory
{
    public const string CardName = "Swiftfoot Boots";
    public const string Cost = "{2}";
    public const string EquipCost = "{1}";

    /// <summary>
    /// Constructs Swiftfoot Boots with no live continuous-effects wiring (the
    /// shape / dispatcher path). The hexproof / haste grant is NOT registered
    /// against any service. Suitable for factory-shape / dispatch tests.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs Swiftfoot Boots. When <paramref name="continuousEffects"/>
    /// is supplied the Hexproof / Haste grant (Layer 6) is registered against
    /// it; it gates on the Boots being on the battlefield AND attached to a
    /// battlefield permanent. When null, the grant is skipped.
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
        // "Equipped creature has hexproof and haste." A single
        // AttachedBoostEffect with no P/T change (0/0) granting the two
        // marker keywords at Layer 6 (CR 613.1f ability-adding). Same
        // paired-grant shape as Shadowspear's "has trample and lifelink".
        // The effect gates on the source being on the battlefield AND
        // attached (AttachedBoostEffect.IsActive).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(
                    source: card,
                    power: 0,
                    toughness: 0,
                    grantedKeywords: new[] { "Hexproof", "Haste" },
                    layer: Layer.Abilities));
        }

        // --------------------------------------------------------------
        // Equip {1} — standard equipment-cycle Equip activated ability
        // (CR 702.6) via the shared primitive. Threads the Puresteel
        // zero-cost provider hook.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }
}
