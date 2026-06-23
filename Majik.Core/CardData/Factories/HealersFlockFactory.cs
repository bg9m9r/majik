using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Healer's Flock (Strixhaven, {W}{W}{W}).
///
/// Creature — Bird 3/3. Oracle text (verified against Scryfall):
///   "Flying, lifelink"
///
/// ## Shape source
/// Card identity (name, {W}{W}{W}, 3/3, Creature — Bird) is loaded from
/// <c>Majik.Core/CardData/Cards/healers-flock.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The keyword markers are attached in
/// code below — same keyword-only construction as the suggested analogue
/// <see cref="GiftedAetherbornFactory"/> (Lifelink keyword body), with Flying
/// in place of Deathtouch.
///
/// ## Implementation
/// - {W}{W}{W} 3/3 <see cref="Creature"/> — Bird, mana value 3,
///   white (CR 202.3 / CR 105.1).
/// - <b>Flying (CR 702.9)</b> and <b>Lifelink (CR 702.15)</b> attached as
///   <see cref="KeywordAbility"/> markers. Flying is consumed by the combat
///   blocking-legality rules; Lifelink by <see cref="Majik.Core.Combat.CombatAbilities"/>
///   for life gain on damage dealt.
///
/// No triggers, no activated abilities — a clean keyword-only creature.
/// Single-arg <see cref="Create(Player)"/> is the canonical entry point.
/// </summary>
[CardName("Healer's Flock")]
public static class HealersFlockFactory
{
    public const string CardName = "Healer's Flock";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("healers-flock");

    /// <summary>
    /// Constructs Healer's Flock — a {W}{W}{W} 3/3 Creature — Bird with
    /// Flying and Lifelink keyword markers.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying marker. Consumed by combat blocking-legality rules
        // (only creatures with flying or reach may block it).
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 702.15 — Lifelink marker. CombatAbilities.HasLifelink consumes
        // this for life-gain on damage dealt.
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        return card;
    }
}
