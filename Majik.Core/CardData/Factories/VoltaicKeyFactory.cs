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
/// ## Deferred (v1 gap — matches the existing stub pattern)
/// - <b>Untap-target effect — target selection + actual untap</b>: the
///   effect is the <c>untap_target_stub</c> JSON variant, a no-op closure
///   that mirrors Minamo's <c>untap_target_stub</c> and Boseiju's
///   <c>destroy_target_stub</c>. The untap action itself (CR 701.21) is
///   supported by the engine; the gap is purely target selection via the
///   prompt system. When targeting lands, the stub upgrades to a real
///   <c>untap_target</c> without breaking the JSON.
/// </summary>
[CardName("Voltaic Key")]
public static class VoltaicKeyFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("voltaic-key");

    /// <summary>Construct Voltaic Key for the supplied owner. The
    /// untap-target effect resolves as a no-op stub in v1 (see class
    /// xmldoc).</summary>
    public static Artifact Create(Player owner) =>
        (Artifact)CardDefinitionFactory.Build(Definition, owner);
}
