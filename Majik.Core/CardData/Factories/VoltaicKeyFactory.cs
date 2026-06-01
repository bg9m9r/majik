using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Voltaic Key (Urza's Legacy / Mirrodin, {1}).
///
/// Artifact.
/// Oracle text:
///   "{1}, {T}: Untap target artifact."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/voltaic-key.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card:
/// <list type="bullet">
///   <item>{1}, {T}: Untap target artifact — an
///   <see cref="Majik.Core.Abilities.ActivatedAbility"/> whose costs are a
///   ManaCostCost({1}) + a tap-self additional cost (the {T} symbol).</item>
/// </list>
///
/// Same shape as <see cref="MinamoSchoolAtWatersEdgeFactory"/>
/// ("{U}, {T}: Untap target legendary permanent"); the only differences are
/// the generic {1} cost (vs {U}) and the <c>artifact</c> target filter
/// (vs <c>legendary_permanent</c>).
///
/// ## Implemented (PLAN 01 Slice F)
/// - <b>Untap-target effect — target selection + actual untap</b>: the
///   effect is a real <c>untap_target</c> verb declaring a 1..1 target
///   request over <c>artifact</c> candidates. The shared
///   <see cref="Majik.Core.Targeting.TargetCollection"/> pipeline prompts the
///   controller's agent, and the effect untaps the chosen artifact
///   (CR 701.21) at resolution (CR 608.2b — illegal target fizzles).
/// </summary>
[CardName("Voltaic Key")]
public static class VoltaicKeyFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("voltaic-key");

    /// <summary>Construct Voltaic Key for the supplied owner. The
    /// untap-target effect untaps the chosen artifact at resolution
    /// (Slice F).</summary>
    public static Artifact Create(Player owner) =>
        (Artifact)CardDefinitionFactory.Build(Definition, owner);
}
