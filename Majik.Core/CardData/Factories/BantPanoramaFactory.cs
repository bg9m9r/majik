using Majik.Core.Cards;
using Majik.Core.CardData.Definitions;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Bant Panorama — Alara Reborn / Conflux-era "Panorama" land. The Bant
/// (GWU) member of the five-card paid-fetch Panorama cycle.
///
/// Oracle (verified against Scryfall 2026-06-03):
///   <c>{T}: Add {C}.</c>
///   <c>{1}, {T}, Sacrifice this land: Search your library for a basic Forest,
///      Plains, or Island card, put it onto the battlefield tapped, then
///      shuffle.</c>
///
/// ## Fully declarative
/// As of the <c>search_library</c> tutor-verb deferral pay-down, this whole
/// card is expressed by the embedded JSON definition (<c>bant-panorama.json</c>)
/// + the shared <see cref="CardDefRuntime"/> interpreter — no imperative wiring:
/// <list type="bullet">
///   <item><b>{T}: Add {C}</b> — vanilla <c>mana</c> ability (CR 605.1 / CR
///   107.4c — {C} colorless modeled as +1 generic).</item>
///   <item><b>{1}, {T}, Sacrifice this land</b> — declarative <c>mana</c> +
///   <c>tap_self</c> + <c>sacrifice_self</c> activation costs (CR 117.5 — paid as
///   the ability resolves, so this land is OFF the battlefield during the
///   search; CR 701.16 sacrifice via the live <c>ZoneService</c> so sac triggers
///   fire).</item>
///   <item><b>Search for a basic Forest/Plains/Island onto the battlefield
///   tapped, then shuffle</b> — the declarative <c>search_library</c> verb
///   (<see cref="SearchLibraryEffectDef"/>) with the basic-land Forest/Plains/
///   Island filter (CR 205.4a) + <c>destination: "battlefield_tapped"</c> + the
///   default CR 701.20a shuffle rider.</item>
/// </list>
///
/// ## Deferred (matches every tutor)
/// - <b>Reveal event</b>: the tutored basic moves Library → Battlefield without
///   publishing a reveal event.
/// </summary>
[CardName("Bant Panorama")]
public static class BantPanoramaFactory
{
    public const string CardName = "Bant Panorama";
    public const string Slug = "bant-panorama";

    /// <summary>Construct Bant Panorama owned and controlled by
    /// <paramref name="owner"/> from its embedded JSON definition.</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Land)CardDefinitionFactory.Build(definition, owner);
    }
}
