using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Seachrome Coast (Phyrexia: All Will Be One reprint
/// of the Scars of Mirrodin W/U fastland cycle).
///
/// W/U fastland. Oracle text:
///   "This land enters tapped unless you control two or fewer other lands.
///    {T}: Add {W} or {U}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/seachrome-coast.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. Two mana
/// abilities only — the conditional ETB-tapped is handled by the binder
/// layer in the production path. Same fastland cycle as Spirebluff Canal,
/// Inspiring Vantage, Concealed Courtyard, Botanical Sanctum, Blooming
/// Marsh.
///
/// ## Implemented elsewhere
/// - <b>Conditional ETB-tapped ("two or fewer other lands")</b>: handled
///   at the binder layer
///   (<see cref="Majik.Core.CardData.ConditionalEntersTappedBinder"/>),
///   which already matches the "N or fewer / more other lands" form
///   (Rule 614 replacement effect). Production card-load path via
///   <see cref="Majik.Core.CardData.ScryfallCardFactory"/>; this
///   named-card factory builds the land without the replacement (test
///   convenience), mirroring <see cref="SpirebluffCanalFactory"/>.
/// </summary>
[CardName("Seachrome Coast")]
public static class SeachromeCoastFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("seachrome-coast");

    /// <summary>
    /// Construct Seachrome Coast owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
