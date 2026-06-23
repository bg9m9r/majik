using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Flensermite (New Phyrexia, {1}{B}).
///
/// Creature — Phyrexian Gremlin 1/1. Oracle text:
///   "Infect (This creature deals damage to creatures in the form of -1/-1
///    counters and to players in the form of poison counters.)
///    Lifelink (Damage dealt by this creature also causes you to gain that
///    much life.)"
///
/// ## Implementation
///
/// - {1}{B} 1/1 <see cref="Creature"/> — Phyrexian Gremlin, mana value 2,
///   black (CR 202.3 / CR 105.1). Phyrexian / Gremlin are creature subtypes
///   (CR 205.3m), not the Phyrexian mana symbol — Flensermite has no
///   Phyrexian-mana cost.
/// - <b>Infect (CR 702.90)</b> and <b>Lifelink (CR 702.15)</b> are carried
///   as the JSON definition's <c>keywords</c> entries;
///   <see cref="CardDefinitionFactory"/> attaches each as a plain
///   <see cref="Majik.Core.Abilities.KeywordAbility"/> marker. The combat /
///   damage pipeline consumes the markers: Lifelink routes dealt damage into
///   life gain for the controller, and Infect re-shapes that damage into
///   -1/-1 counters (creatures) / poison counters (players).
///
/// A clean keyword-only creature — no triggers, no activated abilities. The
/// JSON definition fully describes the card; this factory just loads it and
/// builds through <see cref="CardDefinitionFactory"/> (the same pattern as
/// <see cref="KnightOfMeadowgrainFactory"/>).
/// </summary>
[CardName("Flensermite")]
public static class FlensermiteFactory
{
    public const string CardName = "Flensermite";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("flensermite");

    /// <summary>
    /// Constructs Flensermite — a {1}{B} 1/1 Creature — Phyrexian Gremlin
    /// with Infect and Lifelink keyword markers.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return (Creature)CardDefinitionFactory.Build(Definition, owner);
    }
}
