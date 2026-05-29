using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sundown Pass (Streets of New Capenna R/W slowland).
///
/// R/W slowland. Oracle text:
///   "This land enters tapped unless you control two or more other lands.
///    {T}: Add {R} or {W}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/sundown-pass.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. Two mana
/// abilities only — the conditional ETB-tapped is handled by the binder
/// layer in the production path.
///
/// ## Implemented elsewhere
/// - <b>Conditional ETB-tapped ("two or more other lands")</b>: CR 614.1c
///   replacement effect, handled at the binder layer
///   (<see cref="Majik.Core.CardData.ConditionalEntersTappedBinder"/>),
///   which already matches the "N or [more|fewer] other lands" form. The
///   slowland clause is the "more" direction — Sundown Pass enters untapped
///   only once the controller already has two or more other lands.
///   Production card-load path via
///   <see cref="Majik.Core.CardData.ScryfallCardFactory"/>; this named-card
///   factory builds the land without the replacement (test convenience).
/// </summary>
[CardName("Sundown Pass")]
public static class SundownPassFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("sundown-pass");

    /// <summary>
    /// Construct Sundown Pass owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
