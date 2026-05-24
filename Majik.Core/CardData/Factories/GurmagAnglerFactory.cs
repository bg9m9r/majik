using Majik.Core.CardData.Definitions;
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
/// - Delve marker <see cref="Majik.Core.Abilities.KeywordAbility"/>. The
///   mechanic itself lives in <see cref="Majik.Core.Costs.DelveCost"/> +
///   <see cref="Majik.Core.Game.SpellCastFlow"/>.
///
/// Migrated to the fluent <see cref="CardDef"/> DSL.
///
/// ## Bot-side discovery
/// - <see cref="Majik.Core.Players.Agents.DelveAltCostProbe"/> surfaces
///   Gurmag Angler via the Delve <see cref="Majik.Core.Abilities.KeywordAbility"/>
///   marker.
/// </summary>
[CardName("Gurmag Angler")]
public static class GurmagAnglerFactory
{
    public static CardDef Define() => CardDef
        .Creature("Gurmag Angler", "{7}{B}", power: 5, toughness: 5)
        .WithSubtypes(CardSubtype.Zombie, CardSubtype.Fish)
        // CR 702.66 — Delve marker. The mechanic lives in DelveCost +
        // SpellCastFlow; the marker is here so introspection sees the
        // keyword on the card.
        .WithKeyword("Delve");

    public static Creature Create(Player owner) =>
        (Creature)CardDefRuntime.Build(Define(), owner);
}
