using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Orcish Bowmasters (The Lord of the Rings).
///
/// Creature — Orc Archer {1}{B} 1/1. Oracle text:
///   "Flash
///    When this creature enters and whenever an opponent draws a card except
///    the first one they draw in each of their draw steps, this creature deals
///    1 damage to any target. Then amass Orcs 1."
///
/// ## Implemented (v1)
/// - 1/1 Orc Archer at {1}{B}.
/// - Flash keyword (CR 702.8) via <see cref="Majik.Core.Abilities.KeywordAbility"/>.
///
/// Migrated to the fluent <see cref="CardDef"/> DSL — pure shape + Flash
/// marker.
///
/// ## Deferred (v1 gaps)
/// - <b>ETB damage trigger</b>: targeting prompt for any-target damage.
/// - <b>Opponent-draw watcher</b>: per-player draw-step ordinal tracking
///   across opponents.
/// - <b>Amass Orcs 1</b>: Army-token + token-upsizing infra.
/// </summary>
[CardName("Orcish Bowmasters")]
public static class OrcishBowmastersFactory
{
    public static CardDef Define() => CardDef
        .Creature("Orcish Bowmasters", "{1}{B}", power: 1, toughness: 1)
        .WithSubtypes(CardSubtype.Orc, CardSubtype.Archer)
        // CR 702.8 — Flash. TimingRules.CanCastAtInstantSpeed checks for
        // this keyword.
        .WithKeyword("Flash");

    public static Creature Create(Player owner) =>
        (Creature)CardDefRuntime.Build(Define(), owner);
}
