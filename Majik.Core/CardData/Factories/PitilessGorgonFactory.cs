using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pitiless Gorgon (Shadowmoor, {1}{B/G}{B/G}).
///
/// Creature — Gorgon 2/2. Oracle text (verified against Scryfall):
///   "Deathtouch"
///
/// ## Shape source
/// Card identity (name, {1}{B/G}{B/G}, 2/2, Creature — Gorgon) is loaded from
/// <c>Majik.Core/CardData/Cards/pitiless-gorgon.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The Deathtouch marker is attached in
/// code below — the same keyword-only Deathtouch shell as the suggested
/// analogue <see cref="GiftedAetherbornFactory"/>, minus Lifelink.
///
/// ## Implementation
/// - {1}{B/G}{B/G} 2/2 <see cref="Creature"/> — Gorgon, mana value 3. The two
///   {B/G} hybrid pips make the card both black and green (CR 105.1 /
///   CR 202.2c); <see cref="Majik.Core.Cards.CardColors"/> derives both colours
///   from the hybrid pips.
/// - <b>Deathtouch (CR 702.2)</b> attached as a <see cref="KeywordAbility"/>
///   marker. <see cref="Majik.Core.Combat.CombatAbilities"/> consumes it for
///   lethal-damage determination.
///
/// No triggers, no activated abilities — a clean keyword-only creature.
/// Single-arg <see cref="Create(Player)"/> is the canonical entry point.
/// </summary>
[CardName("Pitiless Gorgon")]
public static class PitilessGorgonFactory
{
    public const string CardName = "Pitiless Gorgon";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("pitiless-gorgon");

    /// <summary>
    /// Constructs Pitiless Gorgon — a {1}{B/G}{B/G} 2/2 Creature — Gorgon with
    /// a Deathtouch keyword marker.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.2 — Deathtouch marker. CombatAbilities.HasDeathtouch
        // consumes this for lethal-damage determination.
        card.AddAbility(new KeywordAbility("Deathtouch", card, owner));

        return card;
    }
}
