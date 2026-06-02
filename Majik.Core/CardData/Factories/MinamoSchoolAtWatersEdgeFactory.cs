using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Minamo, School at Water's Edge (Champions of Kamigawa).
///
/// Legendary Land. Oracle text (Scryfall-confirmed):
///   "{T}: Add {U}.
///    {U}, {T}: Untap target legendary permanent."
///
/// ## Shape source
/// The entire card — Legendary supertype + Land type, the <c>{T}: Add {U}</c>
/// mana ability (CR 605.1a), and the <c>{U}, {T}: Untap target legendary
/// permanent</c> activated ability (CR 602) — is fully expressed in
/// <c>Majik.Core/CardData/Cards/minamo-school-at-waters-edge.json</c> and built
/// data-driven through <see cref="CardDefinitionFactory"/>. The untap ability is
/// an <c>untap_target</c> effect (<see cref="UntapTargetEffectDef"/>) with
/// <c>targetFilter: "legendary_permanent"</c>: it declares a single target
/// request and, at resolution, reads the chosen permanent off
/// <see cref="Majik.Core.Abilities.ResolutionContext.ChosenTargets"/> and untaps
/// it (CR 701.21). CR 608.2b — an illegal/absent target at resolution fizzles
/// with no untap performed.
///
/// ## Production / test parity
/// This is a thin wrapper around the JSON definition — the same posture as the
/// structural twin <see cref="EiganjoCastleFactory"/> (another Kamigawa
/// legendary land: mana ability + a <c>{cost}, {T}: do-thing-to-target</c>
/// activated ability). The dispatcher / test path
/// (<see cref="NamedCardFactory.Create"/>) routes here. Adding this factory
/// flips <c>IsImplemented</c> automatically via the
/// <see cref="ImplementedCardNames"/> registry.
/// </summary>
[CardName("Minamo, School at Water's Edge")]
public static class MinamoSchoolAtWatersEdgeFactory
{
    public const string CardName = "Minamo, School at Water's Edge";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("minamo-school-at-waters-edge");

    /// <summary>Construct Minamo, School at Water's Edge owned and controlled
    /// by <paramref name="owner"/>.</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
