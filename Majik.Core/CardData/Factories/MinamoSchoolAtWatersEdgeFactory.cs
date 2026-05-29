using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Minamo, School at Water's Edge
/// (Champions of Kamigawa).
///
/// Legendary Land.
/// Oracle text:
///   "{T}: Add {U}.
///    {U}, {T}: Untap target legendary permanent."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/minamo-school-at-waters-edge.json</c> and
/// lets <see cref="CardDefinitionFactory"/> build the runtime card:
/// <list type="bullet">
///   <item>{T}: Add {U} — a <see cref="Majik.Core.Abilities.ManaAbility"/>.</item>
///   <item>{U}, {T}: Untap target legendary permanent — an
///   <see cref="Majik.Core.Abilities.ActivatedAbility"/> whose costs are a
///   ManaCostCost({U}) + a tap-self additional cost (the {T} symbol).</item>
/// </list>
///
/// ## Deferred (v1 gap — matches the existing stub pattern)
/// - <b>Untap-target effect — target selection + actual untap</b>: the
///   effect is the <c>untap_target_stub</c> JSON variant, a no-op closure
///   that mirrors Boseiju's <c>destroy_target_stub</c> and Walking
///   Ballista's <c>deal_damage_stub</c>. The untap action itself
///   (CR 701.21) is supported by the engine; the gap is purely target
///   selection via the prompt system. When targeting lands, the stub
///   upgrades to a real <c>untap_target</c> without breaking the JSON.
/// </summary>
[CardName("Minamo, School at Water's Edge")]
public static class MinamoSchoolAtWatersEdgeFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("minamo-school-at-waters-edge");

    /// <summary>Construct Minamo for the supplied owner. The untap-target
    /// effect resolves as a no-op stub in v1 (see class xmldoc).</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
