using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gurmag Angler (Khans of Tarkir, {7}{B}).
///
/// Creature — Zombie Fish 5/5. Oracle text:
///   "Delve (Each card you exile from your graveyard while casting this
///    spell pays for {1}.)"
///
/// ## Implemented (v1)
/// - 5/5 Zombie Fish at {7}{B}.
/// - Delve marker <see cref="KeywordAbility"/>. The mechanic itself lives in
///   <see cref="Majik.Core.Costs.DelveCost"/> +
///   <see cref="Majik.Core.Game.SpellCastFlow"/>; the marker is on the card
///   so introspection (UI, bots) can see the keyword. Identical wiring to
///   <see cref="MurktideRegentFactory"/> minus the ETB trigger — Gurmag
///   Angler is a "vanilla delve creature" with no printed triggers or
///   activated abilities, so the factory ends right after the keyword
///   markers.
///
/// ## Bot-side discovery
/// - <see cref="Majik.Core.Players.Agents.DelveAltCostProbe"/> surfaces
///   Gurmag Angler to the heuristic bot's
///   <see cref="Majik.Core.Players.Agents.IAlternativeCostProbe"/> stream
///   via the Delve <see cref="KeywordAbility"/> marker. The probe yields a
///   <see cref="Majik.Core.Costs.DelveAlternativeCost"/> that reduces the
///   generic mana cost by the graveyard exiles the bot selects.
/// </summary>
[CardName("Gurmag Angler")]
public static class GurmagAnglerFactory
{
    /// <summary>
    /// Construct Gurmag Angler owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Gurmag Angler",
            manaCost: "{7}{B}",
            power: 5,
            toughness: 5,
            subtypes: new[] { CardSubtype.Zombie, CardSubtype.Fish });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.66 — Delve marker. The mechanic itself lives in DelveCost
        // + SpellCastFlow; the marker is here so introspection (UI, bots)
        // can see the keyword on the card.
        card.AddAbility(new KeywordAbility("Delve", card, owner));

        return card;
    }
}
