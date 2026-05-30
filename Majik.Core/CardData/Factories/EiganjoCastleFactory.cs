using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Eiganjo Castle (Champions of Kamigawa).
///
/// Legendary Land.
/// Oracle text:
///   "{T}: Add {W}.
///    {W}, {T}: Prevent the next 2 damage that would be dealt to target
///     legendary creature this turn."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/eiganjo-castle.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. Structural
/// twin of <see cref="MinamoSchoolAtWatersEdgeFactory"/> (Legendary Land,
/// mana ability + a "{cost}, {T}: do-thing-to-target-legendary" activated
/// ability):
/// <list type="bullet">
///   <item>{T}: Add {W} — a <see cref="Majik.Core.Abilities.ManaAbility"/>.</item>
///   <item>{W}, {T}: Prevent the next 2 damage to target legendary creature
///   this turn — an <see cref="Majik.Core.Abilities.ActivatedAbility"/> whose
///   costs are a ManaCostCost({W}) + a tap-self additional cost (the {T}
///   symbol).</item>
/// </list>
///
/// ## Deferred (v1 gap — matches the existing stub pattern)
/// - <b>Prevent-damage-target effect — target selection + shield
///   registration</b>: the effect is the <c>prevent_damage_target_stub</c>
///   JSON variant, a no-op closure that mirrors Minamo's
///   <c>untap_target_stub</c> and Boseiju's <c>destroy_target_stub</c>. The
///   prevention shield itself (CR 615 — a per-turn pool of prevented damage
///   points) is supported by the engine via
///   <see cref="Majik.Core.Effects.PreventNextNDamageToAnyTargetShield"/>;
///   the gap is purely target selection via the prompt system. When
///   targeting lands, the stub upgrades to a real
///   <c>prevent_damage_target</c> (register the 2-point shield against the
///   chosen legendary creature) without breaking the JSON.
/// </summary>
[CardName("Eiganjo Castle")]
public static class EiganjoCastleFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("eiganjo-castle");

    /// <summary>Construct Eiganjo Castle for the supplied owner. The
    /// prevent-damage-target effect resolves as a no-op stub in v1 (see
    /// class xmldoc — CR 615 shield exists; targeting is deferred).</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
