using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Riptide Laboratory (Onslaught).
///
/// Land. Oracle text (Scryfall-confirmed):
///   "{T}: Add {C}.
///    {1}{U}, {T}: Return target Wizard you control to its owner's hand."
///
/// ## Shape source
/// The entire card — the colorless <c>{T}: Add {C}</c> mana ability
/// (CR 605.1a) and the <c>{1}{U}, {T}: Return target Wizard you control to its
/// owner's hand</c> activated ability (CR 602) — is fully expressed in
/// <c>Majik.Core/CardData/Cards/riptide-laboratory.json</c> and built
/// data-driven through <see cref="CardDefinitionFactory"/>. The bounce is a
/// <c>return_to_hand</c> effect (<see cref="ReturnToHandEffectDef"/>) with
/// <c>targetFilter: "wizard_you_control"</c>: it declares a single target
/// request whose CandidateGatherer enumerates only battlefield creatures with
/// the Wizard subtype (CR 205.3m) controlled by the resolving player (the "you
/// control" rider, CR 109.5). At resolution it reads the chosen permanent off
/// <see cref="Majik.Core.Abilities.ResolutionContext.ChosenTargets"/> and
/// returns it to its owner's hand (CR 701.11). CR 608.2b — an illegal/absent
/// target at resolution fizzles cleanly with no bounce performed.
///
/// ## Production / test parity
/// This is a thin wrapper around the JSON definition — the same posture as the
/// structural twin <see cref="KarakasFactory"/> (a land whose
/// <c>{T}: Return target [filter] creature to its owner's hand</c> activated
/// ability runs through the identical <c>return_to_hand</c> effect path). The
/// dispatcher / test path (<see cref="NamedCardFactory.Create"/>) routes here.
/// Adding this factory flips <c>IsImplemented</c> automatically via the
/// <see cref="ImplementedCardNames"/> registry.
/// </summary>
[CardName("Riptide Laboratory")]
public static class RiptideLaboratoryFactory
{
    public const string CardName = "Riptide Laboratory";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("riptide-laboratory");

    /// <summary>Construct Riptide Laboratory owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
