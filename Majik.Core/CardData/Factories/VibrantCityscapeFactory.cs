using Majik.Core.Cards;
using Majik.Core.CardData.Definitions;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Vibrant Cityscape — Bloomburrow common "fetch" land; a functional reprint of
/// Evolving Wilds / Terramorphic Expanse.
///
/// Oracle (verified against Scryfall 2026-06-24):
///   <c>{T}, Sacrifice this land: Search your library for a basic land card, put
///   it onto the battlefield tapped, then shuffle.</c>
///
/// ## Fully declarative
/// Same posture as <see cref="BantPanoramaFactory"/> (the paid-fetch Panorama
/// cycle) minus the extra generic {1} and minus the basic-subtype filter — every
/// piece is expressed by the embedded JSON definition
/// (<c>vibrant-cityscape.json</c>) + the shared <see cref="CardDefRuntime"/>
/// interpreter, no imperative wiring:
/// <list type="bullet">
///   <item><b>{T}, Sacrifice this land</b> — declarative <c>tap_self</c> +
///   <c>sacrifice_self</c> activation costs (CR 117.5 — paid as the ability
///   resolves, so this land is OFF the battlefield during the search;
///   CR 701.16 sacrifice via the live <c>ZoneService</c> so sac triggers
///   fire).</item>
///   <item><b>Search for a basic land onto the battlefield tapped, then
///   shuffle</b> — the declarative <c>search_library</c> verb
///   (<see cref="SearchLibraryEffectDef"/>) with the basic-land filter
///   (CR 205.4a — Basic supertype + Land card type, no subtype restriction) +
///   <c>destination: "battlefield_tapped"</c> + the default CR 701.20a shuffle
///   rider.</item>
/// </list>
///
/// Card identity (a nonbasic, colorless Land with no supertype/subtype that
/// produces no mana on its own; CR 305.6) is loaded from the JSON.
///
/// ## Deferred (matches every tutor)
/// - <b>Reveal event</b>: the tutored basic moves Library → Battlefield without
///   publishing a reveal event.
/// </summary>
[CardName("Vibrant Cityscape")]
public static class VibrantCityscapeFactory
{
    public const string CardName = "Vibrant Cityscape";
    public const string Slug = "vibrant-cityscape";

    /// <summary>Construct Vibrant Cityscape owned and controlled by
    /// <paramref name="owner"/> from its embedded JSON definition.</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Land)CardDefinitionFactory.Build(definition, owner);
    }
}
