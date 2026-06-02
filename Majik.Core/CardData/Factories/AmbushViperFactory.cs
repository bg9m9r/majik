using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ambush Viper (Dragons of Tarkir, {1}{G}).
///
/// Creature — Snake 2/1. Oracle text (verified against Scryfall):
///   "Flash
///    Deathtouch"
///
/// ## Shape source
/// Card identity (name, {1}{G}, 2/1, Creature — Snake) is loaded from
/// <c>Majik.Core/CardData/Cards/ambush-viper.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> — same posture as
/// <see cref="FaerieSeerFactory"/>. The two keyword markers are attached in
/// code below: the JSON ability schema does not yet express keyword markers.
///
/// ## Implemented (v1)
/// - 2/1 Creature — Snake at {1}{G}. Color identity green (derived from the
///   {G} pip per CR 202.2c). Mana value 2 (CR 202.3).
/// - <b>Flash</b> (CR 702.8): <see cref="KeywordAbility"/> marker read by
///   <c>TimingRules</c> to allow casting at instant speed — same wire-up
///   shape as <see cref="MysticSnakeFactory"/>.
/// - <b>Deathtouch</b> (CR 702.2): <see cref="KeywordAbility"/> marker read by
///   <c>Majik.Core.Combat.CombatAbilities.HasDeathtouch</c> for lethal-damage
///   determination — same wire-up shape as <see cref="PharikasChosenFactory"/>.
///
/// A keyword-only creature — no triggers, no activated abilities. Single-arg
/// <see cref="Create(Player)"/> is the canonical (and only) entry point.
/// </summary>
[CardName("Ambush Viper")]
public static class AmbushViperFactory
{
    public const string CardName = "Ambush Viper";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("ambush-viper");

    /// <summary>
    /// Constructs Ambush Viper — a {1}{G} 2/1 Creature — Snake with Flash and
    /// Deathtouch keyword markers.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash. Allows casting at instant speed. TimingRules
        // reads this marker.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // CR 702.2 — Deathtouch. CombatAbilities.HasDeathtouch consumes this
        // marker for lethal-damage determination.
        card.AddAbility(new KeywordAbility("Deathtouch", card, owner));

        return card;
    }
}
