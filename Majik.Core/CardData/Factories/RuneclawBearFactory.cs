using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Runeclaw Bear (8th Edition and reprints, {1}{G}).
/// Creature — Bear 2/2. Oracle text (verified against Scryfall 2026-06):
/// empty — Runeclaw Bear is a functional reprint of <see cref="GrizzlyBearsFactory"/>
/// (identical cost {1}{G}, identical 2/2, identical Bear type, no printed
/// keywords, triggers, statics, or activated abilities). It is the
/// proverbial vanilla two-drop with a different name and art.
///
/// The card's entire shape (name, Creature type, Bear subtype, {1}{G}, 2/2)
/// is materialised from the embedded JSON definition (<c>runeclaw-bear.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Because the card is vanilla
/// there is no behaviour to layer on top — the factory is a thin wrapper that
/// builds the definition and wires owner/controller (handled by
/// <see cref="CardDefinitionFactory.Build"/>). CR 110 — a creature is a
/// permanent; no abilities means no further rules wiring is required.
///
/// NB: "Runeclaw Bear" is deliberately KEPT in both the inline switch arm of
/// <see cref="NamedCardFactory"/> and
/// <see cref="ImplementedCardNames.InlineFallbackNames"/>, exactly as
/// <see cref="GrizzlyBearsFactory"/> and <see cref="HillGiantFactory"/> do.
/// That keeps <see cref="ImplementedCardNames.HasRealFactory"/> returning
/// <c>false</c> for the name, so <c>GameFacade</c>'s "instance swap" rebuild
/// is NOT triggered for a directly-constructed Runeclaw Bear shell — which
/// would otherwise replace a test's <c>new Creature("Runeclaw Bear", "1G", 2, 2)</c>
/// with a JSON-built instance mid-cast and break the auto-tap cast path.
/// <c>IsImplemented</c> still reports true via the inline-fallback set
/// regardless.
/// </summary>
[CardName("Runeclaw Bear")]
public static class RuneclawBearFactory
{
    public const string CardName = "Runeclaw Bear";
    public const string Slug = "runeclaw-bear";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Runeclaw Bear from its embedded JSON definition. The card is
    /// fully shaped (name, Creature — Bear, {1}{G}, 2/2, owner/controller) by
    /// <see cref="CardDefinitionFactory.Build"/>; there is no ability to layer
    /// on. This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Creature)CardDefinitionFactory.Build(definition, owner);
    }
}
