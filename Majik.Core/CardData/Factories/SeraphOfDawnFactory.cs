using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Seraph of Dawn (Dragon's Maze / M14, {2}{W}{W}).
///
/// Creature — Angel 2/4. Oracle text (verified against Scryfall):
///   "Flying, lifelink"
///
/// ## Shape source
/// Card identity (name, {2}{W}{W}, 2/4, Creature — Angel) is loaded from
/// <c>Majik.Core/CardData/Cards/seraph-of-dawn.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The keyword markers are attached in
/// code below — same keyword-only shell as the suggested analogue
/// <see cref="GiftedAetherbornFactory"/>, swapping Deathtouch+Lifelink for
/// Flying+Lifelink.
///
/// ## Implementation
/// - {2}{W}{W} 2/4 <see cref="Creature"/> — Angel, mana value 4, white
///   (CR 202.3 / CR 105.1).
/// - <b>Flying (CR 702.9)</b> and <b>Lifelink (CR 702.15)</b> attached as
///   <see cref="KeywordAbility"/> markers. The combat subsystem consumes
///   Flying (evasion / legal-blocker determination) and Lifelink (life gain
///   on damage dealt).
///
/// No triggers, no activated abilities — a clean keyword-only creature.
/// Single-arg <see cref="Create(Player)"/> is the canonical entry point.
/// </summary>
[CardName("Seraph of Dawn")]
public static class SeraphOfDawnFactory
{
    public const string CardName = "Seraph of Dawn";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("seraph-of-dawn");

    /// <summary>
    /// Constructs Seraph of Dawn — a {2}{W}{W} 2/4 Creature — Angel with
    /// Flying and Lifelink keyword markers.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying marker. The combat subsystem consumes this for
        // evasion / legal-blocker determination.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 702.15 — Lifelink marker. CombatAbilities.HasLifelink consumes
        // this for life-gain on combat damage.
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        return card;
    }
}
