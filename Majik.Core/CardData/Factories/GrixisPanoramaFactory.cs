using Majik.Core.Cards;
using Majik.Core.CardData.Definitions;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Grixis Panorama — the Grixis member of the Alara Reborn / Conflux paid-fetch
/// "Panorama" land cycle.
///
/// Oracle (verified against Scryfall 2026-06-03):
///   <c>{T}: Add {C}.</c>
///   <c>{1}, {T}, Sacrifice this land: Search your library for a basic Island, Swamp, or Mountain
///      card, put it onto the battlefield tapped, then shuffle.</c>
///
/// Fully declarative as of the <c>search_library</c> tutor-verb deferral
/// pay-down: the embedded JSON definition (<c>grixis-panorama.json</c>) expresses the
/// whole card via the shared <see cref="CardDefRuntime"/> interpreter — the
/// {T}: Add {C} mana ability, the {1} + <c>tap_self</c> + <c>sacrifice_self</c>
/// activation costs (CR 117.5 / CR 701.16), and the <c>search_library</c> verb
/// (<see cref="SearchLibraryEffectDef"/>) with the basic-land Island, Swamp, or Mountain filter
/// (CR 205.4a) + <c>destination: "battlefield_tapped"</c> + the CR 701.20a
/// shuffle rider. See <see cref="BantPanoramaFactory"/> for the cycle notes.
///
/// Deferred (matches every tutor): no reveal event on the Library → Battlefield
/// move.
/// </summary>
[CardName("Grixis Panorama")]
public static class GrixisPanoramaFactory
{
    public const string CardName = "Grixis Panorama";
    public const string Slug = "grixis-panorama";

    /// <summary>Construct Grixis Panorama owned and controlled by
    /// <paramref name="owner"/> from its embedded JSON definition.</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Land)CardDefinitionFactory.Build(definition, owner);
    }
}
