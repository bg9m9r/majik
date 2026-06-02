using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gifted Aetherborn (Aether Revolt, {B}{B}).
///
/// Creature — Aetherborn Vampire 2/3. Oracle text (verified against Scryfall):
///   "Deathtouch, lifelink"
///
/// ## Shape source
/// Card identity (name, {B}{B}, 2/3, Creature — Aetherborn Vampire) is loaded
/// from <c>Majik.Core/CardData/Cards/gifted-aetherborn.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The keyword markers are attached in
/// code below — same keyword-only Vampire shell as the suggested analogue
/// <see cref="VampireNighthawkFactory"/>, minus Flying.
///
/// ## Implementation
/// - {B}{B} 2/3 <see cref="Creature"/> — Aetherborn Vampire, mana value 2,
///   black (CR 202.3 / CR 105.1).
/// - <b>Deathtouch (CR 702.2)</b> and <b>Lifelink (CR 702.15)</b> attached as
///   <see cref="KeywordAbility"/> markers. <see cref="Majik.Core.Combat.CombatAbilities"/>
///   consumes Deathtouch (lethal-damage determination) and Lifelink (life gain
///   on damage dealt).
///
/// No triggers, no activated abilities — a clean keyword-only creature.
/// Single-arg <see cref="Create(Player)"/> is the canonical entry point.
/// </summary>
[CardName("Gifted Aetherborn")]
public static class GiftedAetherbornFactory
{
    public const string CardName = "Gifted Aetherborn";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("gifted-aetherborn");

    /// <summary>
    /// Constructs Gifted Aetherborn — a {B}{B} 2/3 Creature — Aetherborn
    /// Vampire with Deathtouch and Lifelink keyword markers.
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

        // CR 702.15 — Lifelink marker. CombatAbilities.HasLifelink
        // consumes this for life-gain on combat damage.
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        return card;
    }
}
