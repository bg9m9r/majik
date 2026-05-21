using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spymaster's Vault (Bloomburrow).
///
/// Land. Oracle text:
///   "This land enters tapped unless you control a Swamp.
///    {T}: Add {B}.
///    {B}, {T}: Target creature you control connives X, where X is the number
///    of creatures that died this turn."
///
/// Now a thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/spymasters-vault.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card.
///
/// ## Implemented elsewhere
/// - <b>ETB-tapped restriction</b>: "enters tapped unless you control a Swamp"
///   is handled at the binder layer (<see cref="Majik.Core.CardData.SubtypeEntersTappedBinder"/>)
///   for the production card-load path via <see cref="Majik.Core.CardData.ScryfallCardFactory"/>.
///   This named-card factory builds the land without the replacement
///   (test convenience).
///
/// ## Deferred (v1 gaps)
/// - <b>Connive activated ability</b>: "{B}, {T}: Target creature you
///   control connives X" requires per-turn death-count tracking,
///   targeted activation, card-draw + forced discard with player
///   selection, and counter placement on a targeted permanent.
///   (CR 701.41: connive.) Not yet wired.
/// </summary>
public static class SpymastersVaultFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("spymasters-vault");

    /// <summary>
    /// Construct Spymaster's Vault owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
