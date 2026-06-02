using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Grizzly Bears (Alpha and many reprints, {1}{G}).
/// Creature — Bear 2/2. Oracle text (verified against Scryfall 2026-06):
/// empty — Grizzly Bears is the proverbial vanilla two-drop, the baseline
/// against which other 2/2s are measured. No printed keywords, triggers,
/// statics, or activated abilities.
///
/// The card's entire shape (name, Creature type, Bear subtype, {1}{G}, 2/2)
/// is materialised from the embedded JSON definition (<c>grizzly-bears.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Because the card is vanilla
/// there is no behaviour to layer on top — the factory is a thin wrapper that
/// builds the definition and wires owner/controller (handled by
/// <see cref="CardDefinitionFactory.Build"/>).
///
/// Mirrors the JSON-backed define-only path used by the rest of the vanilla
/// pool (e.g. <see cref="LoamLionFactory"/>'s base-shape construction, minus
/// the conditional pump; same posture as the merged
/// <see cref="HillGiantFactory"/>). CR 110 — a creature is a permanent; no
/// abilities means no further rules wiring is required.
///
/// NB: "Grizzly Bears" is deliberately KEPT in both the inline switch arm of
/// <see cref="NamedCardFactory"/> and
/// <see cref="ImplementedCardNames.InlineFallbackNames"/>, exactly as
/// <see cref="HillGiantFactory"/> does. That keeps
/// <see cref="ImplementedCardNames.HasRealFactory"/> returning <c>false</c>
/// for the name, so <c>GameFacade</c>'s "instance swap" rebuild is NOT
/// triggered for a directly-constructed Grizzly Bears shell — which would
/// otherwise replace a test's <c>new Creature("Grizzly Bears", "1G", 2, 2)</c>
/// with a JSON-built instance mid-cast and break the auto-tap cast path
/// (CastAutoTapSourcesTests). <c>IsImplemented</c> still reports true via the
/// inline-fallback set regardless.
/// </summary>
[CardName("Grizzly Bears")]
public static class GrizzlyBearsFactory
{
    public const string CardName = "Grizzly Bears";
    public const string Slug = "grizzly-bears";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Grizzly Bears from its embedded JSON definition. The card is
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
