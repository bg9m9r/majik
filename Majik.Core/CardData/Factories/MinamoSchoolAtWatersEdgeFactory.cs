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
/// ## Implemented (PLAN 01 Slice F)
/// - <b>Untap-target effect — target selection + actual untap</b>: the
///   effect is a real <c>untap_target</c> verb declaring a 1..1 target
///   request over legendary permanents. The shared
///   <see cref="Majik.Core.Targeting.TargetCollection"/> pipeline prompts the
///   controller's agent, and the effect untaps the chosen permanent
///   (CR 701.21, <c>Permanent.Untap</c>) at resolution (CR 608.2b — illegal
///   target fizzles).
/// </summary>
[CardName("Minamo, School at Water's Edge")]
public static class MinamoSchoolAtWatersEdgeFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("minamo-school-at-waters-edge");

    /// <summary>Construct Minamo for the supplied owner. The untap-target
    /// effect untaps the chosen legendary permanent at resolution (Slice F).</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
